using boot_portal.Models;
using boot_portal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace boot_portal.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;

    public HealthController(PoolConfig poolConfig, BootProtocolStateService stateService)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
    }

    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new
        {
            status = "live",
            timeUtc = DateTime.UtcNow
        });
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("ready")]
    public IActionResult Ready()
    {
        BootNetworkStatusDto network = _stateService.GetNetworkStatus();
        bool baseReady = network.WinnersCount > 0 && !string.IsNullOrWhiteSpace(network.CurrentStateId);
        bool peerReady = (!_poolConfig.EnablePeerSync || network.PeerLoopsHealthy) && !network.IdentityChanged;
        bool ready = baseReady && peerReady;
        int statusCode = ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        return StatusCode(statusCode, new
        {
            status = ready ? "ready" : baseReady ? "degraded" : "starting",
            network.ProtocolVersion,
            network.NetworkId,
            network.CurrentStateId,
            network.CandidateStateId,
            network.CurrentTipBlockHash,
            network.WinnersCount,
            network.OnDeckCount,
            network.PeerCount,
            peerSyncEnabled = _poolConfig.EnablePeerSync,
            network.PeerLoopsHealthy,
            network.IdentityChanged,
            network.OutboundRelayHealthy,
            network.LastPeerPollCompletedUtc,
            network.LastShareRelayDequeuedUtc,
            network.LastSuccessfulOutboundRelayUtc,
            network.ShareRelayQueueDepth,
            network.ConfigWarnings,
            publicEndpoint = network.SelfEndpoint,
            timeUtc = DateTime.UtcNow
        });
    }
}
