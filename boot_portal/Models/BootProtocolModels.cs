namespace boot_portal.Models;

public class RecordedShareSubmission
{
    public string MinerAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string HeaderHex { get; set; } = string.Empty;
    public string CoinbaseHex { get; set; } = string.Empty;
    public List<string> MerklePath { get; set; } = [];
    public string? PrevBlockHash { get; set; }
    public double Difficulty { get; set; }
    public string Source { get; set; } = "unknown";
}

public class BootShareProof
{
    public string ShareId { get; set; } = string.Empty;
    public string MinerAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ScriptPubKeyHex { get; set; } = string.Empty;
    public string HeaderHex { get; set; } = string.Empty;
    public string CoinbaseHex { get; set; } = string.Empty;
    public List<string> MerklePath { get; set; } = [];
    public string? PrevBlockHash { get; set; }
    public double Difficulty { get; set; }
    public string DiffString { get; set; } = "0";
    public string Source { get; set; } = "unknown";
    public DateTime Timestamp { get; set; }
}

public class BootCommitmentInfo
{
    public int ProtocolVersion { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public string NextStateId { get; set; } = string.Empty;
    public bool OnChainSupported { get; set; }
    public string TagPreview { get; set; } = string.Empty;
    public string SupportNote { get; set; } = string.Empty;
}

public class BootPeerStatus
{
    public string Endpoint { get; set; } = string.Empty;
    public string Status { get; set; } = "unimplemented";
    public double? LatencyMs { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}

public class BootReasonCountDto
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class BootDatumDiagnosticsDto
{
    public int WindowSeconds { get; set; }
    public int TotalSubmissions { get; set; }
    public int AcceptedCount { get; set; }
    public int AcceptedOnDeckCount { get; set; }
    public int RejectedCount { get; set; }
    public DateTime? LastAcceptedUtc { get; set; }
    public DateTime? LastRejectedUtc { get; set; }
    public List<BootReasonCountDto> RejectionReasons { get; set; } = [];
}

public class BootCoinbaserDiagnosticsSummaryDto
{
    public int WindowSeconds { get; set; }
    public int TotalFetches { get; set; }
    public DateTime? LastFetchUtc { get; set; }
    public double? AverageDurationMs { get; set; }
    public double? P95DurationMs { get; set; }
    public double? AverageParseDurationMs { get; set; }
    public double? AverageStateReadDurationMs { get; set; }
    public double? AverageBuildDurationMs { get; set; }
    public double? AverageSerializeDurationMs { get; set; }
    public double? AverageSendDurationMs { get; set; }
    public double? P95SendDurationMs { get; set; }
    public int TemporarySlotZeroCount { get; set; }
    public int SlowFetchCount { get; set; }
    public int SlowStateReadCount { get; set; }
    public int SlowBuildCount { get; set; }
    public int SlowSerializeCount { get; set; }
    public int SlowSendCount { get; set; }
}

public class BootLocalDatumMinerSummaryDto
{
    public string Address { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int RecentAcceptedShareCount { get; set; }
    public int CurrentRoundAcceptedShareCount { get; set; }
    public double? CurrentHashrateThs { get; set; }
    public string CurrentHashrateDisplay { get; set; } = "--";
    public double CurrentRoundBestDifficulty { get; set; }
    public string CurrentRoundBestDifficultyDisplay { get; set; } = "0";
    public DateTime? LastShareUtc { get; set; }
}

public class BootNetworkStatusDto
{
    public string SelfEndpoint { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public int CurrentRoundNumber { get; set; }
    public int SharedWinnerSlotCount { get; set; }
    public int TotalPayoutSlotCount { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public DateTime? LastRotationUtc { get; set; }
    public int WinnersCount { get; set; }
    public double CurrentStateTotalDifficulty { get; set; }
    public int OnDeckCount { get; set; }
    public double OnDeckTotalDifficulty { get; set; }
    public long? CurrentRoundElapsedSeconds { get; set; }
    public double? CurrentRoundObservedHashrateThs { get; set; }
    public string CurrentRoundObservedHashrateDisplay { get; set; } = "--";
    public double? LocalDatumHashrateThs { get; set; }
    public string LocalDatumHashrateDisplay { get; set; } = "--";
    public int PeerCount { get; set; }
    public bool AdminApiEnabled { get; set; }
    public bool TestingRoundResetEnabled { get; set; }
    public string TestingRoundResetMode { get; set; } = "none";
    public int TestingRoundResetLowNibbleThreshold { get; set; }
    public string TestingRoundResetDescription { get; set; } = string.Empty;
    public string? LastTestingTriggerBlockHash { get; set; }
    public long? LastTestingTriggerBlockHeight { get; set; }
    public BootDatumDiagnosticsDto LocalDatumDiagnostics { get; set; } = new();
    public List<BootLocalDatumMinerSummaryDto> LocalDatumMiners { get; set; } = [];
    public BootCoinbaserDiagnosticsSummaryDto CoinbaserDiagnostics { get; set; } = new();
    public List<BootPeerStatus> Peers { get; set; } = [];
    public BootCommitmentInfo Commitment { get; set; } = new();
}

public class BootAcceptedShareTelemetry
{
    public string MinerAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Difficulty { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class BootShareDiagnosticTelemetry
{
    public string Source { get; set; } = string.Empty;
    public string MinerAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public bool AffectedOnDeck { get; set; }
    public string? RejectionReason { get; set; }
    public string? RejectionCategory { get; set; }
    public double Difficulty { get; set; }
    public int CurrentRoundNumber { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class BootCoinbaserFetchTelemetry
{
    public string Source { get; set; } = "datum";
    public string RemoteEndpoint { get; set; } = string.Empty;
    public string ClientIdentityPreview { get; set; } = string.Empty;
    public long RequestSequence { get; set; }
    public ulong RewardValue { get; set; }
    public ulong TeamPayoutTotal { get; set; }
    public ulong SlotZeroValue { get; set; }
    public string SlotZeroAddress { get; set; } = string.Empty;
    public bool UsingTemporarySlotZero { get; set; }
    public int WinnersCount { get; set; }
    public int CoinbaseOutputCount { get; set; }
    public int ResponsePayloadBytes { get; set; }
    public double DurationMs { get; set; }
    public double ParseDurationMs { get; set; }
    public double StateReadDurationMs { get; set; }
    public double BuildDurationMs { get; set; }
    public double SerializeDurationMs { get; set; }
    public double SendDurationMs { get; set; }
    public int CurrentRoundNumber { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class BootDatumShareResponseTelemetry
{
    public string RemoteEndpoint { get; set; } = string.Empty;
    public string MinerAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public bool AffectedOnDeck { get; set; }
    public string? RejectionReason { get; set; }
    public double Difficulty { get; set; }
    public string? PrevBlockHash { get; set; }
    public byte JobId { get; set; }
    public byte CoinbaseId { get; set; }
    public uint Nonce { get; set; }
    public bool IsBlock { get; set; }
    public bool SubsidyOnly { get; set; }
    public bool QuickDiff { get; set; }
    public bool NonceOnlySubmit { get; set; }
    public bool UsedCachedJob { get; set; }
    public double? CachedJobAgeMs { get; set; }
    public byte TargetByte { get; set; }
    public ushort? TargetByteIndex { get; set; }
    public int PayloadBytes { get; set; }
    public int CoinbaseBytes { get; set; }
    public int Coinb1Bytes { get; set; }
    public int Coinb2Bytes { get; set; }
    public int MerkleBranchCount { get; set; }
    public double ParseDurationMs { get; set; }
    public double BuildDurationMs { get; set; }
    public double ValidationDurationMs { get; set; }
    public double StaleHandlingDurationMs { get; set; }
    public double ResponseSendDurationMs { get; set; }
    public double TotalDurationMs { get; set; }
    public int CurrentRoundNumber { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class BootNetworkEvent
{
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? BlockHash { get; set; }
    public long? BlockHeight { get; set; }
    public int CurrentRoundNumber { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class BootCoinbaserDiagnosticsSeriesDto
{
    public int WindowSeconds { get; set; }
    public int TotalEvents { get; set; }
    public List<BootCoinbaserFetchTelemetry> Events { get; set; } = [];
}

public class BootShareDiagnosticsSeriesDto
{
    public int WindowSeconds { get; set; }
    public int TotalEvents { get; set; }
    public List<BootShareDiagnosticTelemetry> Events { get; set; } = [];
}

public class BootDatumShareResponseSeriesDto
{
    public int WindowSeconds { get; set; }
    public int TotalEvents { get; set; }
    public List<BootDatumShareResponseTelemetry> Events { get; set; } = [];
}

public class BootNetworkEventSeriesDto
{
    public int WindowSeconds { get; set; }
    public int TotalEvents { get; set; }
    public List<BootNetworkEvent> Events { get; set; } = [];
}

public class BootHashratePoint
{
    public DateTime TimestampUtc { get; set; }
    public int CurrentRoundNumber { get; set; }
    public double? TeamEstimatedHashrateThs { get; set; }
    public string TeamEstimatedHashrateDisplay { get; set; } = "--";
    public double? LocalDatumHashrateThs { get; set; }
    public string LocalDatumHashrateDisplay { get; set; } = "--";
}

public class BootHashrateSeriesDto
{
    public int SampleIntervalSeconds { get; set; }
    public int LocalWindowSeconds { get; set; }
    public List<BootHashratePoint> Points { get; set; } = [];
}

public class BootRoundPayoutAggregate
{
    public string Address { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int SlotCount { get; set; }
    public ulong TotalValue { get; set; }
    public double TotalDifficulty { get; set; }
    public string TotalDifficultyDisplay { get; set; } = "0";
}

public class BootRoundHistoryEntry
{
    public int RoundNumber { get; set; }
    public string StateId { get; set; } = string.Empty;
    public string? PreviousStateId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public bool IsCanonical { get; set; }
    public bool IsOrphaned { get; set; }
    public string? TriggerBlockHash { get; set; }
    public long? TriggerBlockHeight { get; set; }
    public string? ParentBlockHash { get; set; }
    public long? ParentBlockHeight { get; set; }
    public DateTime LockedAtUtc { get; set; }
    public long? RoundElapsedSeconds { get; set; }
    public int WinningShareCount { get; set; }
    public double WinningTotalDifficulty { get; set; }
    public string WinningTotalDifficultyDisplay { get; set; } = "0";
    public double? ObservedHashrateThs { get; set; }
    public string ObservedHashrateDisplay { get; set; } = "--";
    public int PaidSlotCount { get; set; }
    public int PaidRecipientCount { get; set; }
    public ulong PaidTotalValue { get; set; }
    public int NextWinnerSlotCount { get; set; }
    public int NextWinnerRecipientCount { get; set; }
    public ulong NextWinnerTotalValue { get; set; }
    public List<BootRoundPayoutAggregate> PaidRecipients { get; set; } = [];
    public List<BootRoundPayoutAggregate> NextRecipients { get; set; } = [];
}

public class PeerShareAnnouncement
{
    public string SenderEndpoint { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public BootShareProof Share { get; set; } = new();
}

public class BootStateBundle
{
    public string StateId { get; set; } = string.Empty;
    public string? PreviousStateId { get; set; }
    public string Kind { get; set; } = "current";
    public int CurrentRoundNumber { get; set; }
    public int ProtocolVersion { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public string? LockedByBlockHash { get; set; }
    public long? LockedByBlockHeight { get; set; }
    public string? ParentBlockHash { get; set; }
    public long? ParentBlockHeight { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public double TotalDifficulty { get; set; }
    public List<string> ValidParentBlockHashes { get; set; } = [];
    public List<PayoutInfo> WinnersList { get; set; } = [];
    public List<PayoutInfo> ProofWinnersList { get; set; } = [];
    public List<BootShareProof> ShareProofs { get; set; } = [];
    public BootCommitmentInfo Commitment { get; set; } = new();
}

public class ShareRecordingResult
{
    public bool Accepted { get; set; }
    public string? RejectionReason { get; set; }
    public bool NewRecord { get; set; }
    public bool AffectedOnDeck { get; set; }
    public double ComputedDifficulty { get; set; }
    public bool IsBlock { get; set; }
    public string BlockHash { get; set; } = string.Empty;
    public BootShareProof? AcceptedProof { get; set; }
    public List<PayoutInfo> OnDeckList { get; set; } = [];
    public BestShareRecord BestShare { get; set; } = new();
    public BootNetworkStatusDto NetworkStatus { get; set; } = new();
    public RoundRotationResult? Rotation { get; set; }
}

public class RoundRotationResult
{
    public bool Rotated { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? BlockHash { get; set; }
    public List<PayoutInfo> WinnersList { get; set; } = [];
    public List<PayoutInfo> OnDeckList { get; set; } = [];
    public BootNetworkStatusDto NetworkStatus { get; set; } = new();
    public BootStateBundle? LockedStateBundle { get; set; }
}
