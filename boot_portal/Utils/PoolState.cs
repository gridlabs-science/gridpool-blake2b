using boot_portal.Models;

public class PoolState
{
    public BootProtocolMetadata Metadata { get; set; } = new();
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public string? LastTestingTriggerBlockHash { get; set; }
    public long? LastTestingTriggerBlockHeight { get; set; }
    public List<string> AcceptedParentBlockHashes { get; set; } = [];
    public DateTime? LastRotationUtc { get; set; }
    public List<PayoutInfo> WinnersList { get; set; } = [];
    public List<PayoutInfo> OnDeckList { get; set; } = [];
    public List<BootShareProof> OnDeckProofs { get; set; } = [];
    public List<BootStateBundle> ArchivedStateBundles { get; set; } = [];
    public List<BootPeerStatus> Peers { get; set; } = [];
    public Dictionary<string, string> KnownDatumPayoutAddresses { get; set; } = [];
    public BestShareRecord BestShare { get; set; } = new();
}

public class BootProtocolMetadata
{
    public string NetworkId { get; set; } = "public-beta";
    public int ProtocolVersion { get; set; } = 1;
}

public class BestShareRecord
{
    public double Difficulty { get; set; }
    public string MinerAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
