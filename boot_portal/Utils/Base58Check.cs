using System.Numerics;
using System.Security.Cryptography;

namespace boot_portal.Utils;

public static class Base58Check
{
    private static readonly string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private static readonly BigInteger AlphabetSize = 58;

    public static byte[] Decode(string address)
    {
        BigInteger intData = 0;
        foreach (char c in address)
        {
            int digit = Alphabet.IndexOf(c);
            if (digit == -1) throw new FormatException($"Invalid character in Base58 address: {c}");
            intData = intData * AlphabetSize + digit;
        }

        byte[] data = intData.ToByteArray(isUnsigned: true, isBigEndian: true);
        int leadingZeros = address.TakeWhile(c => c == '1').Count();
        byte[] result = new byte[leadingZeros + data.Length];
        Array.Copy(data, 0, result, leadingZeros, data.Length);

        if (result.Length < 4) throw new FormatException("Invalid Base58Check data length");
        byte[] payload = result.Take(result.Length - 4).ToArray();
        byte[] checksum = result.TakeLast(4).ToArray();
        byte[] hash = DoubleSha256(payload).Take(4).ToArray();
        
        if (!hash.SequenceEqual(checksum)) throw new FormatException("Invalid checksum");

        return payload; // version (1) + hash (20)
    }

    public static string Encode(byte[] payload)
    {
        // Calculate checksum: first 4 bytes of double SHA256
        byte[] checksum = DoubleSha256(payload).Take(4).ToArray();
        byte[] dataWithChecksum = payload.Concat(checksum).ToArray();

        // Convert to BigInteger
        BigInteger intData = 0;
        foreach (byte b in dataWithChecksum)
        {
            intData = intData * 256 + b;
        }

        // Convert to Base58
        var chars = new List<char>();
        while (intData > 0)
        {
            int remainder = (int)(intData % AlphabetSize);
            intData /= AlphabetSize;
            chars.Add(Alphabet[remainder]);
        }

        // Add leading '1's for each leading zero in payload
        int leadingZeros = payload.TakeWhile(b => b == 0).Count();
        for (int i = 0; i < leadingZeros; i++)
        {
            chars.Add('1');
        }

        // Reverse to get correct order
        chars.Reverse();
        return new string(chars.ToArray());
    }

    private static byte[] DoubleSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hash1 = sha256.ComputeHash(data);
        return sha256.ComputeHash(hash1);
    }
}