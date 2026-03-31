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
    public DateTime? LastRotationUtc { get; set; }
    public int WinnersCount { get; set; }
    public int OnDeckCount { get; set; }
    public double OnDeckTotalDifficulty { get; set; }
    public int PeerCount { get; set; }
    public List<BootPeerStatus> Peers { get; set; } = [];
    public BootCommitmentInfo Commitment { get; set; } = new();
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
    public string? ParentBlockHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public double TotalDifficulty { get; set; }
    public List<string> ValidParentBlockHashes { get; set; } = [];
    public List<PayoutInfo> WinnersList { get; set; } = [];
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
