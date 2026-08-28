using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace boot_portal.Utils;

internal sealed class BitcoinBlake2bHeaderV2Profile : IChainHeaderProfile
{
    private const int HeaderBytes = 164;
    private const uint HeaderV2Flag = 0x80000000;
    private const byte UseTimeOffsetFlag = 4;
    private readonly BigInteger _bitcoinPowLimit = DecodeCompactTargetCore(0x1d00ffff);
    private readonly BigInteger _regtestPowLimit = DecodeCompactTargetCore(0x207fffff);

    public string PowAlgorithmId => "blake2b";
    public string HeaderFormatId => "bitcoin-header-v2";
    public int HeaderLengthBytes => HeaderBytes;
    public BigInteger DifficultyOneTarget => _bitcoinPowLimit;

    public ParsedChainHeader ParseAndHash(string? headerHex)
    {
        string normalized = BitcoinHashes.NormalizeHex(headerHex);
        if (normalized.Length != HeaderBytes * 2)
        {
            throw new ArgumentException("Blake2b block header must be exactly 164 bytes.", nameof(headerHex));
        }

        byte[] header = Convert.FromHexString(normalized);
        uint completeVersion = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if ((completeVersion & HeaderV2Flag) == 0)
        {
            throw new ArgumentException("Blake2b block header must set the header-v2 version flag.", nameof(headerHex));
        }

        byte flags = header[110];
        if ((flags & 0xc0) != 0)
        {
            throw new ArgumentException("Blake2b block header has reserved high flag bits set.", nameof(headerHex));
        }

        byte[] powHash = ComputePowHash(header);
        uint wireTime = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(68, 4));
        uint timeOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(104, 4));
        uint effectiveTime = (flags & UseTimeOffsetFlag) == 0
            ? wireTime
            : unchecked(wireTime + timeOffset);
        uint compactTarget = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(72, 4));
        BigInteger powValue = new(powHash, isUnsigned: true, isBigEndian: true);

        return new ParsedChainHeader
        {
            HeaderHex = normalized,
            HeaderBytes = header,
            PowHashLittleEndianBytes = powHash.Reverse().ToArray(),
            DisplayBlockHash = Convert.ToHexString(powHash).ToLowerInvariant(),
            DisplayParentBlockHash = BitcoinHashes.ToDisplayHashHex(header.AsSpan(4, 32).ToArray()),
            MerkleRootLittleEndianBytes = header.AsSpan(36, 32).ToArray(),
            HeaderTimeUtc = DateTimeOffset.FromUnixTimeSeconds(effectiveTime).UtcDateTime,
            CompactTarget = compactTarget,
            EncodedTarget = DecodeCompactTarget(compactTarget),
            PowValue = powValue,
            AchievedWork = CalculateExactWork(powValue),
            AchievedDifficulty = powValue.IsZero
                ? double.MaxValue
                : (double)DifficultyOneTarget / (double)powValue,
            DeclaredHeight = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(128, 4)),
            DeclaredTransactionCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(108, 2)),
            HeaderFlags = flags
        };
    }

    public BigInteger DecodeCompactTarget(uint compactTarget) => DecodeCompactTargetCore(compactTarget);

    public BigInteger GetPowLimit(string? bitcoinNetwork) =>
        BitcoinScript.NormalizeNetwork(bitcoinNetwork) == BitcoinScript.Regtest
            ? _regtestPowLimit
            : _bitcoinPowLimit;

    private static BigInteger CalculateExactWork(BigInteger powValue) =>
        (BigInteger.One << 256) / (powValue + BigInteger.One);

    private static byte[] ComputePowHash(byte[] header)
    {
        byte[] xorKey = header.AsSpan(112, 16).ToArray();
        byte[] xorKeyHash = TaggedSha256("Bitcoin block hash PoW XOR key", xorKey);
        byte[] xorMask = new byte[32];
        if (xorKey.Any(value => value != 0))
        {
            xorMask = TaggedSha256("Bitcoin block hash PoW XOR mask", xorKey);
            int clearBits = header[111];
            int clearBytes = clearBits / 8;
            Array.Clear(xorMask, 0, clearBytes);
            if (clearBytes < xorMask.Length)
            {
                xorMask[clearBytes] &= (byte)(0xff >> (clearBits % 8));
            }
        }

        byte[] orderedParent = header.AsSpan(4, 32).ToArray();
        Array.Reverse(orderedParent);
        byte[] hiddenParent = TaggedSha256("Bitcoin prevblock header, hashed", orderedParent);

        byte[] h1Payload = new byte[119];
        int offset = 0;
        Copy(header.AsSpan(0, 4), h1Payload, ref offset);
        Copy(orderedParent, h1Payload, ref offset);
        Copy(header.AsSpan(128, 4), h1Payload, ref offset);
        Copy(header.AsSpan(36, 32), h1Payload, ref offset);
        Copy(header.AsSpan(68, 4), h1Payload, ref offset);
        h1Payload[offset++] = 0;
        Copy(header.AsSpan(72, 4), h1Payload, ref offset);
        ushort transactionCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(108, 2));
        BinaryPrimitives.WriteUInt32LittleEndian(h1Payload.AsSpan(offset, 4), transactionCount);
        offset += 4;
        h1Payload[offset++] = header[110];
        h1Payload[offset++] = header[111];
        Copy(xorKeyHash, h1Payload, ref offset);
        System.Diagnostics.Debug.Assert(offset == h1Payload.Length);
        byte[] h1 = TaggedSha256("Bitcoin block header 1", h1Payload);

        byte[] h2Payload = new byte[96];
        h1.CopyTo(h2Payload, 0);
        header.AsSpan(132, 32).CopyTo(h2Payload.AsSpan(64));
        byte[] h2 = TaggedSha256("Merge-mining hook", h2Payload);

        byte[] firstInput = new byte[52];
        h2.CopyTo(firstInput, 4);
        header.AsSpan(88, 16).CopyTo(firstInput.AsSpan(36));
        byte[] firstHash = Blake2b256(firstInput);

        byte[] asicInput = BuildAsicInput(header, hiddenParent, h2, firstHash);
        byte[] secondHash = Blake2b256(asicInput);
        for (int index = 0; index < secondHash.Length; index++)
        {
            secondHash[index] ^= xorMask[index];
        }

        return secondHash;
    }

    private static byte[] BuildAsicInput(byte[] header, byte[] hiddenParent, byte[] h2, byte[] firstHash)
    {
        int profile = header[110] & 3;
        using MemoryStream stream = new();
        switch (profile)
        {
            case 3:
                stream.Write(new byte[32]);
                goto case 2;
            case 2:
                stream.Write(new byte[48]);
                stream.Write(h2);
                stream.Write(header.AsSpan(76, 4));
                stream.Write(header.AsSpan(80, 4));
                stream.Write(header.AsSpan(104, 4));
                stream.Write(header.AsSpan(84, 4));
                stream.Write(firstHash);
                break;
            case 0:
                byte[] maskedParent = hiddenParent.ToArray();
                Array.Clear(maskedParent, 0, 6);
                stream.Write(maskedParent);
                stream.Write(header.AsSpan(76, 4));
                stream.Write(header.AsSpan(80, 4));
                stream.Write(header.AsSpan(104, 4));
                stream.Write(header.AsSpan(84, 4));
                stream.Write(firstHash);
                break;
            case 1:
                stream.Write(header.AsSpan(76, 4));
                stream.Write(header.AsSpan(80, 4));
                stream.Write(header.AsSpan(84, 4));
                stream.Write(header.AsSpan(104, 4));
                stream.Write(firstHash);
                stream.Write(h2);
                break;
            default:
                throw new InvalidOperationException("Unknown Blake2b ASIC profile.");
        }

        return stream.ToArray();
    }

    private static byte[] TaggedSha256(string tag, ReadOnlySpan<byte> payload)
    {
        byte[] tagHash = SHA256.HashData(Encoding.UTF8.GetBytes(tag));
        byte[] input = new byte[(tagHash.Length * 2) + payload.Length];
        tagHash.CopyTo(input, 0);
        tagHash.CopyTo(input, tagHash.Length);
        payload.CopyTo(input.AsSpan(tagHash.Length * 2));
        return SHA256.HashData(input);
    }

    private static byte[] Blake2b256(byte[] input)
    {
        Blake2bDigest digest = new(256);
        digest.BlockUpdate(input, 0, input.Length);
        byte[] output = new byte[32];
        digest.DoFinal(output, 0);
        return output;
    }

    private static void Copy(ReadOnlySpan<byte> source, byte[] destination, ref int offset)
    {
        source.CopyTo(destination.AsSpan(offset));
        offset += source.Length;
    }

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
