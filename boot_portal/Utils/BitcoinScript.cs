using boot_portal.Utils;

namespace boot_portal.Utils;

public static class BitcoinScript
{
    public static string NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        int dotIndex = address.IndexOf('.');
        return dotIndex >= 0 ? address[..dotIndex] : address;
    }

    public static bool TryAddressToScriptPubKey(string address, out byte[] scriptPubKey)
    {
        try
        {
            scriptPubKey = AddressToScriptPubKey(address);
            return true;
        }
        catch
        {
            scriptPubKey = [];
            return false;
        }
    }

    public static byte[] AddressToScriptPubKey(string address)
    {
        string normalized = NormalizeAddress(address);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Address is required.");
        }

        if (normalized.StartsWith("bc1", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("tb1", StringComparison.OrdinalIgnoreCase))
        {
            var (_, version, program) = Bech32.Decode(normalized);
            byte witnessVersionOpCode = version switch
            {
                0 => (byte)0x00,
                >= 1 and <= 16 => (byte)(0x50 + version),
                _ => throw new InvalidOperationException($"Unsupported witness version {version} for address {address}.")
            };

            byte[] script = new byte[2 + program.Length];
            script[0] = witnessVersionOpCode;
            script[1] = (byte)program.Length;
            Array.Copy(program, 0, script, 2, program.Length);
            return script;
        }

        byte[] payload = Base58Check.Decode(normalized);
        if (payload.Length != 21)
        {
            throw new InvalidOperationException($"Unsupported Base58 payload length {payload.Length} for address {address}.");
        }

        return payload[0] switch
        {
            0x00 => BuildP2PkhScript(payload),
            0x05 => BuildP2ShScript(payload),
            _ => throw new InvalidOperationException($"Unsupported Base58 version 0x{payload[0]:X2} for address {address}.")
        };
    }

    public static string AddressToScriptPubKeyHex(string address)
    {
        return Convert.ToHexString(AddressToScriptPubKey(address)).ToLowerInvariant();
    }

    public static string ScriptToAddress(byte[] script)
    {
        if (script.Length == 25 &&
            script[0] == 0x76 &&
            script[1] == 0xA9 &&
            script[2] == 0x14 &&
            script[23] == 0x88 &&
            script[24] == 0xAC)
        {
            byte[] payload = new byte[21];
            payload[0] = 0x00;
            Array.Copy(script, 3, payload, 1, 20);
            return Base58Check.Encode(payload);
        }

        if (script.Length == 23 &&
            script[0] == 0xA9 &&
            script[1] == 0x14 &&
            script[22] == 0x87)
        {
            byte[] payload = new byte[21];
            payload[0] = 0x05;
            Array.Copy(script, 2, payload, 1, 20);
            return Base58Check.Encode(payload);
        }

        if (script.Length == 22 && script[0] == 0x00 && script[1] == 0x14)
        {
            byte[] program = script.Skip(2).Take(20).ToArray();
            return Bech32.Encode("bc", 0, program);
        }

        if (script.Length == 34 && script[0] == 0x00 && script[1] == 0x20)
        {
            byte[] program = script.Skip(2).Take(32).ToArray();
            return Bech32.Encode("bc", 0, program);
        }

        if (script.Length == 34 && script[0] == 0x51 && script[1] == 0x20)
        {
            byte[] program = script.Skip(2).Take(32).ToArray();
            return Bech32.Encode("bc", 1, program);
        }

        return "UNKNOWN";
    }

    private static byte[] BuildP2PkhScript(byte[] payload)
    {
        byte[] script = new byte[25];
        script[0] = 0x76;
        script[1] = 0xA9;
        script[2] = 0x14;
        Array.Copy(payload, 1, script, 3, 20);
        script[23] = 0x88;
        script[24] = 0xAC;
        return script;
    }

    private static byte[] BuildP2ShScript(byte[] payload)
    {
        byte[] script = new byte[23];
        script[0] = 0xA9;
        script[1] = 0x14;
        Array.Copy(payload, 1, script, 2, 20);
        script[22] = 0x87;
        return script;
    }
}
