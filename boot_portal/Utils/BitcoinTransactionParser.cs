using System.Buffers.Binary;
using System.Security.Cryptography;

namespace boot_portal.Utils;

public sealed class BitcoinTransactionOutput
{
    public ulong Value { get; init; }
    public byte[] ScriptPubKey { get; init; } = [];
}

public static class BitcoinTransactionParser
{
    public static byte[] ComputeTransactionIdHash(byte[] transactionBytes)
    {
        if (transactionBytes.Length < 10)
        {
            throw new InvalidOperationException("Coinbase transaction is too short.");
        }

        int offset = 0;
        SkipBytes(transactionBytes, ref offset, 4); // version

        bool hasWitness = offset + 2 <= transactionBytes.Length &&
                          transactionBytes[offset] == 0x00 &&
                          transactionBytes[offset + 1] != 0x00;
        if (hasWitness)
        {
            SkipBytes(transactionBytes, ref offset, 2); // marker + flag
        }

        int inputsStart = offset;
        ulong inputCount = ReadVarInt(transactionBytes, ref offset);
        if (inputCount == 0)
        {
            throw new InvalidOperationException("Coinbase transaction has no inputs.");
        }

        for (ulong i = 0; i < inputCount; i++)
        {
            SkipInput(transactionBytes, ref offset);
        }

        ulong outputCount = ReadVarInt(transactionBytes, ref offset);
        if (outputCount > 1024)
        {
            throw new InvalidOperationException("Coinbase transaction output count is unreasonable.");
        }

        for (ulong i = 0; i < outputCount; i++)
        {
            SkipBytes(transactionBytes, ref offset, 8); // value
            ulong scriptLength = ReadVarInt(transactionBytes, ref offset);
            SkipBytes(transactionBytes, ref offset, scriptLength);
        }
        int outputsEnd = offset;

        if (hasWitness)
        {
            for (ulong i = 0; i < inputCount; i++)
            {
                ulong itemCount = ReadVarInt(transactionBytes, ref offset);
                for (ulong item = 0; item < itemCount; item++)
                {
                    ulong itemLength = ReadVarInt(transactionBytes, ref offset);
                    SkipBytes(transactionBytes, ref offset, itemLength);
                }
            }
        }

        int locktimeOffset = offset;
        SkipBytes(transactionBytes, ref offset, 4);
        if (offset != transactionBytes.Length)
        {
            throw new InvalidOperationException("Coinbase transaction has trailing bytes.");
        }

        if (!hasWitness)
        {
            return DoubleSha256(transactionBytes);
        }

        byte[] transactionIdPreimage = new byte[
            4 +
            (outputsEnd - inputsStart) +
            4];
        transactionBytes.AsSpan(0, 4).CopyTo(transactionIdPreimage);
        transactionBytes.AsSpan(inputsStart, outputsEnd - inputsStart)
            .CopyTo(transactionIdPreimage.AsSpan(4));
        transactionBytes.AsSpan(locktimeOffset, 4)
            .CopyTo(transactionIdPreimage.AsSpan(transactionIdPreimage.Length - 4));
        return DoubleSha256(transactionIdPreimage);
    }

