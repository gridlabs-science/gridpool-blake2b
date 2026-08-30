namespace boot_portal.Utils;

public static class BitcoinBlockParser
{
    private const int BlockHeaderBytes = 80;

    public static bool TryReadCoinbaseHeight(
        ReadOnlySpan<byte> block,
        out long height,
        int headerBytes = BlockHeaderBytes)
    {
        height = 0;
        if (headerBytes <= 0 || block.Length < headerBytes + 1)
        {
            return false;
        }

        byte[] bytes = block.ToArray();
        int offset = headerBytes;
        try
        {
            ulong transactionCount = BitcoinTransactionParser.ReadVarInt(bytes, ref offset);
            if (transactionCount == 0)
            {
                return false;
            }

            SkipBytes(bytes, ref offset, 4); // transaction version
            if (offset + 2 <= bytes.Length && bytes[offset] == 0 && bytes[offset + 1] != 0)
            {
                SkipBytes(bytes, ref offset, 2); // segwit marker and flag
            }

            ulong inputCount = BitcoinTransactionParser.ReadVarInt(bytes, ref offset);
            if (inputCount == 0)
            {
                return false;
            }

            // The first transaction in a block is the coinbase transaction.
            SkipBytes(bytes, ref offset, 32 + 4); // null prevout hash and index
            ulong scriptLength = BitcoinTransactionParser.ReadVarInt(bytes, ref offset);
            if (scriptLength == 0 || scriptLength > 100)
            {
                return false;
            }

            EnsureRemaining(bytes, offset, (int)scriptLength);
            ReadOnlySpan<byte> script = bytes.AsSpan(offset, (int)scriptLength);
            if (!TryReadFirstScriptNumber(script, out height))
            {
                height = 0;
                return false;
            }

            return height >= 0;
        }
        catch (InvalidOperationException)
        {
            height = 0;
            return false;
        }
    }

    private static bool TryReadFirstScriptNumber(ReadOnlySpan<byte> script, out long value)
    {
        value = 0;
        if (script.IsEmpty)
        {
            return false;
        }

        int length;
        int prefixBytes;
        byte prefix = script[0];
        if (prefix is >= 1 and <= 75)
        {
            length = prefix;
            prefixBytes = 1;
        }
        else if (prefix == 0x4c && script.Length >= 2)
        {
            length = script[1];
            prefixBytes = 2;
        }
        else
        {
            return false;
        }

        if (length == 0 || length > sizeof(long) || script.Length < prefixBytes + length)
        {
            return false;
        }

        ulong magnitude = 0;
        for (int i = 0; i < length; i++)
        {
            magnitude |= (ulong)script[prefixBytes + i] << (8 * i);
        }

        bool negative = (script[prefixBytes + length - 1] & 0x80) != 0;
        if (negative)
        {
            ulong mask = 0x80UL << (8 * (length - 1));
            magnitude &= ~mask;
            if (magnitude > long.MaxValue)
            {
                return false;
            }

            value = -(long)magnitude;
        }
        else
        {
            if (magnitude > long.MaxValue)
            {
                return false;
            }

            value = (long)magnitude;
        }

        return true;
    }

    private static void SkipBytes(byte[] bytes, ref int offset, int count)
    {
        EnsureRemaining(bytes, offset, count);
        offset += count;
    }

    private static void EnsureRemaining(byte[] bytes, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > bytes.Length)
        {
            throw new InvalidOperationException("Bitcoin block is truncated.");
        }
    }
}
