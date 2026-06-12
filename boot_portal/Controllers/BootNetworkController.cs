using boot_portal.Services;
using boot_portal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace boot_portal.Controllers;

[ApiController]
[Route("api/network")]
public class BootNetworkController : ControllerBase
{
    public const string PeerEndpointHeader = "X-Boot-Peer-Endpoint";
    public const string PeerProtocolVersionHeader = "X-Boot-Protocol-Version";
    public const string PeerNetworkIdHeader = "X-Boot-Network-Id";

    private readonly BootProtocolStateService _stateService;
    private readonly PoolConfig _poolConfig;
    private readonly ILogger<BootNetworkController> _logger;

    public BootNetworkController(PoolConfig poolConfig, BootProtocolStateService stateService, ILogger<BootNetworkController> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _logger = logger;
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        RememberAnnouncedPeer();
        return Ok(_stateService.GetNetworkStatus());
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("launch-readiness")]
    public IActionResult GetLaunchReadiness()
    {
        return Ok(_stateService.GetNetworkStatus().LaunchReadiness);
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("peer-addresses")]
    public IActionResult GetPeerAddresses([FromQuery] int limit = 128)
    {
        RememberAnnouncedPeer();
        return Ok(_stateService.GetPeerAddressBook(limit));
    }

    private void RememberAnnouncedPeer()
    {
        if (!_poolConfig.EnablePeerSync)
        {
            return;
        }

        string? endpoint = Request.Headers[PeerEndpointHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        string? protocolVersionText = Request.Headers[PeerProtocolVersionHeader].FirstOrDefault();
        string? networkId = Request.Headers[PeerNetworkIdHeader].FirstOrDefault();
        if (!int.TryParse(protocolVersionText, out int protocolVersion) ||
            !_stateService.IsCompatiblePeerNetwork(protocolVersion, networkId ?? string.Empty))
        {
            return;
        }

        _stateService.AnnouncePeer(endpoint);
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("state/{stateId}")]
    public IActionResult GetStateBundle(string stateId)
    {
        var bundle = _stateService.GetStateBundle(stateId);
        return bundle == null ? NotFound() : Ok(bundle);
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("history")]
    public IActionResult GetHistory([FromQuery] int limit = 24)
    {
        return Ok(_stateService.GetRoundHistory(limit));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("history/{stateId}")]
    public IActionResult GetHistoryEntry(string stateId)
    {
        var entry = _stateService.GetRoundHistoryEntry(stateId);
        return entry == null ? NotFound() : Ok(entry);
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("hashrate")]
    public IActionResult GetHashrateSeries([FromQuery] string? window = "24h")
    {
        return Ok(_stateService.GetHashrateSeries(window));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("local-miners")]
    public IActionResult GetLocalDatumMiners(
        [FromQuery] string? address = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? window = "24h")
    {
        return Ok(_stateService.GetLocalDatumMinerSummaries(address, limit, window));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("share-diagnostics")]
    public IActionResult GetShareDiagnostics(
        [FromQuery] string? window = "12h",
        [FromQuery] string? source = "datum",
        [FromQuery] bool? accepted = false,
        [FromQuery] int limit = 500,
        [FromQuery] string? minerAddress = null,
        [FromQuery] string? category = null)
    {
        return Ok(_stateService.GetShareDiagnostics(window, source, accepted, limit, minerAddress, category));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("events")]
    public IActionResult GetNetworkEvents(
        [FromQuery] string? window = "12h",
        [FromQuery] int limit = 500,
        [FromQuery] string? eventType = null,
        [FromQuery] string? source = null)
    {
        return Ok(_stateService.GetNetworkEvents(window, limit, eventType, source));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("peer-relay-latency")]
    public IActionResult GetPeerRelayLatency(
        [FromQuery] string? window = "12h",
        [FromQuery] int limit = 500,
        [FromQuery] string? remoteEndpoint = null,
        [FromQuery] string? transport = null)
    {
        return Ok(_stateService.GetPeerRelayLatency(window, limit, remoteEndpoint, transport));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("coinbaser-diagnostics")]
    public IActionResult GetCoinbaserDiagnostics(
        [FromQuery] string? window = "12h",
        [FromQuery] int limit = 500,
        [FromQuery] string? remoteEndpoint = null,
        [FromQuery] bool? temporarySlotZero = null)
    {
        return Ok(_stateService.GetCoinbaserDiagnostics(window, limit, remoteEndpoint, temporarySlotZero));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("datum-share-responses")]
    public IActionResult GetDatumShareResponses(
        [FromQuery] string? window = "12h",
        [FromQuery] int limit = 500,
        [FromQuery] string? remoteEndpoint = null,
        [FromQuery] bool? accepted = null,
        [FromQuery] string? reason = null)
    {
        return Ok(_stateService.GetDatumShareResponses(window, limit, remoteEndpoint, accepted, reason));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("datum-sessions")]
    public IActionResult GetDatumSessions(
        [FromQuery] string? window = "12h",
        [FromQuery] int limit = 500,
        [FromQuery] string? remoteEndpoint = null,
        [FromQuery] bool? active = null,
        [FromQuery] string? protocol = null)
    {
        return Ok(_stateService.GetDatumSessions(window, limit, remoteEndpoint, active, protocol));
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("datum-protocol-events")]
    public IActionResult GetDatumProtocolEvents(
        [FromQuery] string? window = "12h",
        [FromQuery] int limit = 500,
        [FromQuery] string? sessionId = null,
        [FromQuery] string? remoteEndpoint = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? messageLabel = null)
    {
        return Ok(_stateService.GetDatumProtocolEvents(window, limit, sessionId, remoteEndpoint, eventType, direction, messageLabel));
    }

    [EnableRateLimiting("admin-write")]
    [HttpPost("admin/reset")]
    public async Task<IActionResult> ResetRound()
    {
        if (!_poolConfig.EnableAdminApi)
        {
            return NotFound();
        }

        string? apiKey = Request.Headers["X-Boot-Admin-Key"].FirstOrDefault();
        if (!_stateService.IsAdminAuthorized(apiKey))
        {
            return Unauthorized(new { status = "rejected", reason = "Missing or invalid admin key" });
        }

        var result = await _stateService.RotateToNextRoundAsync(string.Empty, "manual-reset", manual: true);
        _logger.LogWarning("Manual round reset triggered via admin API. New state: {StateId}", result.NetworkStatus.CurrentStateId);
        return Ok(result);
    }

    [EnableRateLimiting("admin-write")]
    [HttpPost("admin/reset-genesis")]
    public async Task<IActionResult> ResetHistoryToGenesis()
    {
        if (!_poolConfig.EnableAdminApi)
        {
            return NotFound();
        }

        string? apiKey = Request.Headers["X-Boot-Admin-Key"].FirstOrDefault();
        if (!_stateService.IsAdminAuthorized(apiKey))
        {
            return Unauthorized(new { status = "rejected", reason = "Missing or invalid admin key" });
        }

        var status = await _stateService.ResetHistoryToGenesisAsync();
        _logger.LogWarning(
            "Genesis history reset triggered via admin API. New state: {StateId}, round: {RoundNumber}",
            status.CurrentStateId,
            status.CurrentRoundNumber);
        return Ok(status);
    }

    [EnableRateLimiting("admin-write")]
    [HttpPost("admin/peer/tombstone")]
    public IActionResult TombstonePeer([FromBody] BootPeerTombstoneRequest request)
    {
        if (!_poolConfig.EnableAdminApi)
        {
            return NotFound();
        }

        string? apiKey = Request.Headers["X-Boot-Admin-Key"].FirstOrDefault();
        if (!_stateService.IsAdminAuthorized(apiKey))
        {
            return Unauthorized(new { status = "rejected", reason = "Missing or invalid admin key" });
        }

        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return BadRequest(new { status = "rejected", reason = "Missing peer endpoint" });
        }

        bool removed = _stateService.TombstonePeer(request.Endpoint);
        if (!removed)
        {
            return NotFound(new { status = "not-found", endpoint = request.Endpoint });
        }

        _logger.LogWarning("Peer endpoint manually tombstoned via admin API: {Endpoint}", request.Endpoint);
        return Ok(new { status = "removed", endpoint = request.Endpoint });
    }
}
