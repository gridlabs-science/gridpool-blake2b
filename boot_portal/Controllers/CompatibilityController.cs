using System.Net;
using System.Text;
using System.Text.Json;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace boot_portal.Controllers;

[ApiController]
public class CompatibilityController : ControllerBase
{
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly ILogger<CompatibilityController> _logger;

    public CompatibilityController(
        PoolConfig poolConfig,
        BootProtocolStateService stateService,
        ILogger<CompatibilityController> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _logger = logger;
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("api/compat/summary")]
    public IActionResult GetSummary()
    {
        return Ok(BuildSummary());
    }

    [EnableRateLimiting("network-read")]
    [HttpGet("compat")]
    public ContentResult GetPage()
    {
        string html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>GridPool Compatibility Test</title>
  <style>
    :root { color-scheme: dark; --bg:#06080a; --panel:#111820; --line:#263442; --text:#edf4f8; --muted:#8ea0ad; --accent:#49d391; --warn:#ffd166; --bad:#ff6b6b; }
    body { margin:0; font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif; background: radial-gradient(circle at top left, #173126, #06080a 38rem); color:var(--text); }
    main { max-width:1120px; margin:0 auto; padding:32px 18px 64px; }
    h1 { font-size:clamp(2rem, 5vw, 4rem); line-height:1; margin:0 0 10px; }
    p { color:var(--muted); line-height:1.55; }
    .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(240px,1fr)); gap:14px; margin:24px 0; }
    .card { background:rgba(17,24,32,.86); border:1px solid var(--line); border-radius:18px; padding:18px; box-shadow:0 18px 60px rgba(0,0,0,.25); }
    .label { color:var(--muted); font-size:.78rem; text-transform:uppercase; letter-spacing:.12em; }
    .value { font-size:1.2rem; font-weight:750; margin-top:5px; overflow-wrap:anywhere; }
    code { background:#071015; border:1px solid var(--line); border-radius:8px; padding:2px 6px; color:#c7f7df; }
    table { width:100%; border-collapse:collapse; margin-top:16px; font-size:.92rem; }
    th, td { border-bottom:1px solid var(--line); padding:10px 8px; text-align:left; vertical-align:top; }
    th { color:var(--muted); font-size:.75rem; text-transform:uppercase; letter-spacing:.08em; }
    .ok { color:var(--accent); } .warn { color:var(--warn); } .bad { color:var(--bad); }
    .pill { display:inline-block; padding:3px 8px; border-radius:999px; background:#0d2430; border:1px solid var(--line); }
  </style>
</head>
<body>
<main>
  <h1>Full-Coinbase Firmware Test</h1>
  <p>This testnet-only endpoint serves large uncondensed GridPool payout templates. It is for checking whether firmware, Stratum gateways, or rental services can handle a mature 300-slot GridPool coinbase.</p>
  <section class="grid">
    <div class="card"><div class="label">Stratum endpoint</div><div class="value" id="endpoint">Loading...</div></div>
    <div class="card"><div class="label">Template mode</div><div class="value" id="mode">Loading...</div></div>
    <div class="card"><div class="label">Unsafe override password</div><div class="value"><code id="unsafe">Loading...</code></div></div>
  </section>
  <section class="card">
    <h2>How to test</h2>
    <p>Use username format <code>testerTag.workerName</code>. The payout address is fixed by this test endpoint; do not use this to test payouts. Use any normal password for safe mode. If DATUM fingerprints your firmware as too small, it will refuse to send large work. To intentionally test anyway, set the Stratum password to include the unsafe override phrase shown above.</p>
    <p class="warn">Unsafe mode can hard-lock some firmware. Use it only when you can recover the miner.</p>
  </section>
  <section class="card">
    <h2>Observed clients</h2>
    <p id="status">Loading telemetry...</p>
    <table>
      <thead><tr><th>Tester</th><th>Worker</th><th>Status</th><th>Coinbase</th><th>Unsafe</th><th>Accepted</th><th>Rejected</th><th>User agent</th></tr></thead>
      <tbody id="clients"></tbody>
    </table>
    <h3>Recent compatibility events</h3>
    <p>Fast disconnects may appear here even when the client is gone before the DATUM live client list refreshes.</p>
    <table>
      <thead><tr><th>Time</th><th>Event</th><th>Fingerprint</th><th>Forced</th><th>User agent</th></tr></thead>
      <tbody id="events"></tbody>
    </table>
  </section>
</main>
<script>
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, c => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", "\"":"&quot;", "'":"&#39;" }[c]));
async function refresh() {
  const res = await fetch("/api/compat/summary", { cache: "no-store" });
  const data = await res.json();
  document.getElementById("endpoint").textContent = data.stratumEndpoint || "Not configured";
  document.getElementById("mode").textContent = data.uncondensedOutputsEnabled ? "Uncondensed 300-output stress mode" : "Not in stress mode";
  document.getElementById("unsafe").textContent = data.unsafeOverridePhrase || "UNSAFE_FULL_COINBASE";
  const status = document.getElementById("status");
  const clients = Array.isArray(data.telemetry?.clients) ? data.telemetry.clients : [];
  status.textContent = data.status === "ok" ? `Telemetry updated ${data.telemetryUpdatedUtc || "recently"}; ${clients.length} client(s) observed.` : (data.warning || data.status);
  const tbody = document.getElementById("clients");
  tbody.innerHTML = clients.map(c => `<tr>
    <td>${esc(c.testerTag || "unknown")}</td>
    <td>${esc(c.workerName || "")}</td>
    <td><span class="pill">${esc(c.status || "")}</span></td>
    <td>${esc(c.coinbaseClass || "")}</td>
    <td class="${c.unsafeFullCoinbaseOverride ? "warn" : "ok"}">${c.unsafeFullCoinbaseOverride ? "yes" : "no"}</td>
    <td>${esc(c.acceptedShareCount ?? 0)}</td>
    <td>${esc(c.rejectedShareCount ?? 0)}</td>
    <td>${esc(c.userAgent || "")}</td>
  </tr>`).join("");
  const events = Array.isArray(data.telemetry?.recentEvents) ? data.telemetry.recentEvents.slice(-25).reverse() : [];
  const eventBody = document.getElementById("events");
  eventBody.innerHTML = events.map(e => `<tr>
    <td>${esc(e.timestamp || "")}</td>
    <td><span class="pill">${esc(e.type || "")}</span></td>
    <td>${esc(e.fingerprintedClass ?? "")}</td>
    <td>${esc(e.forcedClass ?? "")}</td>
    <td>${esc(e.userAgent || "")}</td>
  </tr>`).join("");
}
refresh();
setInterval(refresh, 15000);
</script>
</body>
</html>
""";
        return Content(html, "text/html", Encoding.UTF8);
    }

    private CompatibilitySummaryDto BuildSummary()
    {
        BootNetworkStatusDto network = _stateService.GetNetworkStatus();
        string telemetryPath = ResolveTelemetryPath();
        CompatibilitySummaryDto dto = new()
        {
            Enabled = _poolConfig.CompatibilityPageEnabled,
            Status = _poolConfig.CompatibilityPageEnabled ? "ok" : "disabled",
            NetworkId = network.NetworkId,
            BitcoinNetwork = network.BitcoinNetwork,
            UncondensedOutputsEnabled = _poolConfig.CoinbaseUncondensedOutputsEnabled,
            StratumEndpoint = ResolveStratumEndpoint(),
            UnsafeOverridePhrase = string.IsNullOrWhiteSpace(_poolConfig.CompatibilityUnsafeOverridePhrase)
                ? "UNSAFE_FULL_COINBASE"
                : _poolConfig.CompatibilityUnsafeOverridePhrase.Trim()
        };

        if (!_poolConfig.CompatibilityPageEnabled)
        {
            dto.Warning = "Compatibility page is disabled.";
            return dto;
        }

        if (!System.IO.File.Exists(telemetryPath))
        {
            dto.Status = "telemetry-offline";
            dto.Warning = $"No compatibility telemetry file found at {telemetryPath}.";
            return dto;
        }

        try
        {
            string json = System.IO.File.ReadAllText(telemetryPath);
            using JsonDocument document = JsonDocument.Parse(json);
            dto.Telemetry = document.RootElement.Clone();
            dto.TelemetryUpdatedUtc = System.IO.File.GetLastWriteTimeUtc(telemetryPath);
            dto.Status = "ok";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read compatibility telemetry from {Path}", telemetryPath);
            dto.Status = "telemetry-error";
            dto.Warning = "Compatibility telemetry exists but could not be parsed.";
        }

        return dto;
    }

    private string ResolveTelemetryPath()
    {
        if (!string.IsNullOrWhiteSpace(_poolConfig.CompatibilityTelemetryPath))
        {
            return Path.GetFullPath(_poolConfig.CompatibilityTelemetryPath.Trim());
        }

        string directory = Path.GetDirectoryName(BootPortalPaths.PoolStateFilePath) ?? string.Empty;
        return Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, "compatibility_status.json");
    }

    private string ResolveStratumEndpoint()
    {
        string host = string.IsNullOrWhiteSpace(_poolConfig.CompatibilityStratumPublicHost)
            ? _poolConfig.DatumPublicHost
            : _poolConfig.CompatibilityStratumPublicHost;
        int port = _poolConfig.CompatibilityStratumPublicPort > 0
            ? _poolConfig.CompatibilityStratumPublicPort
            : 3334;
        return string.IsNullOrWhiteSpace(host)
            ? string.Empty
            : $"{host.Trim()}:{port}";
    }
}
