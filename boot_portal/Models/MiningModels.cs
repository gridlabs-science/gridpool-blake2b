namespace boot_portal.Models;

// The JSON object sent via POST /api/mining/share
public class ShareSubmissionDto
{
    public string MinerAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string HeaderHex { get; set; } = string.Empty;    // 80 byte block header
    public string CoinbaseHex { get; set; } = string.Empty;  // The coinbase tx
    public List<string> MerklePath { get; set; } = new();    // Hashes needed to rebuild root
    public string? PayoutSnapshotId { get; set; }
    public string? PrevBlockHash { get; set; }
    public long Nonce { get; set; }
    public double Difficulty { get; set; }
}

// The JSON object returned via GET /api/mining/payouts
public class PayoutResponseDto
{
    public long Sequence { get; set; } // Helps clients know if list changed
    public List<PayoutInfo> Payouts { get; set; } = new();
    public List<PayoutInfo> CoinbaseOutputs { get; set; } = new();
    public BootNetworkStatusDto Network { get; set; } = new();
}

public class MiningShareAdviceDto
{
    public long Sequence { get; set; }
    public int CurrentRoundNumber { get; set; }
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string ActiveSnapshotId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public int SharedWinnerSlotCount { get; set; }
    public int WorkSetCount { get; set; }
    public int WorkSetReserveLimit { get; set; }
    public int OnDeckCount { get; set; }
    public int OpenOnDeckSlots { get; set; }
    public bool OnDeckIsFull { get; set; }
    public double MinimumAcceptedDifficulty { get; set; } = 1;
    public double? CurrentWorkSetFloorDifficulty { get; set; }
    public string CurrentWorkSetFloorDifficultyDisplay { get; set; } = "--";
    public double? CurrentOnDeckFloorDifficulty { get; set; }
    public string CurrentOnDeckFloorDifficultyDisplay { get; set; } = "--";
    public double MinimumDifficultyToEnterOnDeck { get; set; } = 1;
    public string MinimumDifficultyToEnterOnDeckDisplay { get; set; } = "1";
    public bool RequiresStrictlyGreaterThanFloor { get; set; }
    public bool PulseProofsEnabled { get; set; }
    public double MinimumPulseDifficulty { get; set; } = 1;
    public string MinimumPulseDifficultyDisplay { get; set; } = "1";
    public int PulseTargetIntervalSeconds { get; set; } = 60;
    public int PulseRelayTtl { get; set; } = 1;
    public bool OptimisticRelayEnabled { get; set; }
    public double MinimumOptimisticRelayDifficulty { get; set; } = 1;
    public string MinimumOptimisticRelayDifficultyDisplay { get; set; } = "1";
    public double? BestOnDeckDifficulty { get; set; }
    public string BestOnDeckDifficultyDisplay { get; set; } = "--";
    public string SubmitRule { get; set; } = string.Empty;
}

public class Sv2WorkSelectionDto
{
    public long Sequence { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public string BitcoinNetwork { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; }
    public string ActiveSnapshotId { get; set; } = string.Empty;
    public string CurrentStateId { get; set; } = string.Empty;
    public string CandidateStateId { get; set; } = string.Empty;
    public string? CurrentTipBlockHash { get; set; }
    public long? CurrentTipBlockHeight { get; set; }
    public int TotalPayoutSlotCount { get; set; }
    public int SharedWinnerSlotCount { get; set; }
    public bool SupportFeeEnabled { get; set; }
    public int CoinbaseOutputCount { get; set; }
    public int CoinbaseTxOutputsBytes { get; set; }
    public string CoinbaseTxOutputsHex { get; set; } = string.Empty;
    public List<Sv2CoinbaseOutputDto> CoinbaseOutputs { get; set; } = [];
    public double MinimumAcceptedDifficulty { get; set; } = 1;
    public double MinimumDifficultyToEnterReserve { get; set; } = 1;
    public string MinimumDifficultyToEnterReserveDisplay { get; set; } = "1";
    public string UserIdentifierRule { get; set; } = string.Empty;
    public string Mode { get; set; } = "coinbase-only";
}

public class Sv2CoinbaseOutputDto
{
    public ulong Value { get; set; }
    public string Address { get; set; } = string.Empty;
    public string ScriptPubKeyHex { get; set; } = string.Empty;
    public string OutputHex { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public double Difficulty { get; set; }
    public string DiffString { get; set; } = "0";
}
