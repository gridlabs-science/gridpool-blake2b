using boot_portal.Services;

namespace boot_portal.Models;

public static class DashboardWindows
{
    public static readonly IReadOnlyDictionary<string, TimeSpan> Supported =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["6h"] = TimeSpan.FromHours(6),
            ["24h"] = TimeSpan.FromHours(24),
            ["7d"] = TimeSpan.FromDays(7)
        };

    public static string Normalize(string? value) =>
        value != null && Supported.ContainsKey(value) ? value.ToLowerInvariant() : "24h";
}

public sealed class DashboardSummaryDto
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public DashboardNodeDto Node { get; set; } = new();
    public DashboardHealthDto Health { get; set; } = new();
    public DashboardSnapshotDto Snapshot { get; set; } = new();
    public DashboardWorkRateEstimateDto WorkRate { get; set; } = new();
    public DashboardPulseDto Pulse { get; set; } = new();
    public DashboardCapabilitiesDto Capabilities { get; set; } = new();
}

public sealed class DashboardNodeDto
{
    public string NodeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string PublicEndpoint { get; set; } = string.Empty;
    public string NetworkId { get; set; } = string.Empty;
    public string BitcoinNetwork { get; set; } = string.Empty;
    public string ReleaseVersion { get; set; } = string.Empty;
    public int ConsensusVersion { get; set; }
    public int ProtocolVersion { get; set; }
    public int HttpApiVersion { get; set; }
    public DateTime ServiceStartedUtc { get; set; }
}

public sealed class DashboardHealthDto
{
    public string Status { get; set; } = "unknown";
    public bool MiningWorkSafe { get; set; }
    public string MiningWorkSafetyReason { get; set; } = string.Empty;
    public int PeerCount { get; set; }
    public bool PeerLoopsHealthy { get; set; }
    public bool OutboundRelayHealthy { get; set; }
    public string BitcoinNotificationMode { get; set; } = string.Empty;
    public string BitcoinAuthorityClass { get; set; } = string.Empty;
    public bool BitcoinRpcReachable { get; set; }
    public bool BitcoinRpcSynced { get; set; }
    public bool BitcoinInitialBlockDownload { get; set; }
    public string CurrentTipBlockHash { get; set; } = string.Empty;
    public long? CurrentTipBlockHeight { get; set; }
    public string ProvisionalTipBlockHash { get; set; } = string.Empty;
    public DateTime? LastPeerPollCompletedUtc { get; set; }
}

