using Microsoft.AspNetCore.Mvc;
using boot_portal.HostedServices;
using boot_portal.Models;

namespace boot_portal.Controllers;

[ApiController]
[Route("api/mining")]
public class MiningApiController : ControllerBase
{
    private readonly ILogger<MiningApiController> _logger;

    public MiningApiController(ILogger<MiningApiController> logger)
    {
        _logger = logger;
    }

    // GET: api/mining/payouts
    // Returns the current list of winners (the required coinbase outputs)
    [HttpGet("payouts")]
    public IActionResult GetPayouts()
    {
        // Accessing static state from DatumServer
        // Note: In a production app, we might use a dedicated Service for state, 
        // but for this architecture, accessing the static lists is efficient.
        
        var response = new PayoutResponseDto
        {
            // You might want to add a Sequence/Version number to DatumServer state later
            Sequence = DateTime.UtcNow.Ticks, 
            Payouts = DatumServer.WinnersList
        };

        return Ok(response);
    }

    // POST: api/mining/share
    // Receives a high-difficulty share
    [HttpPost("share")]
    public async Task<IActionResult> SubmitShare([FromBody] ShareSubmissionDto share)
    {
        if (string.IsNullOrEmpty(share.HeaderHex) || string.IsNullOrEmpty(share.MinerAddress))
        {
            return BadRequest("Invalid share data");
        }

        try
        {
            // We delegate the logic to DatumServer to keep thread safety in one place
            bool isValid = await DatumServer.ProcessApiShareAsync(share);

            if (isValid)
            {
                return Ok(new { status = "accepted", difficulty = share.Difficulty });
            }
            else
            {
                // In production, don't be too verbose about WHY it failed to prevent gaming
                return BadRequest(new { status = "rejected", reason = "Low difficulty or invalid proof" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing API share");
            return StatusCode(500, "Internal Server Error");
        }
    }
}