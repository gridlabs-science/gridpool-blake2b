using System.Text.Json.Serialization;

public class PoolState
{
    public List<PayoutInfo> WinnersList { get; set; } = new();
    public List<PayoutInfo> OnDeckList { get; set; } = new();
    public BestShareRecord? BestShare { get; set; }
}

public class BestShareRecord
{
    public double Difficulty { get; set; }
    public string MinerAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}