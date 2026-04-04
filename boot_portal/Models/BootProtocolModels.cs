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

public class BootNetworkStatusDto
{
    public string SelfEndpoint { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public int SharedWinnerSlotCount { get; set; }
    public int TotalPayoutSlotCount { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public DateTime? LastRotationUtc { get; set; }
    public int WinnersCount { get; set; }
    public int OnDeckCount { get; set; }
    public double OnDeckTotalDifficulty { get; set; }
    public long? CurrentRoundElapsedSeconds { get; set; }
    public double? CurrentRoundObservedHashrateThs { get; set; }
    public string CurrentRoundObservedHashrateDisplay { get; set; } = "--";
    public int PeerCount { get; set; }
    public bool TestingRoundResetEnabled { get; set; }
    public string TestingRoundResetMode { get; set; } = "none";
    public int TestingRoundResetLowNibbleThreshold { get; set; }
    public string TestingRoundResetDescription { get; set; } = string.Empty;
    public string? LastTestingTriggerBlockHash { get; set; }
    public long? LastTestingTriggerBlockHeight { get; set; }
    public List<BootPeerStatus> Peers { get; set; } = [];
    public BootCommitmentInfo Commitment { get; set; } = new();
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
    public string Kind { get; set; } = string.Empty;
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
    public string Kind { get; set; } = "current";
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
