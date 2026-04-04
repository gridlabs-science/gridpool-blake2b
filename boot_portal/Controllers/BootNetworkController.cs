using boot_portal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace boot_portal.Controllers;

[ApiController]
[Route("api/network")]
public class BootNetworkController : ControllerBase
{
    private readonly BootProtocolStateService _stateService;
    private readonly ILogger<BootNetworkController> _logger;

    public BootNetworkController(BootProtocolStateService stateService, ILogger<BootNetworkController> logger)
    {
        _stateService = stateService;
        _logger = logger;
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        return Ok(_stateService.GetNetworkStatus());
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

    [EnableRateLimiting("admin-write")]
    [HttpPost("admin/reset")]
    public async Task<IActionResult> ResetRound()
    {
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
}
