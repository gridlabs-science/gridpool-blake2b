namespace boot_portal.Models;

public sealed class LocalMiningTelemetryBatchDto
{
    public string SourceInstance { get; set; } = string.Empty;
    public List<LocalMiningTelemetryEntryDto> Entries { get; set; } = [];
}

public sealed class LocalMiningTelemetryEntryDto
{
    public string ChannelId { get; set; } = string.Empty;
    public string PayoutAddress { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public long AcceptedShareCount { get; set; }
    public long RejectedShareCount { get; set; }
    public double AcceptedWorkDifficulty { get; set; }
    public double FeeWorkDifficulty { get; set; }
    public double BestDifficulty { get; set; }
}

public sealed class LocalMiningTelemetryResultDto
{
    public int AcceptedEntries { get; set; }
    public long AcceptedShares { get; set; }
    public double AcceptedWorkDifficulty { get; set; }
}
