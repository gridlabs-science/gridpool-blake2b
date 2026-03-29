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
}
