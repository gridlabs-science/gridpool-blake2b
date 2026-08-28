using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace boot_portal.Utils;

public interface IChainHeaderProfile
{
    string PowAlgorithmId { get; }
    string HeaderFormatId { get; }
    int HeaderLengthBytes { get; }
    BigInteger DifficultyOneTarget { get; }

    ParsedChainHeader ParseAndHash(string? headerHex);
    BigInteger DecodeCompactTarget(uint compactTarget);
    BigInteger GetPowLimit(string? bitcoinNetwork);
}

public sealed class ParsedChainHeader
{
    public string HeaderHex { get; init; } = string.Empty;
    public byte[] HeaderBytes { get; init; } = [];
    public byte[] PowHashLittleEndianBytes { get; init; } = [];
    public string DisplayBlockHash { get; init; } = string.Empty;
    public string DisplayParentBlockHash { get; init; } = string.Empty;
    public byte[] MerkleRootLittleEndianBytes { get; init; } = [];
    public DateTime HeaderTimeUtc { get; init; }
    public uint CompactTarget { get; init; }
    public BigInteger EncodedTarget { get; init; }
    public BigInteger PowValue { get; init; }
    public BigInteger AchievedWork { get; init; }
    public double AchievedDifficulty { get; init; }
    public int? DeclaredHeight { get; init; }
    public ushort? DeclaredTransactionCount { get; init; }
    public byte? HeaderFlags { get; init; }
}

public static class ChainProfiles
{
    public static IChainHeaderProfile BitcoinSha256dHeaderV1 { get; } = new BitcoinSha256dHeaderV1Profile();
    public static IChainHeaderProfile BitcoinBlake2bHeaderV2 { get; } = new BitcoinBlake2bHeaderV2Profile();

    public static IChainHeaderProfile SelectForHeader(string? headerHex)
    {
        string normalized = BitcoinHashes.NormalizeHex(headerHex);
        return normalized.Length switch
        {
            160 => BitcoinSha256dHeaderV1,
            328 => BitcoinBlake2bHeaderV2,
            _ => throw new ArgumentException("Block header must be exactly 80 or 164 bytes.", nameof(headerHex))
        };
    }
}

internal sealed class BitcoinSha256dHeaderV1Profile : IChainHeaderProfile
{
    private const int HeaderBytes = 80;
    private readonly BigInteger _bitcoinPowLimit = DecodeCompactTargetCore(0x1d00ffff);
    private readonly BigInteger _regtestPowLimit = DecodeCompactTargetCore(0x207fffff);

    public string PowAlgorithmId => "sha256d";
    public string HeaderFormatId => "bitcoin-header-v1";
    public int HeaderLengthBytes => HeaderBytes;
    public BigInteger DifficultyOneTarget => _bitcoinPowLimit;

    public ParsedChainHeader ParseAndHash(string? headerHex)
    {
        string normalized = BitcoinHashes.NormalizeHex(headerHex);
        if (normalized.Length != HeaderBytes * 2)
        {
            throw new ArgumentException("Header must be exactly 80 bytes.", nameof(headerHex));
        }

        byte[] header = Convert.FromHexString(normalized);
        byte[] first = SHA256.HashData(header);
        byte[] powHash = SHA256.HashData(first);
        BigInteger powValue = new(powHash, isUnsigned: true, isBigEndian: false);
        uint timestamp = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(68, 4));
        uint compactTarget = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(72, 4));

        return new ParsedChainHeader
        {
            HeaderHex = normalized,
            HeaderBytes = header,
            PowHashLittleEndianBytes = powHash,
            DisplayBlockHash = BitcoinHashes.ToDisplayHashHex(powHash),
            DisplayParentBlockHash = BitcoinHashes.ToDisplayHashHex(header.AsSpan(4, 32).ToArray()),
            MerkleRootLittleEndianBytes = header.AsSpan(36, 32).ToArray(),
            HeaderTimeUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime,
            CompactTarget = compactTarget,
            EncodedTarget = DecodeCompactTarget(compactTarget),
            PowValue = powValue,
            AchievedWork = CalculateExactWork(powValue),
            AchievedDifficulty = powValue.IsZero
                ? double.MaxValue
                : (double)DifficultyOneTarget / (double)powValue
        };
    }

    public BigInteger DecodeCompactTarget(uint compactTarget) => DecodeCompactTargetCore(compactTarget);

    public BigInteger GetPowLimit(string? bitcoinNetwork) =>
        BitcoinScript.NormalizeNetwork(bitcoinNetwork) == BitcoinScript.Regtest
            ? _regtestPowLimit
            : _bitcoinPowLimit;

    private static BigInteger CalculateExactWork(BigInteger powValue) =>
        (BigInteger.One << 256) / (powValue + BigInteger.One);

    private static BigInteger DecodeCompactTargetCore(uint compactTarget)
    {
        int exponent = (int)(compactTarget >> 24);
        uint mantissa = compactTarget & 0x007fffff;
        bool isNegative = (compactTarget & 0x00800000) != 0;
        if (mantissa == 0 || isNegative)
        {
            return BigInteger.Zero;
        }

        BigInteger value = mantissa;
        int shift = 8 * (exponent - 3);
        return shift >= 0 ? value << shift : value >> -shift;
    }
}
