using System.Text.Json;

namespace boot_portal.Models;

public class CompatibilitySummaryDto
{
    public bool Enabled { get; set; }
    public string Status { get; set; } = "unconfigured";
    public string NetworkId { get; set; } = string.Empty;
    public string BitcoinNetwork { get; set; } = string.Empty;
    public bool UncondensedOutputsEnabled { get; set; }
    public string StratumEndpoint { get; set; } = string.Empty;
    public string UnsafeOverridePhrase { get; set; } = "UNSAFE_FULL_COINBASE";
    public string Warning { get; set; } = string.Empty;
    public DateTime? TelemetryUpdatedUtc { get; set; }
    public JsonElement? Telemetry { get; set; }
}
