using System.Buffers.Binary;

namespace boot_portal.Utils;

public static class Blake2bDatumHeader
{
    public const int SerializedLength = 164;
    public const byte UseTimeOffsetFlag = 0x04;

    public static byte[] BuildProfile0(
        int version,
        ReadOnlySpan<byte> previousBlockHash,
        ReadOnlySpan<byte> merkleRoot,
        uint timeOnWire,
        ReadOnlySpan<byte> nBits,
        ulong nonce,
        ulong nTime,
        ReadOnlySpan<byte> extranonce,
        uint transactionCount,
        uint height,
        bool useTimeOffset)
    {
        if (previousBlockHash.Length != 32) throw new ArgumentException("Previous block hash must be 32 bytes.", nameof(previousBlockHash));
        if (merkleRoot.Length != 32) throw new ArgumentException("Merkle root must be 32 bytes.", nameof(merkleRoot));
        if (nBits.Length != 4) throw new ArgumentException("Compact target must be 4 bytes.", nameof(nBits));
        if (extranonce.Length != 12) throw new ArgumentException("DATUM extranonce must be 12 bytes.", nameof(extranonce));
        if (transactionCount > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(transactionCount));

        byte[] header = new byte[SerializedLength];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), unchecked((uint)version) | 0x80000000u);
        previousBlockHash.CopyTo(header.AsSpan(4, 32));
        merkleRoot.CopyTo(header.AsSpan(36, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68, 4), timeOnWire);
        nBits.CopyTo(header.AsSpan(72, 4));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(76, 8), nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(84, 4), (uint)(nTime >> 32));
        extranonce.CopyTo(header.AsSpan(92, 12));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(104, 4), (uint)nTime);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(108, 2), (ushort)transactionCount);
        header[110] = useTimeOffset ? UseTimeOffsetFlag : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(128, 4), height);
        return header;
    }
}
