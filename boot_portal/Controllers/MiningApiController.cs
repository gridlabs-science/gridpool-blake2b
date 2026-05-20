using Microsoft.AspNetCore.Mvc;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;
using Microsoft.AspNetCore.RateLimiting;

namespace boot_portal.Controllers;

[ApiController]
[Route("api/mining")]
public class MiningApiController : ControllerBase
{
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly ILogger<MiningApiController> _logger;

    public MiningApiController(PoolConfig poolConfig, BootProtocolStateService stateService, ILogger<MiningApiController> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _logger = logger;
    }

    // GET: api/mining/payouts
    // Returns the current list of winners (the required coinbase outputs)
    [EnableRateLimiting("network-read")]
    [HttpGet("payouts")]
    public IActionResult GetPayouts()
    {
        return Ok(_stateService.GetPayoutResponse());
    }

    // GET: api/mining/share-advice
    // Returns the current minimum difficulty needed for a direct client share to affect the on-deck list.
    [EnableRateLimiting("network-read")]
    [HttpGet("share-advice")]
    public IActionResult GetShareAdvice()
    {
        return Ok(_stateService.GetShareAdviceResponse());
    }

    // POST: api/mining/share
    // Receives a high-difficulty share
    [EnableRateLimiting("mining-write")]
    [HttpPost("share")]
    public async Task<IActionResult> SubmitShare([FromBody] ShareSubmissionDto? share)
    {
        if (share == null)
        {
            return BadRequest(new { status = "rejected", reason = "Missing share payload" });
        }

        BootRequestValidationFailure? requestValidation = BootRequestGuards.ValidateShareRequest(
            _poolConfig,
            Request,
            share.MinerAddress,
            share.HeaderHex,
            share.CoinbaseHex,
            share.MerklePath);
        if (requestValidation.HasValue)
        {
            return StatusCode(requestValidation.Value.StatusCode, new { status = "rejected", reason = requestValidation.Value.Reason });
        }

        try
        {
            var result = await _stateService.SubmitShareAsync(new RecordedShareSubmission
            {
                MinerAddress = share.MinerAddress,
                Username = string.IsNullOrWhiteSpace(share.Username) ? string.Empty : share.Username,
                HeaderHex = share.HeaderHex,
                CoinbaseHex = share.CoinbaseHex,
                MerklePath = share.MerklePath,
                PrevBlockHash = share.PrevBlockHash,
                Difficulty = share.Difficulty,
                Source = "http"
            }, "http-block");

            if (result.Accepted || string.Equals(result.RejectionReason, "Duplicate share", StringComparison.Ordinal))
            {
                return Ok(new
                {
                    status = result.Accepted ? "accepted" : "duplicate",
                    difficulty = result.ComputedDifficulty,
                    isBlock = result.IsBlock,
                    blockHash = result.BlockHash,
                    stateId = result.IsBlock
                        ? result.NetworkStatus.CurrentStateId
                        : result.NetworkStatus.CandidateStateId
                });
            }
            else
            {
                // In production, don't be too verbose about WHY it failed to prevent gaming
                return BadRequest(new { status = "rejected", reason = result.RejectionReason ?? "Low difficulty or invalid proof" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing API share");
            return StatusCode(500, "Internal Server Error");
        }
    }
}
