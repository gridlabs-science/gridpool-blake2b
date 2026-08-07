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
    private readonly NodeSetupState _setupState;

    public HealthController(
        PoolConfig poolConfig,
        BootProtocolStateService stateService,
        NodeSetupState setupState)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _setupState = setupState;
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
        if (!_setupState.OperationalAtStartup)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = _setupState.RestartRequired ? "restart_required" : "setup_required",
                miningEnabled = false,
                restartRequired = _setupState.RestartRequired,
                timeUtc = DateTime.UtcNow
            });
        }

        BootNetworkStatusDto network = _stateService.GetNetworkStatus();
        bool baseReady = network.WinnersCount > 0 && !string.IsNullOrWhiteSpace(network.CurrentStateId);
        bool peerReady = (!_poolConfig.EnablePeerSync || network.PeerLoopsHealthy) && !network.IdentityChanged;
        bool bitcoinReady = network.BitcoinNotification.MiningSafe;
        bool ready = baseReady && peerReady && bitcoinReady;
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
            bitcoinNotification = network.BitcoinNotification,
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
