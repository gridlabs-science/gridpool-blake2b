using System.Numerics;

namespace boot_portal.Utils;

public static class BitcoinHashes
{
    public static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return string.Empty;
        }

        string normalized = hex.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized.Replace(" ", string.Empty).ToLowerInvariant();
    }

    public static string ToDisplayHashHex(byte[] hashBytes)
    {
        return Convert.ToHexString(hashBytes.Reverse().ToArray()).ToLowerInvariant();
    }

    public static string ToLikelyDisplayHashHex(byte[] hashBytes)
    {
        string raw = Convert.ToHexString(hashBytes).ToLowerInvariant();
        string reversed = ToDisplayHashHex(hashBytes);
        return CountLeadingZeroNibbles(raw) > CountLeadingZeroNibbles(reversed)
            ? raw
            : reversed;
    }

    public static string ReverseHexByteOrder(string hex)
    {
        string normalized = NormalizeHex(hex);
        if (normalized.Length % 2 != 0)
        {
            return normalized;
        }

        char[] reversed = new char[normalized.Length];
        int targetIndex = 0;
        for (int sourceIndex = normalized.Length - 2; sourceIndex >= 0; sourceIndex -= 2)
        {
            reversed[targetIndex++] = normalized[sourceIndex];
            reversed[targetIndex++] = normalized[sourceIndex + 1];
        }

        return new string(reversed);
    }

    public static string NormalizeLikelyDisplayHashHex(string? hex)
    {
        string normalized = NormalizeHex(hex);
        if (normalized.Length != 64)
        {
            return normalized;
        }

        string reversed = ReverseHexByteOrder(normalized);
        return CountLeadingZeroNibbles(normalized) > CountLeadingZeroNibbles(reversed)
            ? normalized
            : reversed;
    }

    private static int CountLeadingZeroNibbles(string hex)
    {
        int count = 0;
        foreach (char c in NormalizeHex(hex))
        {
            if (c != '0')
            {
                break;
            }

            count++;
        }

        return count;
    }

    public static bool AreEquivalent(string? first, string? second)
    {
        string left = NormalizeHex(first);
        string right = NormalizeHex(second);

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return left.Length == 64 &&
               right.Length == 64 &&
               string.Equals(ReverseHexByteOrder(left), right, StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeBlockHashFromHeader(string? headerHex)
    {
        string normalized = NormalizeHex(headerHex);
        if (normalized.Length is not (160 or 328))
        {
            throw new ArgumentException("Bitcoin block header must be exactly 80 or 164 bytes.", nameof(headerHex));
        }

        return ChainProfiles.SelectForHeader(normalized).ParseAndHash(normalized).DisplayBlockHash;
    }

    public static BitcoinHeaderEvaluation EvaluateHeader(
        string? headerHex,
        DateTime receivedUtc,
        string? bitcoinNetwork = BitcoinScript.Mainnet)
    {
        try
        {
            string normalized = NormalizeHex(headerHex);
            if (normalized.Length is not (160 or 328))
            {
                return BitcoinHeaderEvaluation.Invalid("Block header must be exactly 80 or 164 bytes.");
            }

            IChainHeaderProfile headerProfile = ChainProfiles.SelectForHeader(normalized);
            ParsedChainHeader header = headerProfile.ParseAndHash(normalized);
            if (header.EncodedTarget <= BigInteger.Zero ||
                header.EncodedTarget > headerProfile.GetPowLimit(bitcoinNetwork))
            {
                return BitcoinHeaderEvaluation.Invalid("Header target is outside the Bitcoin proof-of-work limit.");
            }

            if (header.PowValue > header.EncodedTarget)
            {
                return BitcoinHeaderEvaluation.Invalid("Header does not satisfy its encoded proof-of-work target.");
            }

            return new BitcoinHeaderEvaluation
            {
                IsValid = true,
                HeaderHex = normalized,
                BlockHash = header.DisplayBlockHash,
                ParentBlockHash = header.DisplayParentBlockHash,
                CompactTarget = header.CompactTarget,
                HeaderTimeUtc = header.HeaderTimeUtc,
                ReceivedUtc = receivedUtc
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return BitcoinHeaderEvaluation.Invalid(ex.Message);
        }
    }

    public static BigInteger DecodeCompactTarget(uint compact) => ChainProfiles.BitcoinSha256dHeaderV1.DecodeCompactTarget(compact);
}

public sealed class BitcoinHeaderEvaluation
{
    public bool IsValid { get; init; }
    public string RejectionReason { get; init; } = string.Empty;
    public string HeaderHex { get; init; } = string.Empty;
    public string BlockHash { get; init; } = string.Empty;
    public string ParentBlockHash { get; init; } = string.Empty;
    public uint CompactTarget { get; init; }
    public DateTime HeaderTimeUtc { get; init; }
    public DateTime ReceivedUtc { get; init; }

    public static BitcoinHeaderEvaluation Invalid(string reason) => new()
    {
        RejectionReason = reason
    };
}
