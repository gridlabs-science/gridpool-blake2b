using System.Buffers.Binary;

namespace boot_portal.Utils;

public static class BitcoinTransactionSerialization
{
    public static byte[] SerializeCompactSize(ulong value)
    {
        if (value < 0xfd)
        {
            return [(byte)value];
        }

        if (value <= ushort.MaxValue)
        {
            byte[] bytes = new byte[3];
            bytes[0] = 0xfd;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1), (ushort)value);
            return bytes;
        }

        if (value <= uint.MaxValue)
        {
            byte[] bytes = new byte[5];
            bytes[0] = 0xfe;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), (uint)value);
            return bytes;
        }

        byte[] largeBytes = new byte[9];
        largeBytes[0] = 0xff;
        BinaryPrimitives.WriteUInt64LittleEndian(largeBytes.AsSpan(1), value);
        return largeBytes;
    }

    public static byte[] SerializeTxOutput(ulong value, byte[] scriptPubKey)
    {
        byte[] scriptLength = SerializeCompactSize((ulong)scriptPubKey.Length);
        byte[] output = new byte[8 + scriptLength.Length + scriptPubKey.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(0, 8), value);
        scriptLength.CopyTo(output.AsSpan(8));
        scriptPubKey.CopyTo(output.AsSpan(8 + scriptLength.Length));
        return output;
    }

    public static byte[] SerializeTxOutputs(IReadOnlyList<(ulong Value, byte[] ScriptPubKey)> outputs)
    {
        byte[] outputCount = SerializeCompactSize((ulong)outputs.Count);
        int totalLength = outputCount.Length + outputs.Sum(output => 8 + SerializeCompactSize((ulong)output.ScriptPubKey.Length).Length + output.ScriptPubKey.Length);
        byte[] serialized = new byte[totalLength];
        int offset = 0;
        outputCount.CopyTo(serialized.AsSpan(offset));
        offset += outputCount.Length;

        foreach ((ulong value, byte[] scriptPubKey) in outputs)
        {
            byte[] txOutput = SerializeTxOutput(value, scriptPubKey);
            txOutput.CopyTo(serialized.AsSpan(offset));
            offset += txOutput.Length;
        }

        return serialized;
    }
}