public sealed class DashboardSnapshotDto
{
    public int RoundNumber { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string ActiveSnapshotId { get; set; } = string.Empty;
    public string ActiveSnapshotFamilyId { get; set; } = string.Empty;
    public int LockedPayoutCount { get; set; }
    public int LockedProofCount { get; set; }
    public int ReserveCount { get; set; }
    public int ReserveLimit { get; set; }
    public double? ReserveFloorDifficulty { get; set; }
    public string ReserveFloorDifficultyDisplay { get; set; } = "--";
    public DateTime? LastRotationUtc { get; set; }
    public int FamilyMemberCount { get; set; }
    public int FamilyUnionProofCount { get; set; }
    public BootSnapshotReconciliationCounters Reconciliation { get; set; } = new();
}

public sealed class DashboardWorkRateEstimateDto
{
    public string Window { get; set; } = "24h";
    public int WindowSeconds { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public int ObservationCount { get; set; }
    public int RetainedOrderStatisticCount { get; set; }
    public double? EstimateThs { get; set; }
    public string EstimateDisplay { get; set; } = "--";
    public double? OrderStatisticDifficulty { get; set; }
    public string OrderStatisticDifficultyDisplay { get; set; } = "--";
    public double EffectiveAdmissionFloorDifficulty { get; set; } = 1d;
    public string EffectiveAdmissionFloorDisplay { get; set; } = "1";
    public double? RelativeStandardErrorPercent { get; set; }
    public string Confidence { get; set; } = "collecting";
    public bool Warmup { get; set; } = true;
    public bool CompleteWindow { get; set; }
    public string Method { get; set; } = "difficulty-order-statistic";
    public string Note { get; set; } = string.Empty;
}

public sealed class DashboardPulseDto
{
    public bool Enabled { get; set; }
    public long AcceptedTotal { get; set; }
    public int AcceptedInWindow { get; set; }
    public double AcceptedPerMinute { get; set; }
    public DateTime? LastAcceptedUtc { get; set; }
    public DateTime? LastSuccessfulOutboundRelayUtc { get; set; }
    public bool OutboundRelayHealthy { get; set; }
    public int TargetIntervalSeconds { get; set; }
    public int RelayTtl { get; set; }
    public string Interpretation { get; set; } =
        "Pulse proofs measure liveness and relay health; they are not blended into the team work-rate estimate.";
}

public sealed class DashboardCapabilitiesDto
{
    public bool WebUiEnabled { get; set; }
    public bool LegacyUiEnabled { get; set; }
    public bool OperatorApiAvailable { get; set; }
    public bool AddressLookupAvailable { get; set; } = true;
    public bool WorkRateTelemetryAvailable { get; set; } = true;
    public bool PulseTelemetryAvailable { get; set; } = true;
    public bool WatchtowerAvailable { get; set; }
    public List<string> Modules { get; set; } =
    [
        "status",
        "snapshot",
        "reserve",
        "work-rate",
        "pulse",
        "address",
        "network",
        "history",
        "protocol",
        "console"
    ];
}

public sealed class DashboardHistoryDto
{
    public int SchemaVersion { get; set; } = 1;
    public string Window { get; set; } = "24h";
    public int WindowSeconds { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<DashboardHistoryPointDto> Points { get; set; } = [];
}

public sealed class DashboardHistoryPointDto
{
    public DateTime TimestampUtc { get; set; }
    public double? WorkRateThs { get; set; }
    public int WorkObservationCount { get; set; }
    public double? RelativeStandardErrorPercent { get; set; }
    public int PulseCount { get; set; }
}

public sealed class DashboardAddressDto
{
    public int SchemaVersion { get; set; } = 1;
    public string Address { get; set; } = string.Empty;
    public bool Found { get; set; }
    public int LockedSlotCount { get; set; }
    public ulong LockedValueSats { get; set; }
    public List<int> LockedPositions { get; set; } = [];
    public int ProvisionalPositionCount { get; set; }
    public List<int> ProvisionalPositions { get; set; } = [];
    public double? BestProvisionalDifficulty { get; set; }
    public string BestProvisionalDifficultyDisplay { get; set; } = "--";
    public double? ReserveFloorDifficulty { get; set; }
    public string ReserveFloorDifficultyDisplay { get; set; } = "--";
    public double? EstimatedTop300SurvivalProbability { get; set; }
    public string Interpretation { get; set; } = string.Empty;
}

public sealed class DashboardOperatorDto
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime GeneratedAtUtc { get; set; }
    public List<BootLocalMiningSourceSummaryDto> LocalMiningSources { get; set; } = [];
    public List<BootLocalDatumMinerSummaryDto> LocalMiners { get; set; } = [];
    public List<BootPeerStatus> Peers { get; set; } = [];
    public BootBitcoinNotificationDto BitcoinNotification { get; set; } = new();
    public BootDatumDiagnosticsDto DatumDiagnostics { get; set; } = new();
    public BootCoinbaserDiagnosticsSummaryDto CoinbaserDiagnostics { get; set; } = new();
    public Dictionary<string, BootPeerLoopFault> PeerLoopFaults { get; set; } = new();
}

public sealed class DashboardChangedDto
{
    public long Revision { get; set; }
    public DateTime TimestampUtc { get; set; }
    public List<string> Topics { get; set; } = [];
}

internal sealed class DashboardTelemetryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime TrackingStartedUtc { get; set; }
    public DateTime? WorkDataTruncatedThroughUtc { get; set; }
    public List<DashboardWorkObservation> WorkProofs { get; set; } = [];
    public List<DashboardFloorObservation> AdmissionFloors { get; set; } = [];
    public List<DashboardPulseObservation> Pulses { get; set; } = [];
}

internal sealed class DashboardWorkObservation
{
    public string ShareId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Difficulty { get; set; }
    public double AdmissionFloorDifficulty { get; set; } = 1d;
    public DateTime ReceivedUtc { get; set; }
}

internal sealed class DashboardFloorObservation
{
    public double Difficulty { get; set; } = 1d;
    public DateTime ObservedUtc { get; set; }
}

internal sealed class DashboardPulseObservation
{
    public string ShareId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime ReceivedUtc { get; set; }
}
