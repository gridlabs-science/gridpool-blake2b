using boot_portal.Models;

public class PoolState
{
    public BootProtocolMetadata Metadata { get; set; } = new();
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public int CurrentRoundNumber { get; set; }
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public string? TrustedLocalTipBlockHash { get; set; }
    public long? TrustedLocalTipBlockHeight { get; set; }
    public uint? CurrentTipCompactTarget { get; set; }
    public BootProvisionalTipState? ProvisionalTip { get; set; }
    public string? LastTestingTriggerBlockHash { get; set; }
    public long? LastTestingTriggerBlockHeight { get; set; }
    public string? LastGridPoolBlockHash { get; set; }
    public long? LastGridPoolBlockHeight { get; set; }
    public DateTime? LastGridPoolBlockUtc { get; set; }
    public string? LastGridPoolBlockMinerAddress { get; set; }
    public double? LastGridPoolBlockDifficulty { get; set; }
    public string ActiveSnapshotId { get; set; } = string.Empty;
    public string LastPaidSnapshotId { get; set; } = string.Empty;
    public List<string> ActiveSnapshotProofIds { get; set; } = [];
    public List<string> LastPaidSnapshotProofIds { get; set; } = [];
    public bool SupportFeeEnabled { get; set; }
    public string PayoutVariant { get; set; } = string.Empty;
    public List<BootPayoutSnapshotContext> SnapshotContexts { get; set; } = [];
    public List<BootSnapshotFamilyState> SnapshotFamilies { get; set; } = [];
    public BootSnapshotReconciliationCounters ReconciliationCounters { get; set; } = new();
    public List<string> AcceptedParentBlockHashes { get; set; } = [];
    public DateTime? LastRotationUtc { get; set; }
    public DateTime? GenesisRoundStartedUtc { get; set; }
    public List<PayoutInfo> WinnersList { get; set; } = [];
    public List<PayoutInfo> OnDeckList { get; set; } = [];
    public List<BootShareProof> OnDeckProofs { get; set; } = [];
    public List<BootAcceptedShareTelemetry> RecentAcceptedShares { get; set; } = [];
    public List<BootShareDiagnosticTelemetry> RecentRejectedShareDiagnostics { get; set; } = [];
    public List<BootCoinbaserFetchTelemetry> RecentCoinbaserDiagnostics { get; set; } = [];
    public List<BootDatumShareResponseTelemetry> RecentDatumShareResponses { get; set; } = [];
    public List<BootDatumSessionTelemetry> RecentDatumSessions { get; set; } = [];
    public List<BootNetworkEvent> RecentNetworkEvents { get; set; } = [];
    public List<BootPeerRelayObservation> RecentPeerRelayObservations { get; set; } = [];
    public List<BootHashratePoint> HashrateSamples { get; set; } = [];
    public List<BootLocalDatumMinerHashrateRollupPoint> LocalDatumMinerHashrateSamples { get; set; } = [];
    public List<BootStateBundle> ArchivedStateBundles { get; set; } = [];
    public List<BootBoundaryTransitionJournalEntry> BoundaryTransitionJournal { get; set; } = [];
    public List<BootPeerStatus> Peers { get; set; } = [];
    public Dictionary<string, string> KnownDatumPayoutAddresses { get; set; } = [];
    public BestShareRecord BestShare { get; set; } = new();
}

public class BootBoundaryTransitionJournalEntry
{
    public string BlockHash { get; set; } = string.Empty;
    public long BlockHeight { get; set; }
    public string ParentBlockHash { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; }
    public bool GridPoolPaymentApplied { get; set; }
    public BootBoundaryTransitionCheckpoint Before { get; set; } = new();
}

