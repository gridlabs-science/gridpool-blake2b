using System.Net;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace boot_portal.Controllers;

[ApiController]
[Route("api/peer")]
[EnableRateLimiting("peer-write")]
public class BootPeerController : ControllerBase
{
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly ILogger<BootPeerController> _logger;

    public BootPeerController(PoolConfig poolConfig, BootProtocolStateService stateService, ILogger<BootPeerController> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _logger = logger;
    }

    [HttpPost("share")]
    public async Task<IActionResult> SubmitPeerShare([FromBody] PeerShareAnnouncement announcement)
    {
        if (announcement?.Share == null)
        {
            return BadRequest(new { status = "rejected", reason = "Missing share payload" });
        }

        BootRequestValidationFailure? requestValidation = BootRequestGuards.ValidateShareRequest(
            _poolConfig,
            Request,
            announcement.Share.MinerAddress,
            announcement.Share.HeaderHex,
            announcement.Share.CoinbaseHex,
            announcement.Share.MerklePath);
        if (requestValidation.HasValue)
        {
            return StatusCode(requestValidation.Value.StatusCode, new { status = "rejected", reason = requestValidation.Value.Reason });
        }

        if (!_stateService.IsCompatiblePeerNetwork(announcement.ProtocolVersion, announcement.NetworkId))
        {
            return BadRequest(new { status = "rejected", reason = "Network mismatch" });
        }

        string senderEndpoint = string.IsNullOrWhiteSpace(announcement.SenderEndpoint)
            ? string.Empty
            : announcement.SenderEndpoint;

        if (!string.IsNullOrWhiteSpace(senderEndpoint))
        {
            _stateService.MergeDiscoveredPeers([senderEndpoint]);
            _stateService.UpdatePeerHeartbeat(senderEndpoint, "share", null, DateTime.UtcNow);
        }

        var result = await _stateService.SubmitShareAsync(new RecordedShareSubmission
        {
            MinerAddress = announcement.Share.MinerAddress,
            Username = string.IsNullOrWhiteSpace(announcement.Share.Username) ? string.Empty : announcement.Share.Username,
            HeaderHex = announcement.Share.HeaderHex,
            CoinbaseHex = announcement.Share.CoinbaseHex,
            MerklePath = announcement.Share.MerklePath,
            PrevBlockHash = announcement.Share.PrevBlockHash,
            Difficulty = announcement.Share.Difficulty,
            Source = string.IsNullOrWhiteSpace(senderEndpoint) ? "peer" : $"peer:{senderEndpoint}"
        }, "peer-block");

        if (!result.Accepted &&
            !string.IsNullOrWhiteSpace(result.RejectionReason) &&
            result.RejectionReason.StartsWith("Share builds on the wrong parent block", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(senderEndpoint) &&
            !string.IsNullOrWhiteSpace(announcement.Share.PrevBlockHash))
        {
            await _stateService.ObserveChainTipAsync(
                announcement.Share.PrevBlockHash,
                $"peer-share:{senderEndpoint}");

            result = await _stateService.SubmitShareAsync(new RecordedShareSubmission
            {
                MinerAddress = announcement.Share.MinerAddress,
                Username = string.IsNullOrWhiteSpace(announcement.Share.Username) ? string.Empty : announcement.Share.Username,
                HeaderHex = announcement.Share.HeaderHex,
                CoinbaseHex = announcement.Share.CoinbaseHex,
                MerklePath = announcement.Share.MerklePath,
                PrevBlockHash = announcement.Share.PrevBlockHash,
                Difficulty = announcement.Share.Difficulty,
                Source = string.IsNullOrWhiteSpace(senderEndpoint) ? "peer" : $"peer:{senderEndpoint}"
            }, "peer-block");
        }

        if (!result.Accepted && !string.Equals(result.RejectionReason, "Duplicate share", StringComparison.Ordinal))
        {
            _logger.LogWarning("Rejected peer share from {SenderEndpoint}: {Reason}", senderEndpoint, result.RejectionReason);
            return BadRequest(new { status = "rejected", reason = result.RejectionReason });
        }

        return Ok(new
        {
            status = result.Accepted ? "accepted" : "duplicate",
            reason = result.RejectionReason,
            stateId = result.IsBlock
                ? result.NetworkStatus.CurrentStateId
                : result.NetworkStatus.CandidateStateId,
            difficulty = result.ComputedDifficulty,
            isBlock = result.IsBlock,
            blockHash = result.BlockHash
        });
    }
}