    public static List<BitcoinTransactionOutput> ParseOutputs(byte[] transactionBytes)
    {
        if (transactionBytes.Length < 10)
        {
            throw new InvalidOperationException("Coinbase transaction is too short.");
        }

        int offset = 0;
        SkipBytes(transactionBytes, ref offset, 4); // version

        bool hasWitness = offset + 2 <= transactionBytes.Length &&
                          transactionBytes[offset] == 0x00 &&
                          transactionBytes[offset + 1] != 0x00;
        if (hasWitness)
        {
            SkipBytes(transactionBytes, ref offset, 2); // marker + flag
        }

        ulong inputCount = ReadVarInt(transactionBytes, ref offset);
        if (inputCount == 0)
        {
            throw new InvalidOperationException("Coinbase transaction has no inputs.");
        }

        for (ulong i = 0; i < inputCount; i++)
        {
            SkipInput(transactionBytes, ref offset);
        }

        ulong outputCount = ReadVarInt(transactionBytes, ref offset);
        if (outputCount > 1024)
        {
            throw new InvalidOperationException("Coinbase transaction output count is unreasonable.");
        }

        var outputs = new List<BitcoinTransactionOutput>((int)outputCount);
        for (ulong i = 0; i < outputCount; i++)
        {
            ulong value = ReadUInt64LittleEndian(transactionBytes, ref offset);
            ulong scriptLength = ReadVarInt(transactionBytes, ref offset);
            byte[] script = ReadBytes(transactionBytes, ref offset, scriptLength);

            outputs.Add(new BitcoinTransactionOutput
            {
                Value = value,
                ScriptPubKey = script
            });
        }

        if (hasWitness)
        {
            for (ulong i = 0; i < inputCount; i++)
            {
                ulong itemCount = ReadVarInt(transactionBytes, ref offset);
                for (ulong item = 0; item < itemCount; item++)
                {
                    ulong itemLength = ReadVarInt(transactionBytes, ref offset);
                    SkipBytes(transactionBytes, ref offset, itemLength);
                }
            }
        }

        SkipBytes(transactionBytes, ref offset, 4); // locktime
        if (offset != transactionBytes.Length)
        {
            throw new InvalidOperationException("Coinbase transaction has trailing bytes.");
        }

        return outputs;
    }

    private static void SkipInput(byte[] transactionBytes, ref int offset)
    {
        SkipBytes(transactionBytes, ref offset, 32); // prev txid
        SkipBytes(transactionBytes, ref offset, 4); // prev index
        ulong scriptLength = ReadVarInt(transactionBytes, ref offset);
        SkipBytes(transactionBytes, ref offset, scriptLength);
        SkipBytes(transactionBytes, ref offset, 4); // sequence
    }

    public static ulong ReadVarInt(byte[] buffer, ref int offset)
    {
        EnsureRemaining(buffer, offset, 1);
        byte prefix = buffer[offset++];
        return prefix switch
        {
            < 0xFD => prefix,
            0xFD => ReadUInt16LittleEndian(buffer, ref offset),
            0xFE => ReadUInt32LittleEndian(buffer, ref offset),
            _ => ReadUInt64LittleEndian(buffer, ref offset)
        };
    }

    private static ushort ReadUInt16LittleEndian(byte[] buffer, ref int offset)
    {
        EnsureRemaining(buffer, offset, 2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    private static uint ReadUInt32LittleEndian(byte[] buffer, ref int offset)
    {
        EnsureRemaining(buffer, offset, 4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static ulong ReadUInt64LittleEndian(byte[] buffer, ref int offset)
    {
        EnsureRemaining(buffer, offset, 8);
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset, 8));
        offset += 8;
        return value;
    }

    private static byte[] ReadBytes(byte[] buffer, ref int offset, ulong count)
    {
        if (count > int.MaxValue)
        {
            throw new InvalidOperationException("Requested byte count is too large.");
        }

        EnsureRemaining(buffer, offset, (int)count);
        byte[] value = buffer.AsSpan(offset, (int)count).ToArray();
        offset += (int)count;
        return value;
    }

    private static void SkipBytes(byte[] buffer, ref int offset, ulong count)
    {
        if (count > int.MaxValue)
        {
            throw new InvalidOperationException("Requested byte count is too large.");
        }

        EnsureRemaining(buffer, offset, (int)count);
        offset += (int)count;
    }

    private static void EnsureRemaining(byte[] buffer, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
        {
            throw new InvalidOperationException("Coinbase transaction is truncated.");
        }
    }

    private static byte[] DoubleSha256(byte[] bytes)
    {
        return SHA256.HashData(SHA256.HashData(bytes));
    }
}
