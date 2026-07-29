using boot_portal.Models;
using boot_portal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace boot_portal.Controllers;

[ApiController]
[Route("api/dashboard/v1")]
public sealed class DashboardController : ControllerBase
{
    private readonly BootProtocolStateService _stateService;
    private readonly DashboardReadModelService _dashboard;

    public DashboardController(
        BootProtocolStateService stateService,
        DashboardReadModelService dashboard)
    {
        _stateService = stateService;
        _dashboard = dashboard;
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("summary")]
    public IActionResult GetSummary([FromQuery] string? window = "24h") =>
        Ok(_dashboard.BuildSummary(window));

    [EnableRateLimiting("network-read")]
    [HttpGet("history")]
    public IActionResult GetHistory([FromQuery] string? window = "24h") =>
        Ok(_dashboard.BuildHistory(window));

    [EnableRateLimiting("network-read")]
    [HttpGet("address/{address}")]
    public IActionResult GetAddress(string address)
    {
        try
        {
            return Ok(_dashboard.BuildAddress(address));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            return BadRequest(new
            {
                status = "rejected",
                reason = "Address is not valid for this node's Bitcoin network."
            });
        }
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("operator")]
    public IActionResult GetOperator()
    {
        string? apiKey = Request.Headers["X-Boot-Admin-Key"].FirstOrDefault();
        if (!_stateService.IsAdminAuthorized(apiKey))
        {
            return Unauthorized(new { status = "rejected", reason = "Missing or invalid admin key" });
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(_dashboard.BuildOperator());
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("schema")]
    public IActionResult GetSchema() =>
        Ok(new
        {
            schemaVersion = 1,
            endpoints = new
            {
                summary = "/api/dashboard/v1/summary?window=24h",
                history = "/api/dashboard/v1/history?window=24h",
                address = "/api/dashboard/v1/address/{address}",
                @operator = "/api/dashboard/v1/operator"
            },
            windows = DashboardWindows.Supported.Keys,
            realtime = new
            {
                hub = "/dashboardHub",
                method = "DashboardChanged",
                payload = new[] { "revision", "timestampUtc", "topics" }
            },
            authentication = new
            {
                operatorHeader = "X-Boot-Admin-Key",
                storageGuidance = "Keep operator credentials in memory only."
            }
        });
}
