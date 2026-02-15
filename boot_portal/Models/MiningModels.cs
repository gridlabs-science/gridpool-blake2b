namespace boot_portal.Models;

// The JSON object sent via POST /api/mining/share
public class ShareSubmissionDto
{
    public string MinerAddress { get; set; } = string.Empty;
    public string HeaderHex { get; set; } = string.Empty;    // 80 byte block header
    public string CoinbaseHex { get; set; } = string.Empty;  // The coinbase tx
    public List<string> MerklePath { get; set; } = new();    // Hashes needed to rebuild root
    public long Nonce { get; set; }
    public double Difficulty { get; set; }
}

// The JSON object returned via GET /api/mining/payouts
public class PayoutResponseDto
{
    public long Sequence { get; set; } // Helps clients know if list changed
    public List<PayoutInfo> Payouts { get; set; } = new();
}