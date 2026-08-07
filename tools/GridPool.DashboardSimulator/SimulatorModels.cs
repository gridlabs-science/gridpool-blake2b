using System.Text.Json;

namespace GridPool.DashboardSimulator;

public sealed class SimulatorState
{
    public int SchemaVersion { get; set; } = 1;
    public int Seed { get; set; } = 42;
    public long Sequence { get; set; }
    public DateTime VirtualTimeUtc { get; set; } = new(2026, 7, 29, 16, 0, 0, DateTimeKind.Utc);
    public bool Playing { get; set; } = true;
    public double Speed { get; set; } = 1;
    public bool LoopTimeline { get; set; }
    public bool AdvancedOverrides { get; set; }
    public string Scenario { get; set; } = "healthy-mesh";
    public NodeControls Node { get; set; } = new();
    public ChainControls Chain { get; set; } = new();
    public WorkControls Work { get; set; } = new();
    public PulseControls Pulse { get; set; } = new();
    public FaultControls Faults { get; set; } = new();
    public List<PeerControl> Peers { get; set; } = [];
    public List<BitcoinPeerControl> BitcoinPeers { get; set; } = [];
    public List<AdapterControl> Adapters { get; set; } = [];
    public List<ProofControl> Reserve { get; set; } = [];
    public List<PayoutControl> LockedPayouts { get; set; } = [];
    public List<HistoryControl> History { get; set; } = [];
    public List<ProofHistoryControl> ProofHistory { get; set; } = [];
    public string SlotZeroAddress { get; set; } = string.Empty;
    public DateTime? SlotZeroObservedUtc { get; set; }
    public List<SimulatorEvent> Events { get; set; } = [];
    public TimelineDocument? Timeline { get; set; }
    public int TimelineCursor { get; set; }
    public double TimelineElapsedSeconds { get; set; }
}

public sealed class NodeControls
{
    public string DisplayName { get; set; } = "GridPool simulator";
    public string Region { get; set; } = "Synthetic lab";
    public string NetworkId { get; set; } = "testnet4-sim";
    public string BitcoinNetwork { get; set; } = "testnet4";
    public string ReleaseVersion { get; set; } = "simulator-v1";
    public int ConsensusVersion { get; set; } = 22;
    public int ProtocolVersion { get; set; } = 22;
    public bool Ready { get; set; } = true;
    public bool MiningSafe { get; set; } = true;
    public string SafetyReason { get; set; } = "Synthetic attached node is synchronized.";
    public bool RpcReachable { get; set; } = true;
    public bool RpcSynced { get; set; } = true;
    public bool InitialBlockDownload { get; set; }
    public bool ZmqHealthy { get; set; } = true;
    public bool PeerLoopsHealthy { get; set; } = true;
    public bool OutboundRelayHealthy { get; set; } = true;
    public bool VersionCompatible { get; set; } = true;
    public double NetworkHashrateHs { get; set; } = 730e18;
}

public sealed class BitcoinPeerControl
{
    public string Id { get; set; } = string.Empty;
    public bool Connected { get; set; } = true;
    public bool Inbound { get; set; }
    public double? LatencyMs { get; set; }
    public string ConnectionType { get; set; } = "outbound-full-relay";
}

public sealed class ChainControls
{
    public long Height { get; set; } = 146_216;
    public string TipHash { get; set; } = string.Empty;
    public string ProvisionalTipHash { get; set; } = string.Empty;
    public int Round { get; set; } = 117;
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string ActiveSnapshotId { get; set; } = string.Empty;
    public string SnapshotFamilyId { get; set; } = string.Empty;
    public DateTime? LastRotationUtc { get; set; }
    public int FamilyMembers { get; set; } = 1;
    public int FamilyUnionProofs { get; set; }
    public long SiblingAdmissions { get; set; }
    public long UnionAdditions { get; set; }
    public long Convergences { get; set; }
    public long PaidProofRemovals { get; set; }
    public long Reorganizations { get; set; }
}

