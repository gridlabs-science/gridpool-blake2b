using System.Numerics;

namespace boot_portal.Utils;

public static class Uint256WorkScore
{
    public static readonly BigInteger MaxValue = (BigInteger.One << 256) - BigInteger.One;

    public static BigInteger FromPowValue(BigInteger powValue)
    {
        EnsureRange(powValue, nameof(powValue));
        return MaxValue - powValue;
    }

    public static BigInteger AdmissionTarget(BigInteger profilePowLimit, ulong minimumDifficulty)
    {
        EnsureRange(profilePowLimit, nameof(profilePowLimit));
        if (minimumDifficulty == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDifficulty), "Minimum difficulty must be greater than zero.");
        }

        return profilePowLimit / minimumDifficulty;
    }

    public static string Format(BigInteger value)
    {
        byte[] bytes = ToBigEndianBytes(value);
        return Convert.ToHexStringLower(bytes);
    }

    public static byte[] ToBigEndianBytes(BigInteger value)
    {
        EnsureRange(value, nameof(value));
        byte[] encoded = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] result = new byte[32];
        encoded.CopyTo(result, result.Length - encoded.Length);
        return result;
    }

    public static BigInteger Parse(string value)
    {
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new FormatException("uint256 values must be exactly 64 lowercase hexadecimal characters.");
        }

        return new BigInteger(Convert.FromHexString(value), isUnsigned: true, isBigEndian: true);
    }

    private static void EnsureRange(BigInteger value, string parameterName)
    {
        if (value < BigInteger.Zero || value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be an unsigned 256-bit integer.");
        }
    }
}