public class BootBoundaryTransitionCheckpoint
{
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public int CurrentRoundNumber { get; set; }
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public string? TrustedLocalTipBlockHash { get; set; }
    public long? TrustedLocalTipBlockHeight { get; set; }
    public uint? CurrentTipCompactTarget { get; set; }
    public BootProvisionalTipState? ProvisionalTip { get; set; }
    public string? LastTestingTriggerBlockHash { get; set; }
    public long? LastTestingTriggerBlockHeight { get; set; }
    public string? LastGridPoolBlockHash { get; set; }
    public long? LastGridPoolBlockHeight { get; set; }
    public DateTime? LastGridPoolBlockUtc { get; set; }
    public string? LastGridPoolBlockMinerAddress { get; set; }
    public double? LastGridPoolBlockDifficulty { get; set; }
    public string ActiveSnapshotId { get; set; } = string.Empty;
    public string LastPaidSnapshotId { get; set; } = string.Empty;
    public List<string> ActiveSnapshotProofIds { get; set; } = [];
    public List<string> LastPaidSnapshotProofIds { get; set; } = [];
    public bool SupportFeeEnabled { get; set; }
    public string PayoutVariant { get; set; } = string.Empty;
    public List<BootPayoutSnapshotContext> SnapshotContexts { get; set; } = [];
    public List<BootSnapshotFamilyState> SnapshotFamilies { get; set; } = [];
    public BootSnapshotReconciliationCounters ReconciliationCounters { get; set; } = new();
    public List<string> AcceptedParentBlockHashes { get; set; } = [];
    public DateTime? LastRotationUtc { get; set; }
    public DateTime? GenesisRoundStartedUtc { get; set; }
    public List<PayoutInfo> WinnersList { get; set; } = [];
    public List<PayoutInfo> OnDeckList { get; set; } = [];
    public List<BootShareProof> OnDeckProofs { get; set; } = [];
    public List<BootStateBundle> ArchivedStateBundles { get; set; } = [];
    public BestShareRecord BestShare { get; set; } = new();
}

public class BootProvisionalTipState
{
    public string BlockHash { get; set; } = string.Empty;
    public string ParentBlockHash { get; set; } = string.Empty;
    public string HeaderHex { get; set; } = string.Empty;
    public uint CompactTarget { get; set; }
    public DateTime HeaderTimeUtc { get; set; }
    public DateTime ObservedUtc { get; set; }
    public DateTime GraceDeadlineUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
    public List<BootShareProof> SnapshotProofs { get; set; } = [];
    public bool ExpectedDifficultyValidated { get; set; }
}

public class PoolStateHistory
{
    public string ChainDomainFingerprint { get; set; } = string.Empty;
    public List<BootAcceptedShareTelemetry> RecentAcceptedShares { get; set; } = [];
    public List<BootShareDiagnosticTelemetry> RecentRejectedShareDiagnostics { get; set; } = [];
    public List<BootCoinbaserFetchTelemetry> RecentCoinbaserDiagnostics { get; set; } = [];
    public List<BootDatumShareResponseTelemetry> RecentDatumShareResponses { get; set; } = [];
    public List<BootDatumSessionTelemetry> RecentDatumSessions { get; set; } = [];
    public List<BootNetworkEvent> RecentNetworkEvents { get; set; } = [];
    public List<BootPeerRelayObservation> RecentPeerRelayObservations { get; set; } = [];
    public List<BootHashratePoint> HashrateSamples { get; set; } = [];
    public List<BootLocalDatumMinerHashrateRollupPoint> LocalDatumMinerHashrateSamples { get; set; } = [];
    public List<BootStateBundle> ArchivedStateBundles { get; set; } = [];
    public List<BootPayoutSnapshotContext> SnapshotContexts { get; set; } = [];
}

public class BootProtocolMetadata
{
    public string NodeId { get; set; } = string.Empty;
    public string NetworkId { get; set; } = "mainnet-beta";
    public string ChainProfileId { get; set; } = ChainDomainProfiles.LegacySha256dProfileId;
    public string ChainDomainFingerprint { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; } = BootProtocolVersions.ConsensusVersion;
    public int ConsensusVersion { get; set; } = BootProtocolVersions.ConsensusVersion;
    public int StateBundleSchemaVersion { get; set; } = BootProtocolVersions.StateBundleSchemaVersion;
    public int HttpApiVersion { get; set; } = BootProtocolVersions.HttpApiVersion;
    public int PeerTransportVersion { get; set; } = BootProtocolVersions.PeerTransportVersion;
    public int UdpRelayVersion { get; set; } = BootProtocolVersions.UdpRelayVersion;
    public string ReleaseVersion { get; set; } = string.Empty;
}

public class BestShareRecord
{
    public double Difficulty { get; set; }
    public string MinerAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