public sealed class WorkControls
{
    public double PoolHashrateThs { get; set; } = 2_400;
    public int ObservationCount { get; set; } = 240;
    public int ReserveLimit { get; set; } = 897;
    public double AdmissionFloorDifficulty { get; set; } = 32_768;
    public string Window { get; set; } = "24h";
}

public sealed class PulseControls
{
    public bool Enabled { get; set; } = true;
    public int TargetIntervalSeconds { get; set; } = 40;
    public int RelayTtl { get; set; } = 3;
    public long Accepted { get; set; } = 2_140;
    public long Rejected { get; set; } = 3;
    public DateTime? LastAcceptedUtc { get; set; }
    public DateTime? LastRelayUtc { get; set; }
    public double SecondsUntilNext { get; set; } = 8;
}

public sealed class FaultControls
{
    public int ApiLatencyMs { get; set; }
    public bool ApiFailure { get; set; }
    public bool SignalRDrop { get; set; }
}

public sealed class PeerControl
{
    public string Id { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool Connected { get; set; } = true;
    public bool Http { get; set; } = true;
    public bool WebSocket { get; set; } = true;
    public bool Udp { get; set; } = true;
    public double LatencyMs { get; set; } = 35;
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public bool Compatible { get; set; } = true;
}

public sealed class AdapterControl
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "sv2";
    public string DisplayName { get; set; } = string.Empty;
    public bool Connected { get; set; } = true;
    public int ClientCount { get; set; } = 1;
    public double HashrateThs { get; set; }
    public long AcceptedShares { get; set; }
    public DateTime? LastShareUtc { get; set; }
    public List<MinerControl> Miners { get; set; } = [];
}

public sealed class MinerControl
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double HashrateThs { get; set; }
    public long AcceptedShares { get; set; }
    public DateTime? LastShareUtc { get; set; }
    public long RejectedShares { get; set; }
    public DateTime? LastRejectedUtc { get; set; }
    public string LastRejectionCategory { get; set; } = string.Empty;
}

public sealed class ProofHistoryControl
{
    public string ProofId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ProofClass { get; set; } = "work";
    public double Difficulty { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool EnteredWorkSet { get; set; }
    public bool BlockQuality { get; set; }
}

public sealed class ProofControl
{
    public string Id { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Difficulty { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public bool BlockQuality { get; set; }
}

public sealed class PayoutControl
{
    public string ProofId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Position { get; set; }
    public ulong ValueSats { get; set; } = 10_000;
}

public sealed class HistoryControl
{
    public DateTime TimestampUtc { get; set; }
    public double WorkRateThs { get; set; }
    public int ObservationCount { get; set; }
    public int PulseCount { get; set; }
}

public sealed class SimulatorEvent
{
    public long Sequence { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Dictionary<string, string> Arguments { get; set; } = [];
}

public sealed class SimulatorAction
{
    public string Action { get; set; } = string.Empty;
    public string? Peer { get; set; }
    public string? Adapter { get; set; }
    public string? Miner { get; set; }
    public string? Address { get; set; }
    public string? Transport { get; set; }
    public double? Value { get; set; }
    public int? Count { get; set; }
    public int? Rank { get; set; }
    public Dictionary<string, JsonElement> Set { get; set; } = [];
}

public sealed class TimelineDocument
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "timeline";
    public int Seed { get; set; } = 42;
    public string InitialScenario { get; set; } = "healthy-mesh";
    public List<TimelineEvent> Events { get; set; } = [];
}

public sealed class TimelineEvent
{
    public string At { get; set; } = "0s";
    public string Action { get; set; } = string.Empty;
    public string? Peer { get; set; }
    public string? Adapter { get; set; }
    public string? Miner { get; set; }
    public string? Address { get; set; }
    public string? Transport { get; set; }
    public double? Value { get; set; }
    public int? Count { get; set; }
    public int? Rank { get; set; }
}

public sealed record ScenarioDefinition(string Id, string Name, string Description);
