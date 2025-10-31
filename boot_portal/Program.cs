// Program.cs

using System.Buffers.Binary;
using System.CommandLine;
using System.Formats.Asn1;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;


// =================================================================================
// 1. MAIN PROGRAM ENTRY POINT
// =================================================================================
// This class is responsible for parsing command-line arguments, managing the
// server's primary cryptographic key, and starting the TCP server.
// =================================================================================
// JSON configuration class for boot_portal_config.json

public static class Bech32
{
    private static readonly string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly uint[] Generator = { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };

    public static (string hrp, int version, byte[] program) Decode(string address)
    {
        int sepIndex = address.LastIndexOf('1');
        if (sepIndex < 1) throw new FormatException("Invalid Bech32 address: no separator");
        string hrp = address.Substring(0, sepIndex).ToLower();
        if (hrp != "bc" && hrp != "tb") throw new FormatException($"Invalid HRP: {hrp}");

        string dataPart = address.Substring(sepIndex + 1);
        if (dataPart.Length < 6) throw new FormatException("Bech32 data too short");

        byte[] data = new byte[dataPart.Length];
        for (int i = 0; i < dataPart.Length; i++)
        {
            int index = Charset.IndexOf(dataPart[i]);
            if (index == -1) throw new FormatException($"Invalid character in Bech32 address: {dataPart[i]}");
            data[i] = (byte)index;
        }

        uint checksum = Polymod(ExpandHrp(hrp).Concat(data).ToArray());
        if (checksum != 1) throw new FormatException("Invalid Bech32 checksum");

        int version = data[0];
        if (version > 16) throw new FormatException($"Invalid witness version: {version}");
        byte[] program5bit = data.Skip(1).Take(data.Length - 7).ToArray();
        byte[] program = ConvertBits(program5bit, 5, 8, false);
        if (program.Length < 2 || program.Length > 40) throw new FormatException($"Invalid program length: {program.Length}");
        if (version == 0 && program.Length != 20 && program.Length != 32) throw new FormatException("Invalid program length for version 0");

        return (hrp, version, program);
    }

    public static string Encode(string hrp, int version, byte[] program)
    {
        if (hrp != "bc" && hrp != "tb") throw new ArgumentException($"Invalid HRP: {hrp}");
        if (version < 0 || version > 16) throw new ArgumentException($"Invalid witness version: {version}");
        if (program.Length < 2 || program.Length > 40) throw new ArgumentException($"Invalid program length: {program.Length}");
        if (version == 0 && program.Length != 20 && program.Length != 32) throw new ArgumentException("Invalid program length for version 0");

        // Convert program to 5-bit
        byte[] data = ConvertBits(program, 8, 5, true);
        byte[] values = new byte[data.Length + 1];
        values[0] = (byte)version;
        Array.Copy(data, 0, values, 1, data.Length);

        // Compute checksum
        byte[] expandedHrp = ExpandHrp(hrp);
        byte[] checksum = new byte[6];
        uint polymod = Polymod(expandedHrp.Concat(values).Concat(new byte[6]).ToArray()) ^ 1;
        for (int i = 0; i < 6; i++)
        {
            checksum[i] = (byte)((polymod >> (5 * (5 - i))) & 31);
        }

        // Combine HRP, version, data, and checksum
        var chars = new List<char>(hrp.Length + 1 + values.Length + checksum.Length);
        chars.AddRange(hrp);
        chars.Add('1');
        chars.Add(Charset[version]);
        foreach (byte b in data)
        {
            chars.Add(Charset[b]);
        }
        foreach (byte b in checksum)
        {
            chars.Add(Charset[b]);
        }

        return new string(chars.ToArray());
    }

    private static uint Polymod(byte[] values)
    {
        uint chk = 1;
        foreach (byte v in values)
        {
            uint top = chk >> 25;
            chk = (chk & 0x1ffffff) << 5 ^ v;
            for (int i = 0; i < 5; i++)
            {
                if ((top >> i & 1) != 0)
                    chk ^= Generator[i];
            }
        }
        return chk;
    }

    private static byte[] ExpandHrp(string hrp)
    {
        byte[] ret = new byte[hrp.Length * 2 + 1];
        for (int i = 0; i < hrp.Length; i++)
        {
            ret[i] = (byte)(hrp[i] >> 5);
            ret[i + hrp.Length + 1] = (byte)(hrp[i] & 31);
        }
        return ret;
    }

    private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
    {
        int acc = 0;
        int bits = 0;
        var ret = new List<byte>();
        int maxv = (1 << toBits) - 1;
        int max_acc = (1 << (fromBits + toBits - 1)) - 1;
        foreach (byte value in data)
        {
            if (value < 0 || (value >> fromBits) != 0) throw new FormatException("Invalid Bech32 data");
            acc = ((acc << fromBits) | value) & max_acc;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                ret.Add((byte)((acc >> bits) & maxv));
            }
        }
        if (pad && bits > 0)
        {
            ret.Add((byte)((acc << (toBits - bits)) & maxv));
        }
        else if (bits >= fromBits || ((acc << (toBits - bits)) & maxv) != 0)
        {
            throw new FormatException("Invalid Bech32 padding");
        }
        return ret.ToArray();
    }
}

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

public static class CryptoUtils
{
    //TODO: This class and function are probably unnecessary, and could just be integrated into the other code.  Idk.
    public static byte[] ComputeSharedSecretForCryptoBox(byte[] serverPrivateKey, byte[] clientPublicKey)
    {
        // Step 1: X25519 key agreement
        byte[] rawSharedSecret = new byte[LibSodium.CryptoBox.SharedKeyLen];
        LibSodium.CryptoBox.CalculateSharedKey(rawSharedSecret, clientPublicKey, serverPrivateKey);
        return rawSharedSecret;
    }
}

//This class stores primary configurations like the payout address. 
//It's written on the assumption that each user will run their own boot_portal, and not rely on other people's portals
//  (though that could be done, it's just not a preferable outcome)
public class PoolConfig
{
    [JsonPropertyName("pool_payout_script")]
    public string PoolPayoutScript { get; set; } = "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y"; //TODO: hard coded default address? 

    [JsonPropertyName("coinbase_tag")]
    public string CoinbaseTag { get; set; } = "Boot protocol";

    [JsonPropertyName("prime_id")]
    public uint PrimeId { get; set; } = 21;

    [JsonPropertyName("min_diff")]
    public ulong MinDiff { get; set; } = 1024;
}

//This just stores the server's primary, long term keys.  These get loaded from a config file or from the command line on startup
//If they change, then the client's won't be able to reach the server until this key is updated on each one manually.
// TODO: Do I really need a separate class to store these strings?  Should they be proper LibSodium style Span<T>'s instead for security?
public class ServerConfig
{
    [JsonPropertyName("ed25519_private_key")]
    public string? Ed25519PrivateKey { get; set; }

    [JsonPropertyName("x25519_private_key")]
    public string? X25519PrivateKey { get; set; }
}

public class Program
{
    // TODO: I should optionally load this from config, instead of hard-coded like this.
    private const int DatumPort = 3008;
    private const string ConfigFilePath = "boot_portal_config.json";

    //private PoolConfig? _poolConfig;

    // Utility function to convert bytes to hex string
    //TODO: This is silly.
    private static string ToHexString(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLower();
    }

    public static async Task Main(string[] args)
    {
        var rootCommand = new RootCommand("DATUM Prime C# Server");
        var ed25519PrivateKeyOption = new Option<string?>(
            name: "--ed25519-private-key",
            description: "The Base64 encoded Ed25519 private key for the server. If not provided, loads from config or generates a new key pair."
        );
        var x25519PrivateKeyOption = new Option<string?>(
            name: "--x25519-private-key",
            description: "The Base64 encoded X25519 private key for the server. If not provided, loads from config or generates a new key pair."
        );
        rootCommand.AddOption(ed25519PrivateKeyOption);
        rootCommand.AddOption(x25519PrivateKeyOption);

        rootCommand.SetHandler(async (ed25519PrivateKeyBase64, x25519PrivateKeyBase64) =>
        {
            Key ed25519Key;
            Key x25519Key;
            bool keysGenerated = false;

            var signatureAlgorithm = SignatureAlgorithm.Ed25519;
            var keyExchangeAlgorithm = KeyAgreementAlgorithm.X25519;

            // Load config from boot_portal_config.json if it exists
            ServerConfig config = new ServerConfig();
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(ConfigFilePath);
                    config = JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
                    Console.WriteLine($"✅ Loaded config from {ConfigFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to load {ConfigFilePath}: {ex.Message}. Using default or command-line keys.");
                }
            }

            // Handle Ed25519 key
            //TODO: These load as NSec Key objects, but I don't really use the NSec library anywhere else.
            //  So ideally I'd convert these to whatever secure Span storage LibSodium uses natively, and skip the awkward ".Export()" calls everywhere.
            string? ed25519KeySource = ed25519PrivateKeyBase64 ?? config.Ed25519PrivateKey;
            if (!string.IsNullOrEmpty(ed25519KeySource))
            {
                try
                {
                    var privateKeyBytes = Convert.FromBase64String(ed25519KeySource);
                    ed25519Key = Key.Import(signatureAlgorithm, privateKeyBytes, KeyBlobFormat.RawPrivateKey, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                    Console.WriteLine("✅ Successfully loaded Ed25519 server key.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to load Ed25519 private key: {ex.Message}. Generating new key.");
                    ed25519Key = Key.Create(signatureAlgorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                    config.Ed25519PrivateKey = Convert.ToBase64String(ed25519Key.Export(KeyBlobFormat.RawPrivateKey));
                    keysGenerated = true;
                }
            }
            else
            {
                ed25519Key = Key.Create(signatureAlgorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                config.Ed25519PrivateKey = Convert.ToBase64String(ed25519Key.Export(KeyBlobFormat.RawPrivateKey));
                Console.WriteLine("⚠️ No Ed25519 private key provided. Generated a new long term Ed25519 key pair.");
                keysGenerated = true;
            }

            // Handle X25519 key
            string? x25519KeySource = x25519PrivateKeyBase64 ?? config.X25519PrivateKey;
            if (!string.IsNullOrEmpty(x25519KeySource))
            {
                try
                {
                    var privateKeyBytes = Convert.FromBase64String(x25519KeySource);
                    //Key.Create(signatureAlgorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                    x25519Key = Key.Import(keyExchangeAlgorithm, privateKeyBytes, KeyBlobFormat.RawPrivateKey, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                    Console.WriteLine("✅ Successfully loaded X25519 server key.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to load X25519 private key: {ex.Message}. Generating new key.");
                    x25519Key = Key.Create(keyExchangeAlgorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                    config.X25519PrivateKey = Convert.ToBase64String(x25519Key.Export(KeyBlobFormat.RawPrivateKey));
                    keysGenerated = true;
                }
            }
            else
            {
                x25519Key = Key.Create(keyExchangeAlgorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                config.X25519PrivateKey = Convert.ToBase64String(x25519Key.Export(KeyBlobFormat.RawPrivateKey));
                Console.WriteLine("⚠️ No X25519 private key provided. Generated a new long term X25519 key pair.");
                keysGenerated = true;
            }

            // Save config if keys were generated or file doesn't exist
            if (keysGenerated || !File.Exists(ConfigFilePath))
            {
                try
                {
                    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(ConfigFilePath, json);
                    Console.WriteLine($"✅ Saved keys to {ConfigFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to save {ConfigFilePath}: {ex.Message}");
                }
            }

            // Export public keys
            var ed25519PubKeyBytes = ed25519Key.PublicKey.Export(KeyBlobFormat.RawPublicKey); // 32 bytes
            var x25519PubKeyBytes = x25519Key.PublicKey.Export(KeyBlobFormat.RawPublicKey); // 32 bytes
            var ed25519PrivKeyBytes = ed25519Key.Export(KeyBlobFormat.RawPrivateKey); // 64 bytes
            var x25519PrivKeyBytes = x25519Key.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes

            // Concatenate Ed25519 and X25519 public keys
            var combinedPubKey = new byte[ed25519PubKeyBytes.Length + x25519PubKeyBytes.Length];
            Buffer.BlockCopy(ed25519PubKeyBytes, 0, combinedPubKey, 0, ed25519PubKeyBytes.Length);
            Buffer.BlockCopy(x25519PubKeyBytes, 0, combinedPubKey, ed25519PubKeyBytes.Length, x25519PubKeyBytes.Length);

            // Convert to hex for client
            var combinedPubKeyHex = ToHexString(combinedPubKey); // 128 hex characters

            //Now load or setup the pool config options, like default payout address and coinbase tag
            PoolConfig _poolConfig = LoadPoolConfig(ConfigFilePath);

            Console.WriteLine("\n====================== IMPORTANT ======================");
            Console.WriteLine("Copy this combined public key (Ed25519 + X25519, hex-encoded) into your DATUM Gateway's config.json:");
            Console.WriteLine($"🔑 Server Public Key (Hex): {combinedPubKeyHex}");
            Console.WriteLine("\nSave these private keys to reuse this server identity later:");
            Console.WriteLine($"🔒 Ed25519 Private Key (Base64): {Convert.ToBase64String(ed25519PrivKeyBytes)}");
            Console.WriteLine($"🔒 X25519 Private Key (Base64): {Convert.ToBase64String(x25519PrivKeyBytes)}"); //x25519Key.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes
            Console.WriteLine("=======================================================\n");

            // Start the DATUM server
            var server = new DatumServer(IPAddress.Any, DatumPort, ed25519Key, x25519Key, _poolConfig);
            await server.StartAsync();

            // TODO: Start the Stratum V1 and V2 servers as well, or with .config options just start the chosen servers.
            
            // TODO: Also start the peer to peer node so we can actually connect to the boot-protocol network
        }, ed25519PrivateKeyOption, x25519PrivateKeyOption);

        await rootCommand.InvokeAsync(args);
    }

    private static PoolConfig LoadPoolConfig(string configPath)
    {
        try
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<PoolConfig>(json);
                if (config != null)
                {
                    Console.WriteLine($"🔧 Loaded config from {configPath}");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to load config from {configPath}: {ex.Message}");
        }
        Console.WriteLine($"🔧 Using default pool config");
        return new PoolConfig();
    }
}

// =================================================================================
// 2. DATUM SERVER CLASS
// =================================================================================
// This class opens a TCP socket and listens for incoming connections. When a new
// client connects, it spins up a dedicated 'ClientHandler' to manage it.
// =================================================================================
public class DatumServer
{
    private readonly TcpListener _listener;

    // TODO: I should at least standardize the naming convention of these keys. Can they just be made readable to the client handler threads?
    private readonly Key _serverKey; // The server's long-term Ed25519 key.
    private readonly Key _serverXKey; //The server's long-term x25519 key.

    private PoolConfig _poolConfig;

    public DatumServer(IPAddress address, int port, Key serverKey, Key serverXKey, PoolConfig poolConfig)
    {
        _listener = new TcpListener(address, port);
        _serverKey = serverKey;
        _serverXKey = serverXKey;
        _poolConfig = poolConfig;
    }

    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($"🚀 DATUM Prime Server started on port {_listener.LocalEndpoint}. Waiting for connections...");

        while (true)
        {
            // Asynchronously wait for a client to connect.
            TcpClient client = await _listener.AcceptTcpClientAsync();
            Console.WriteLine($"\n🔗 Client connected from {client.Client.RemoteEndPoint}.");
            
            // Create a handler for the new client.
            var clientHandler = new ClientHandler(client, _serverKey, _serverXKey, _poolConfig);

            // Run the client handler on a background thread so the server
            // can immediately go back to listening for more connections.
            _ = Task.Run(clientHandler.HandleClientAsync);
        }
    }
}


// =================================================================================
// 3. CLIENT HANDLER CLASS
// =================================================================================
// This class does the bulk of the work in managing the connection and passing
// messages to/from clients.
// TODO: Implement some sort of keep/alive, so that DATUM clients don't drop after
//        60 seconds of no contact. 
//        Build out the functions to recieve and respond to POW mining messages
// =================================================================================
public class ClientHandler
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Key _ed25519LongTermKey; // The server's main Ed25519 key.
    private readonly Key _x25519KeyLongTerm; // The server's long-term x25519 key.

    // --- Per-Session State ---
    private PublicKey? _clientSessionPubKey;
    private Key? _serverSessionSigningKey; //ed25519
    private Key? _serverSessionEncryptKey; //x25519
    private SharedSecret? _channelSharedSecret; // The key for symmetric encryption
    private byte[]? _channelSharedSecretBytes;
    private byte[]? _sessionNonceSender; // Server’s send nonce (client’s receive nonce)
    private byte[]? _sessionNonceReceiver; // Server's receive nonce (client's send nonce)
    private UInt32 _sendingHeaderKey;
    private UInt32 _receivingHeaderKey;
    private HelloMessage? _helloMessage;
    private PoolConfig _poolConfig;
    private static readonly PowSubmitMessage?[] JobCache = new PowSubmitMessage?[8];
    private static double BestDiff = 0; 
    private static string BestDiffAddress = null;


    public ClientHandler(TcpClient client, Key serverLongTermKey, Key serverLongTermXKey, PoolConfig poolConfig)
    {
        _client = client;
        _stream = client.GetStream();
        _ed25519LongTermKey = serverLongTermKey;
        _receivingHeaderKey = 0xDC871829; // initial send header key ... changed by handshake function
        _sendingHeaderKey = 0;
        _x25519KeyLongTerm = serverLongTermXKey;
        _poolConfig = poolConfig;
    }

    public async Task HandleClientAsync()
    {
        try
        {
            while (_client.Connected)
            {
                // Step 1: Read the 4-byte header
                var headerBuffer = new byte[4];
                int bytesRead = await _stream.ReadAsync(headerBuffer, 0, headerBuffer.Length);
                if (bytesRead == 0)
                {
                    Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} disconnected (no data).");
                    break;
                }
                if (bytesRead < 4)
                {
                    Console.WriteLine($"⚠️ Partial header received ({bytesRead} bytes): {BitConverter.ToString(headerBuffer, 0, bytesRead)}");
                    break;
                }
                //Console.WriteLine($"📥 Received header bytes: {BitConverter.ToString(headerBuffer)}")
                // Step 1.2: Decode header with XOR key
                uint headerValue = BitConverter.ToUInt32(headerBuffer, 0); // Read as little-endian
                headerValue ^= _receivingHeaderKey; // XOR as 32-bit integer
                var deXoredHeaderBytes = BitConverter.GetBytes(headerValue); // Convert back to bytes
                _receivingHeaderKey = DatumHeaderXorFeedback(_receivingHeaderKey);
                //Console.WriteLine($"📥 De-XORed header bytes: {BitConverter.ToString(deXoredHeaderBytes)}");


                // Step 1.3: Parse header
                var header = DatumHeader.FromBytes(deXoredHeaderBytes);
                //Console.WriteLine($"📋 Parsed header: Cmd={header.ProtoCmd}, Len={header.CmdLen}, Signed={header.IsSigned}, EncryptedPubKey={header.IsEncryptedPubKey}, EncryptedChannel={header.IsEncryptedChannel}");

                // Step 2: Read in the message body
                var bodyBuffer = new byte[header.CmdLen];
                bytesRead = await _stream.ReadAsync(bodyBuffer, 0, bodyBuffer.Length);
                if (bytesRead == 0) {Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} disconnected (no body).");  break; }
                //Console.WriteLine($"📦 Received encrypted body ({bytesRead} bytes)");
                
                // Step 3: Decrypt the body
                byte[]? decryptedBody;
                //TODO: This if-else could be more robust, and check header.isEncryptedChannel as well
                if (header.IsEncryptedPubKey)
                {
                    decryptedBody = DecryptSigned(bodyBuffer, bytesRead);
                    // Verify cmd_len matches decrypted body length
                    //TODO: change "48" to actually reference the libsodium constant instead.
                    //Modified (+48) to account for CryptoBoxSealBytes, the signature that is added to the encrypted payload.
                    if (header.CmdLen != decryptedBody.Length + 48) { Console.WriteLine($"⚠️ Header cmd_len ({header.CmdLen}) does not match decrypted body length ({decryptedBody.Length})"); break; }
                }  //      We need to use a different decryption key depending on the header.protoCmmd
                else
                {
                    decryptedBody = DecryptStandard(bodyBuffer, bytesRead);
                    // Verify cmd_len matches decrypted body length
                    //TODO: change "16" to actually reference the libsodium constant instead.
                    //Modified (+16) to account for MAC bytes, the signature that is added to the encrypted payload.  I think.
                    if (header.CmdLen != decryptedBody.Length + 16) { Console.WriteLine($"⚠️ Header cmd_len ({header.CmdLen}) does not match decrypted body length ({decryptedBody.Length})"); break;}

                }
                if (decryptedBody == null)
                {
                    Console.WriteLine(" Header info: Cmd=" + (header.ProtoCmd) + " / CmdLen=" + header.CmdLen + " / isSigned=" + header.IsSigned + " / isEncryptedPubKey=" + header.IsEncryptedPubKey + " / isEncryptedChannel=" + header.IsEncryptedChannel);
                    Console.WriteLine($"❌ Failed to decrypt body for client {_client.Client.RemoteEndPoint}");
                    break;
                }
                //Console.WriteLine($"🔓 Decrypted body ({decryptedBody.Length} bytes)");
                
                // Step 4: Parse the message appropriately.  Responses are generated in the appropriate "Handle" function.
                //Console.WriteLine($"[RECV] Command: 0x{header.ProtoCmd:X2}, Length: {header.CmdLen} bytes");
                switch (header.ProtoCmd)
                {
                    case 0x01: await HandleHelloAsync(header, decryptedBody); break;
                    case 0x05: await HandleMiningCommandAsync(header, decryptedBody); break;
                    default:
                        Console.WriteLine("Header xor Key=" + _receivingHeaderKey);
                        Console.WriteLine(" Header info: Cmd=" + (header.ProtoCmd) + " / CmdLen=" + header.CmdLen + " / isSigned=" + header.IsSigned + " / isEncryptedPubKey=" + header.IsEncryptedPubKey + " / isEncryptedChannel=" + header.IsEncryptedChannel);
                        Console.WriteLine($"⚠️ Received unknown command: 0x{header.ProtoCmd:X2}"); break;
                }
                //Finally back to the top of the loop and await the next incoming message
            }
        }
        catch (IOException) { Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} disconnected."); }
        catch (Exception ex) { Console.WriteLine($"💥 An error occurred with client {_client.Client.RemoteEndPoint}: {ex.Message}\n{ex.StackTrace}"); }
        finally { _client.Close(); }
    }


    private byte[]? DecryptSigned(byte[] encryptedBody, int bytesRead)
    {
        //Console.WriteLine($"📦 Ciphertext first 32 bytes: {BitConverter.ToString(encryptedBody, 0, 32)}");
        //Console.WriteLine($"📦 Ciphertext first all bytes: {BitConverter.ToString(encryptedBody)}");
        try
        {
            const int CryptoBoxSealBytes = 48; // 48 (32 ephemeral PK + 16 Poly1305 tag)
            if (bytesRead < CryptoBoxSealBytes) { Console.WriteLine($"❌ Ciphertext too short: {bytesRead} bytes"); return null; }

            // Use the X25519 key pair directly
            //TODO: Switch these from NSec keys to whatever Span<T> thing LibSodium recommends
            var privateKeyBytes = _x25519KeyLongTerm.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes

            // Truncate input to actual length
            var cipherText = encryptedBody.AsSpan(0, bytesRead).ToArray();

            // Decrypt using crypto_box_seal_open
            //Span<byte> decrypted = new Span<byte>();
            byte[] decrypted = new byte[encryptedBody.Length - CryptoBoxSealBytes];
            LibSodium.CryptoBox.DecryptWithPrivateKey(decrypted, cipherText, privateKeyBytes);
            if (decrypted == null) { Console.WriteLine("❌ Decryption failed: Sodium.SealedPublicKeyBox.Open returned null"); return null; }

            //Console.WriteLine($"🔓 Decrypted {decrypted.Length} bytes");
            //Console.WriteLine($"-> {BitConverter.ToString(decrypted)}");
            //Console.WriteLine($"🔓 Client signing public key:    {BitConverter.ToString(decrypted, 0, 16)}...");
            //Console.WriteLine($"🔓 Client encryption public key: {BitConverter.ToString(decrypted, 32, 16)}...");
            return decrypted;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Decryption error: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private byte[]? DecryptStandard(byte[] encryptedBody, int bytesRead)
    {
        try
        {
            const int CryptoBoxSealBytes = 48; // 48 (32 ephemeral PK + 16 Poly1305 tag)
            if (bytesRead < CryptoBoxSealBytes) { Console.WriteLine($"❌ Ciphertext too short: {bytesRead} bytes"); return null; }

            // Use the X25519 key pair directly
            //TODO: Switch these from NSec keys to whatever Span<T> thing LibSodium recommends
            //if (_channelSharedSecretBytes == null) { Console.WriteLine("_serverSessionEncryptKey is null!"); return null; }
            //var privateKeyBytes = _channelSharedSecret.Export(KeyBlobFormat.RawPrivateKey);       //_x25519KeyLongTerm.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes

            // Truncate input to actual length
            var cipherText = encryptedBody.AsSpan(0, bytesRead).ToArray();
            byte[] combinedCiphertext = new byte[bytesRead + LibSodium.CryptoBox.NonceLen];
            Array.Copy(_sessionNonceReceiver, 0, combinedCiphertext, 0, LibSodium.CryptoBox.NonceLen);
            Array.Copy(encryptedBody, 0, combinedCiphertext, LibSodium.CryptoBox.NonceLen, bytesRead);


            //Span<byte> decrypted = new Span<byte>();
            byte[] plaintext = new byte[bytesRead - LibSodium.CryptoBox.MacLen];
            LibSodium.CryptoBox.DecryptWithSharedKey(plaintext, combinedCiphertext, _channelSharedSecretBytes);
            //LibSodium.CryptoBox.DecryptWithSharedKey(decrypted, cipherText, _channelSharedSecretBytes, null, _sessionNonceReceiver);  //Prolly need to add nonce and MAC, or something.  Need to see how client does it.
            if (plaintext == null)
            {
                Console.WriteLine("❌ Decryption failed: Sodium.DecryptWithSharedKey returned null");
                Console.WriteLine($"🔑 /// Session nonce sender: {Convert.ToBase64String(_sessionNonceSender)}");
                Console.WriteLine($"🔑 /// Session nonce receiver: {Convert.ToBase64String(_sessionNonceReceiver)}");
                return null;
            }
            _sessionNonceReceiver = IncrementNonce(_sessionNonceReceiver);

            //Console.WriteLine($"🔓 Decrypted {decrypted.Length} bytes");
            //Console.WriteLine($"-> {BitConverter.ToString(decrypted)}");            
            return plaintext;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Decryption error: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private byte[] InitializeNonce(uint nk, byte[] clientSessionEd25519PubKey)
    {
        var nonce = new byte[24];
        nk -= 42;
        nk ^= BitConverter.ToUInt32(clientSessionEd25519PubKey, 7);
        for (int j = 0; j < 24; j += 4)
        {
            uint value = DatumHeaderXorFeedback(nk - 42);
            nonce[j] = (byte)(value);
            nonce[j + 1] = (byte)(value >> 8);
            nonce[j + 2] = (byte)(value >> 16);
            nonce[j + 3] = (byte)(value >> 24);
            nk = BitConverter.ToUInt32(nonce, j);
            nk = ~nk;
        }
        return nonce;
    }

    private byte[] InitializeReceiverNonce(byte[] senderNonce)
    {
        var receiverNonce = new byte[24];
        for (int j = 0; j < 24; j += 4)
        {
            uint senderValue = BitConverter.ToUInt32(senderNonce, j);
            uint receiverValue = senderValue ^ 0x57575757;
            receiverNonce[j] = (byte)(receiverValue);
            receiverNonce[j + 1] = (byte)(receiverValue >> 8);
            receiverNonce[j + 2] = (byte)(receiverValue >> 16);
            receiverNonce[j + 3] = (byte)(receiverValue >> 24);
        }
        return receiverNonce;
    }

    private byte[] IncrementNonce(byte[] nonce)
    {
        // Increment nonce as a little-endian 192-bit integer
        for (int i = 0; i < nonce.Length; i++)
        {
            if (++nonce[i] != 0) break;
        }
        return nonce;
    }

    /// Handles the initial 0x01 handshake message from the client.
    private async Task HandleHelloAsync(DatumHeader header, byte[] decryptedBody)
    {
        //Console.WriteLine("   -> Received HELLO (0x01). Processing...");
        var bytesConsumed = 0;
        (_helloMessage, bytesConsumed) = HelloMessage.FromBytes(decryptedBody);
        if (_helloMessage == null || bytesConsumed < 0)
        {
            Console.WriteLine($"❌ Failed to parse hello message for client {_client.Client.RemoteEndPoint}");
            return;
        }
        if (bytesConsumed != decryptedBody.Length)
        {
            Console.WriteLine($"⚠️ Parsed {bytesConsumed} bytes, but decrypted body is {decryptedBody.Length} bytes");
            return;
        }

        //Initialize a new ed25519 key for signing the session messages with
        //TODO: Switch these from NSec Keys to whatever Span<T> LibSodium uses
        _serverSessionSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        _clientSessionPubKey = PublicKey.Import(KeyAgreementAlgorithm.X25519, _helloMessage.ClientSessionEncryptPubKey, KeyBlobFormat.RawPublicKey);
        _serverSessionEncryptKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        _channelSharedSecretBytes = CryptoUtils.ComputeSharedSecretForCryptoBox(_serverSessionEncryptKey.Export(KeyBlobFormat.RawPrivateKey), _clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey));
        _channelSharedSecret = SharedSecret.Import(_channelSharedSecretBytes, SharedSecretBlobFormat.RawSharedSecret);

        //Console.WriteLine("//////////////  SHARED KEY PRECOMP   /////////////");
        //var x25519PubKeyBytes = _clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey); // 32 bytes        
        //var x25519PrivKeyBytes = _serverSessionEncryptKey.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes
        //Console.WriteLine($"🔒 X25519 Server Pub Key (Base64): {Convert.ToBase64String(_serverSessionEncryptKey.Export(KeyBlobFormat.RawPublicKey))}");
        //Console.WriteLine($"🔒 X25519 Server Pri Key (Base64): {Convert.ToBase64String(_serverSessionEncryptKey.Export(KeyBlobFormat.RawPrivateKey))}");
        //Console.WriteLine($"🔒 X25519 Client Pub Key (Base64): {Convert.ToBase64String(_clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey))}"); //x25519Key.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes
        //Console.WriteLine($"🔒 X25519 Shared Raw Key (Base64): {Convert.ToBase64String(_channelSharedSecretBytes)}");
        //Console.WriteLine($"🔒 X25519 Shared NSecKey (Base64): {Convert.ToBase64String(_channelSharedSecret.Export(SharedSecretBlobFormat.NSecSharedSecret))}");
        //Console.WriteLine($"🔒 X25519 Shared Raw Key (Base64): {Convert.ToBase64String(_channelSharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret))}");
        /*
                // This is all old test code for verifying that we can properly compute the shared secret key
        string b64String = "H3wh/J71/HSqLNmY2tz9DuDkiPYjPLnCBzk7/gh1Rg8="; // 32 characters long
        byte[] testClientPKBytes = Convert.FromBase64String(b64String);//Encoding.GetBytes(b63String);
        PublicKey testClientPK = PublicKey.Import(KeyAgreementAlgorithm.X25519, testClientPKBytes, KeyBlobFormat.RawPublicKey);
        string b64String2 = "/CLqYMkM3l5GxfL4BqXGFlpvTDEATzcqxzsCX1Yqijo=";
        byte[] testServerBytes = Convert.FromBase64String(b64String2);//Encoding.ASCII.GetBytes(b64String2);
        Key testServerPrK = Key.Import(KeyAgreementAlgorithm.X25519, testServerBytes, KeyBlobFormat.RawPrivateKey, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        //SharedSecret testSharedSecret = KeyAgreementAlgorithm.X25519.Agree(testServerPrK, testClientPK, new SharedSecretCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var testSharedKeyBytes = CryptoUtils.ComputeSharedSecretForCryptoBox(testServerPrK.Export(KeyBlobFormat.RawPrivateKey), testClientPK.Export(KeyBlobFormat.RawPublicKey));
        SharedSecret testSharedSecret = SharedSecret.Import(testSharedKeyBytes, SharedSecretBlobFormat.RawSharedSecret);
        Console.WriteLine($"🔒 X25519 test share Key (Base64): {Convert.ToBase64String(testSharedKeyBytes)}");
        Console.WriteLine("//////////////  SHARED KEY PRECOMP   /////////////");
        */

        uint nk = 0;
        if (_helloMessage.xorKey != null) { nk = BitConverter.ToUInt32(_helloMessage.xorKey, 0); }
        else return;
        _sendingHeaderKey = DatumHeaderXorFeedback(~nk);        //Increment the header key for sending and recieving future message headers
        _receivingHeaderKey = DatumHeaderXorFeedback(nk);
        //Console.WriteLine($"🔑 Updated XOR keys: Sending=0x{_sendingHeaderKey:X8}, Receiving=0x{_receivingHeaderKey:X8}");   
        if (nk == 0) throw new InvalidOperationException("Failed to extract XOR key");
        _sessionNonceSender = InitializeNonce(nk, _helloMessage.ClientSessionSigningPubKey);          // Initialize nonce     
        _sessionNonceReceiver = InitializeReceiverNonce(_sessionNonceSender);
        //Console.WriteLine($"🔑 Initial Session nonce sender: {Convert.ToBase64String(_sessionNonceSender)}");
        //Console.WriteLine($"🔑 Initial Session nonce receiver: {Convert.ToBase64String(_sessionNonceReceiver)}");

        // Send response
        var responsePayload = new HandshakeResponseMessage { /* ... payload initialization ... */ };
        // (The rest of the response generation logic is unchanged, but the encryption call will be fixed in the helper method)
        // First we have to echo the 4 keys that the client sent, or they will reject the handshake
        responsePayload.ClientSigningPubKey = _helloMessage.ClientSigningPubKey;
        responsePayload.ClientEncryptPubKey = _helloMessage.ClientEncryptPubKey;
        responsePayload.ClientSessionSigningPubKey = _helloMessage.ClientSessionSigningPubKey;
        responsePayload.ClientSessionEncryptPubKey = _helloMessage.ClientSessionEncryptPubKey;
        // Next we need to send the client our session public keys for signing and encryption
        responsePayload.ServerSessionSigningPubKey = _serverSessionSigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey); //ed25519
        responsePayload.ServerSessionEncryptPubKey = _serverSessionEncryptKey.PublicKey.Export(KeyBlobFormat.RawPublicKey); //x25519

        var responsePayloadBytes = responsePayload.ToBytes();
        //Console.WriteLine($"📦 Response payload: {BitConverter.ToString(responsePayloadBytes)}");
        var signature = SignatureAlgorithm.Ed25519.Sign(_ed25519LongTermKey, responsePayloadBytes);
        //Console.WriteLine($"📦 Signature: {BitConverter.ToString(signature)}");
        var signedPayload = responsePayloadBytes.Concat(signature).ToArray();
        //Console.WriteLine($"📦 Signed payload (corrected): {BitConverter.ToString(signedPayload)}");
        await SendEncryptedMessageAsync(0x02, signedPayload, true, false, true);
        //Console.WriteLine($"[SEND] Handshake Response (0x02), length " + signedPayload.Length);
        await SendClientConfigureAsync(_poolConfig);          // Send 0x99 client configure message
    }

    private async Task SendClientConfigureAsync(PoolConfig config)
    {
        // Construct payload
        var payload = new List<byte>();

        // Sub-command: 0x99
        payload.Add(0x99);

        // Version: 0x01
        payload.Add(0x01);

        // Pool payout script
        byte[] poolScriptBytes = Encoding.UTF8.GetBytes(config.PoolPayoutScript);
        //TODO: What is the pool payout script sig?  I have no idea.
        if (poolScriptBytes.Length > 255)
        {
            Console.WriteLine($"⚠️ Pool payout script too long ({poolScriptBytes.Length} bytes), truncating to 255");
            Array.Resize(ref poolScriptBytes, 255);  // This is really dumb.  Stupid AI wrote it.
        }
        payload.Add((byte)poolScriptBytes.Length);
        payload.AddRange(poolScriptBytes);

        // Prime ID
        payload.AddRange(BitConverter.GetBytes(config.PrimeId)); // Little-endian uint32

        // Coinbase tag
        byte[] coinbaseTagBytes = Encoding.UTF8.GetBytes(config.CoinbaseTag);
        if (coinbaseTagBytes.Length > 255)
        {
            Console.WriteLine($"⚠️ Coinbase tag too long ({coinbaseTagBytes.Length} bytes), truncating to 255");
            Array.Resize(ref coinbaseTagBytes, 255);
        }
        payload.Add((byte)coinbaseTagBytes.Length);
        payload.AddRange(coinbaseTagBytes);

        // Minimum difficulty
        payload.AddRange(BitConverter.GetBytes(config.MinDiff)); // Little-endian uint64

        // Terminator: 0x00 0xFE
        payload.Add(0x00);
        payload.Add(0xFE);

        // Convert to bytes
        byte[] payloadBytes = payload.ToArray();
        //Console.WriteLine($"📦 Client configure payload (before signing): {BitConverter.ToString(payloadBytes)}");

        // Generate signature
        if (_serverSessionSigningKey == null){ Console.WriteLine("Server Session Signing Key is null!"); return; }
        var signature = SignatureAlgorithm.Ed25519.Sign(_serverSessionSigningKey, payloadBytes);
        //Console.WriteLine($"📦 Signature: {BitConverter.ToString(signature)}");

        // Append signature
        var signedPayload = payloadBytes.Concat(signature).ToArray();
        //Console.WriteLine($"📦 Signed payload: {BitConverter.ToString(signedPayload)}");

        // Send encrypted message (mining command 0x05, channel encryption)
        Console.WriteLine("[SEND} Sending client configuration message 0x05/0x99");
        await SendEncryptedMessageAsync(0x05, signedPayload, isSigned: true, isEncryptedChannel: true, isEncryptedPubKey: false);
    }

    /// Handles all mining-related commands (sub-commands under 0x05).
    private async Task HandleMiningCommandAsync(DatumHeader header, byte[] decryptedBody)
    {
        byte subCmd = decryptedBody[0];
        byte[] subCmdPayload = decryptedBody.Skip(1).ToArray();
        //Console.WriteLine($"[RECV] Mining Command (0x05), Sub-Command: 0x{subCmd:X2}");
        switch (subCmd)
        {
            case 0x10: await HandleCoinbaserFetchAsync(subCmdPayload); break;
            case 0x27: await HandlePowSubmitAsync(subCmdPayload); break;
            default: Console.WriteLine($"   -> Received unknown mining sub-command: 0x{subCmd:X2}"); break;
        }
    }

    private async Task HandleCoinbaserFetchAsync(byte[] payload)
    {
        var fetchRequest = CoinbaserFetchMessage.FromBytes(payload);
        //Console.WriteLine($"   -> Coinbase Fetch request with total reward: {fetchRequest.RewardValue / 100_000_000.0} BTC");
        var fetchResponse = new CoinbaserFetchResponseMessage();
        fetchResponse.Payouts.Add(new PayoutInfo
        {
            Value = fetchRequest.RewardValue,
            Address = "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y"
        });

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0x11); // Sub-command
        writer.Write(fetchRequest.RewardValue); // uint64_t v
        var payoutBytes = fetchResponse.ToBytes();
        writer.Write((uint)payoutBytes.Length); // uint32_t x
        writer.Write(payoutBytes);
        var responsePayload = stream.ToArray();

        //Console.WriteLine($"📦 Coinbase fetch response payload: {BitConverter.ToString(responsePayload)}");
        await SendEncryptedMessageAsync(0x05, responsePayload, isSigned: false, isEncryptedChannel: true, isEncryptedPubKey: false);
        //Console.WriteLine($"[SEND] Coinbaser Fetch Response (0x05, 0x11)");
    }

    private async Task HandlePowSubmitAsync(byte[] payload)
    {
        var powSubmit = PowSubmitMessage.FromBytes(payload);
        //if (powSubmit.JobId == null) return;    
        if (powSubmit.PrevBlockHash == null)  //This is just a nonce update, does not include complete header info
        {
            //JobCache[powSubmit.JobId].Update(powSubmit);  //There's already job data stored here, so just update the new data
            JobCache[powSubmit.JobId].CoinbaseId = powSubmit.CoinbaseId;  //maybe not required...
            JobCache[powSubmit.JobId].IsBlock = powSubmit.IsBlock;
            JobCache[powSubmit.JobId].SubsidyOnly = powSubmit.SubsidyOnly;
            JobCache[powSubmit.JobId].QuickDiff = powSubmit.QuickDiff;
            JobCache[powSubmit.JobId].TargetByte = powSubmit.TargetByte;
            JobCache[powSubmit.JobId].NTime = powSubmit.NTime;
            JobCache[powSubmit.JobId].Nonce = powSubmit.Nonce;
            JobCache[powSubmit.JobId].Version = powSubmit.Version;
            JobCache[powSubmit.JobId].ExtranonceSize = powSubmit.ExtranonceSize;  //Always 12, but whatever
            JobCache[powSubmit.JobId].Extranonce = powSubmit.Extranonce;
            JobCache[powSubmit.JobId].Username = powSubmit.Username;
            //Now check if we got new coinbase data with this share:
            if (powSubmit.CoinbasePairs[powSubmit.CoinbaseId].Coinb1 != null)  // Got a new coinbase with this one
            {
                JobCache[powSubmit.JobId].CoinbasePairs[powSubmit.CoinbaseId] = powSubmit.CoinbasePairs[powSubmit.CoinbaseId];
            }
            powSubmit = JobCache[powSubmit.JobId];  //Copies back over the Merkle Branch info.
        }
        else JobCache[powSubmit.JobId] = powSubmit;  //New job, with complete header info.  
        //TODO: Technically, there is the very edge case that a miner could reuse old coinbase info with a new job and merkle branches.  This case isn't handled right now.
        // Now compute the latest Merkle Root.  We have to do this for every share submission, since the extranonce changes every time.
        byte[] Coinb1 = powSubmit.CoinbasePairs[powSubmit.CoinbaseId].Coinb1;
        byte[] Coinb2 = powSubmit.CoinbasePairs[powSubmit.CoinbaseId].Coinb2;
        byte[] coinbaseTx = Coinb1.Concat(powSubmit.Extranonce).Concat(Coinb2).ToArray();
        Console.WriteLine($"Coinb1: {BitConverter.ToString(Coinb1).Replace("-", "")}");
        Console.WriteLine($"Extranonce: {BitConverter.ToString(powSubmit.Extranonce).Replace("-", "")}");
        Console.WriteLine($"Coinb2: {BitConverter.ToString(Coinb2).Replace("-", "")}");
        Console.WriteLine($"coinbaseTx: {BitConverter.ToString(coinbaseTx).Replace("-", "")}");
        Console.WriteLine($"Merkle Count: {powSubmit.MerkleBranchCount}");
        Console.WriteLine($"TargetByte: {powSubmit.TargetByte}");
        Console.WriteLine($"TargetByteIndex: {powSubmit.TargetByteIndex}");
        
        //byte[] testCoinbase1 = Convert.FromHexString("020000000001010000000000000000000000000000000000000000000000000000000000000000ffffffff2303780b0e225075626c696320506f6f6c206f6e20556d6272656c2251cf70860019ccefffffffff");
        //byte[] testCoinbase2 = Convert.FromHexString("029c04b912000000001600145ea459e521b0d95d521f0dbc1596e9c54d29d9db0000000000000000266a24aa21a9ed46f10a6f564fcb1758d78fcf8d63cb82d6379e5ce53de187b7545589c1cac16a0120000000000000000000000000000000000000000000000000000000000000000000000000");
        //byte[] legacyPrefix = testCoinbase1.Take(4).ToArray();
        //byte[] legacyBody = testCoinbase1.Skip(6).ToArray();
        //byte[] testCoinbase = legacyPrefix.Concat(legacyBody).Concat(testCoinbase2).ToArray();

        if (powSubmit.QuickDiff)
        {
            Console.WriteLine("   using quickdiff");
            // ----- quickdiff magic word (last 2 bytes of Coinb1) -----
            int quickDiffOffset = Coinb1.Length - 2;   // client: cb->coinb1_len - 2
            if (quickDiffOffset < 0)
            {
                Console.WriteLine("Coinb1 too short for quickdiff magic");
            }
            else
            {
                ushort current = BitConverter.ToUInt16(Coinb1, quickDiffOffset);
                ushort magic = current == 0x5144 ? (ushort)0xAEBB : (ushort)0x5144;

                byte[] magicBytes = BitConverter.GetBytes(magic);
                if (BitConverter.IsLittleEndian) Array.Reverse(magicBytes);   // pk_u16le writes LE

                Array.Copy(magicBytes, 0, coinbaseTx, quickDiffOffset, 2);
            }

            // ----- quickdiff target byte -----
            // The client uses the *quick* difficulty that the miner was asked for
            byte quickPot = FloorPoT(powSubmit.TargetByte);   // you already have this helper
            Console.WriteLine($"quickPot: {quickPot}");
            if (powSubmit.TargetByteIndex.HasValue)
            {
                int idx = powSubmit.TargetByteIndex.Value;
                if (idx >= 0 && idx < Coinb1.Length)
                    coinbaseTx[idx] = quickPot;
                else
                    Console.WriteLine($"QuickDiff TargetByteIndex {idx} out of range (coinbase size {coinbaseTx.Length})");
            }
            else Console.WriteLine($"QuickDiff TargetByteIndex has no value)");
        }
        else
        {
            // ----- normal (non-quickdiff) target byte -----
            // The client uses the difficulty that belongs to the current stratum job
            byte normalPot = FloorPoT(powSubmit.TargetByte);   // you must expose this value
            Console.WriteLine($"normalPot: {normalPot:X1}");
            if (powSubmit.TargetByteIndex.HasValue)
            {
                int idx = powSubmit.TargetByteIndex.Value;
                if (idx >= 0 && idx < coinbaseTx.Length)
                {
                    Console.WriteLine($"coinbaseTx[idx]: {coinbaseTx[idx]}");
                    coinbaseTx[idx] = normalPot;
                    Console.WriteLine($"coinbaseTx[idx]: {coinbaseTx[idx]}");
                }
                else
                    Console.WriteLine($"TargetByteIndex {idx} out of range (coinbase size {coinbaseTx.Length})");
            }
        }

        //var testCBHash = DoubleSha256(coinbaseTx);
        //var testMerkleRoot = ComputeMerkleRoot(testCBHash, powSubmit.MerkleBranches, powSubmit.MerkleBranchCount.Value);
        //Console.WriteLine($"testCBHash = {BitConverter.ToString(testCBHash)}");
        //Console.WriteLine($"testMerkle = {BitConverter.ToString(testMerkleRoot)}");
        byte[] coinbaseHash = DoubleSha256(coinbaseTx);
        powSubmit.MerkleRoot = ComputeMerkleRoot(coinbaseHash, powSubmit.MerkleBranches, powSubmit.MerkleBranchCount.Value);
        JobCache[powSubmit.JobId].MerkleRoot = powSubmit.MerkleRoot; //For completeness, I guess.

        //Test merkle tree data from block 123482
        //byte[] cb = Convert.FromHexString("28529fd87f187a4fdc7c70c0a12e86d4b32d398fd71343b2a51b952b6b238def"); //TXID (rev): ef8d236b2b951ba5b24313d78f392db3d4862ea1c0707cdc4f7a187fd89f5228
        //byte[] tx1 = Convert.FromHexString("0fdbd6314800158e12e2699ede24b07a8fc2f00f0fca8fee6491bf690a2c57ea"); //TXID (rev): ea572c0a69bf9164ee8fca0f0ff0c28f7ab024de9e69e2128e15004831d6db0f
        //byte[] final = DoubleSha256(cb.Concat(tx1).ToArray());
        //byte[] actual = Convert.FromHexString("5509c987862865584458e44ab147d061168fdf666fbddb99b5134c73a66a0bb4");
        //if (final == actual) Console.WriteLine("huzzah!!!!  They match!!!");
        //else
        //{
        //Console.WriteLine(BitConverter.ToString(final).Replace("-", ""));
        //Console.WriteLine(BitConverter.ToString(actual).Replace("-", ""));
        //}         

        // Reconstruct block header
        //powSubmit.Version = 0x2f438000;  //For testing version logic
        byte[] header = new byte[80];
        using (var stream = new MemoryStream(header))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(powSubmit.Version); // 4 bytes
            writer.Write(powSubmit.PrevBlockHash); // 32 bytes
            writer.Write(powSubmit.MerkleRoot.Reverse().ToArray()); // 32 bytes  
            writer.Write(powSubmit.NTime); // 4 bytes
            writer.Write(powSubmit.NBits); // 4 bytes
            writer.Write(powSubmit.Nonce); // 4 bytes
        }
        //Console.WriteLine($"Version (Byte): {Convert.ToByte(powSubmit.Version)}");
        //Console.WriteLine($"Version (String): {Convert.ToString(powSubmit.Version)}");
        if (powSubmit.SubsidyOnly) Console.WriteLine("*** Got subsidy only coinbase message!");
        Console.WriteLine($"PoW header: {BitConverter.ToString(header).Replace("-", "")}");

        Console.Write($"{powSubmit.Version:X8} | ");
        Console.WriteLine($"Version (B32): 0b{powSubmit.Version:B32} | ");
        Console.Write($"{BitConverter.ToString(powSubmit.PrevBlockHash).Replace("-", "")} | ");
        Console.Write($"{BitConverter.ToString(powSubmit.MerkleRoot).Replace("-", "")} | ");
        Console.Write($"{BitConverter.ToString(BitConverter.GetBytes(powSubmit.NTime))} | ");
        Console.Write($"{BitConverter.ToString(powSubmit.NBits).Replace("-", "")} | ");
        Console.Write($"{BitConverter.ToString(BitConverter.GetBytes(powSubmit.Nonce))}\n");



        /*
                // Test Bitcoin block 756951
                byte[] testHeader = new byte[80];
                using (var stream = new MemoryStream(testHeader))
                using (var writer = new BinaryWriter(stream))
                {
                    // Version: 0x20400000 (little-endian: 00004020)
                    uint version = 0x20400000;
                    writer.Write(version); // 4 bytes

                    // PrevBlockHash: 000000000000000000050da0da9451c2e1306db4ddb5acc965fc1016678d9154 (big-endian)
                    byte[] prevBlockHash = Convert.FromHexString("54918d671610fc65c9acb5ddb46d30e1c25194daa00d05000000000000000000");
                    writer.Write(prevBlockHash); // 32 bytes

                    // MerkleRoot: 62c46f1efadf6e39b7463e5362bb552cba98f74a80a58378ff5194c7b058005a (big-endian)
                    byte[] merkleRoot = Convert.FromHexString("5a0058b0c79451ff7883a5804af798ba2c55bb62533e46b7396edffa1e6fc462");
                    writer.Write(merkleRoot); // 32 bytes

                    // NTime: 0x633b8c2d (little-endian: 2d8c3b63)
                    uint nTime = 0x633b8c2d;
                    writer.Write(nTime); // 4 bytes

                    // NBits: 0x1708f9ae (big-endian: aef90817)
                    byte[] nBits = Convert.FromHexString("aef90817");
                    writer.Write(nBits); // 4 bytes

                    // Nonce: 0xc1230f8c (little-endian: 8c0f23c1)
                    uint nonce = 0xc1230f8c;
                    writer.Write(nonce); // 4 bytes
                }
        */
        // Verify header
        //Console.WriteLine($"Test Header: {BitConverter.ToString(testHeader).Replace("-", "")}");

        // Compute hash
        byte[] testHash = DoubleSha256(header);  //testHeader
        byte[] reversedTestHash = testHash.Reverse().ToArray();
        Console.WriteLine($"Test Hash (raw): {BitConverter.ToString(testHash).Replace("-", "")}");
        Console.WriteLine($"Test Hash (reversed, explorer): {BitConverter.ToString(reversedTestHash).Replace("-", "")}");

        // Achieved difficulty (hash-based)
        BigInteger hashInt = 0;
        for (int i = 0; i < testHash.Length; i++)//(int i = testHash.Length - 1; i >= 0; i--)
        {
            hashInt = (hashInt << 8) | testHash[i];
        }
        BigInteger maxTarget = BigInteger.Pow(2, 224) - 1;
        BigInteger achievedDifficultyBig = hashInt == 0 ? 0 : maxTarget / hashInt;
        //double achievedDifficulty = (double)achievedDifficultyBig;
        //Console.WriteLine($"Achieved Difficulty: {achievedDifficulty} ({FormatDifficulty(achievedDifficulty)})");

        // Required difficulty (nBits-based)
        //BigInteger target = (ComputeTargetFromNBits(Convert.FromHexString("aef90817")));
        //BigInteger requiredDifficultyBig = target == 0 ? 0 : maxTarget / target;
        //double requiredDifficulty = (double)requiredDifficultyBig;
        //Console.WriteLine($"Required Difficulty: {requiredDifficulty} ({FormatDifficulty(requiredDifficulty)})");

        // Leading zero bits (for your preferred metric)
        /*
        ulong leadingZeroBits = 0;
        bool foundNonZero = false;
        foreach (byte b in reversedTestHash)
        {
            if (b == 0)
            {
                leadingZeroBits += 8;
            }
            else
            {
                int zeros = 0;
                for (int i = 7; i >= 0; i--)
                {
                    if ((b & (1 << i)) == 0) zeros++;
                    else break;
                }
                leadingZeroBits += (ulong)zeros;
                foundNonZero = true;
                break;
            }
        }
        if (!foundNonZero) leadingZeroBits = 256;
        */
        //Console.WriteLine($"Leading Zero Bits: {leadingZeroBits}");

        //Console.WriteLine($"   -> ✅ Received PoW submission: JobID={powSubmit.JobId}, CoinbaseID={powSubmit.CoinbaseId}, IsBlock={powSubmit.IsBlock}, SubsidyOnly={powSubmit.SubsidyOnly}, QuickDiff={powSubmit.QuickDiff}, Username={powSubmit.Username}");

        // 1. Check Difficulty
        //ulong difficulty = CalculateDifficulty(powSubmit.TargetByte, powSubmit.TargetByteIndex, powSubmit.NBits);
        double difficulty = (double)achievedDifficultyBig;
        if (difficulty > BestDiff)
        {
            BestDiff = difficulty;
            BestDiffAddress = powSubmit.Username;
        }


        // 2. Verify Block Header
        bool isHeaderValid = VerifyBlockHeader(powSubmit.Version, powSubmit.NTime, powSubmit.Nonce, powSubmit.PrevBlockHash, powSubmit.MerkleRoot, powSubmit.NBits);
        //Console.WriteLine($"   -> Header valid: {isHeaderValid}");

        // 3. Extract Username
        string minerAddress = powSubmit.Username;  //TODO: ideally we extract this from the coinbase transaction
        //Console.WriteLine($"   -> Miner address: {minerAddress}");

        // 4. Verify Coinbase Transaction
        var (isValidCoinbase, outputs) = VerifyCoinbaseTransaction(Coinb1, Coinb2, powSubmit.CoinbaseValue);
        //Console.WriteLine($"   -> Coinbase valid: {isValidCoinbase}");
        foreach (var output in outputs)
        {
            //Console.WriteLine($"   -> Coinbase output: Address={output.Address}, Amount={output.Amount / 100_000_000.0} BTC");
        }
        if (powSubmit.QuickDiff) Console.WriteLine("QuickDiff!!!");
        Console.WriteLine($"POW: {powSubmit.JobId}\t{powSubmit.CoinbaseId}\t{difficulty}\t{powSubmit.Username}\n");

        // Respond
        //#define DATUM_POW_SHARE_RESPONSE_ACCEPTED 0x50
        //#define DATUM_POW_SHARE_RESPONSE_ACCEPTED_TENTATIVELY 0x55
        //#define DATUM_POW_SHARE_RESPONSE_REJECTED 0x66

        var shareResponse = new ShareResponseMessage
        {
            Status = 0x50, //(byte)(isHeaderValid && isValidCoinbase ? 0x50 : 0x66),
            ReasonCode = (ushort)(isHeaderValid && isValidCoinbase ? 0 : 1),
            Nonce = powSubmit.Nonce,
            TargetPot = powSubmit.TargetByte,
            JobId = powSubmit.JobId
        };

        var responsePayload = shareResponse.ToBytes();

        //Console.WriteLine($"📦 Share response payload: {BitConverter.ToString(responsePayload)}");
        //byte[] testPayload = new byte[] { 0x8F, 0x50, 0x00, 0x00, 0x32, 0x04, 0xCA, 0xC7, 0x0E, 0x02 };
        await SendEncryptedMessageAsync(0x05, responsePayload, isSigned: false, isEncryptedChannel: true, isEncryptedPubKey: false);
        //Console.WriteLine($"[SEND] Share Response [{(isHeaderValid && isValidCoinbase ? "ACCEPTED" : "REJECTED")}] (0x05, 0x{shareResponse.Status:X2})");
    }
    
    private static byte FloorPoT(ulong x)
    {
        if (x == 0) return 0;

        byte pos = 0;
        while (x > 1)          // keep shifting while x > 1
        {
            x >>= 1;           // x = x >> 1
            pos++;
        }
        return pos;
    }

    private ulong CalculateDifficulty(byte targetByte, ushort? targetByteIndex, byte[]? nBits)
    {
        if (nBits == null || targetByteIndex == null) return 0; // Minimal check
        // Simplified: Assume target_byte is difficulty exponent
        return 1UL << targetByte; // Adjust based on actual PoT logic
    }

    private bool VerifyBlockHeader(int version, uint nTime, uint nonce, byte[]? prevBlockHash, byte[]? merkleRoot, byte[]? nBits)
    {
        // Debug: Log input parameters
        //Console.WriteLine($"VerifyBlockHeader Inputs:");
        //Console.WriteLine($"  Version: 0x{version:X8}");
        //Console.WriteLine($"  PrevBlockHash: {(prevBlockHash != null ? BitConverter.ToString(prevBlockHash).Replace("-", "") : "null")}");
        //Console.WriteLine($"  MerkleRoot: {(merkleRoot != null ? BitConverter.ToString(merkleRoot).Replace("-", "") : "null")}");
        //Console.WriteLine($"  nTime: 0x{nTime:X8} ({nTime})");
        //Console.WriteLine($"  nBits: {(nBits != null ? BitConverter.ToString(nBits).Replace("-", "") : "null")}");
        //Console.WriteLine($"  Nonce: 0x{nonce:X8}");

        // Check for null inputs
        if (prevBlockHash == null || merkleRoot == null || nBits == null)
        {
            Console.WriteLine("  Result: False (null input detected)");
            return false;
        }

        // Validate input lengths
        if (prevBlockHash.Length != 32 || merkleRoot.Length != 32 || nBits.Length != 4)
        {
            Console.WriteLine($"  Result: False (invalid lengths - PrevBlockHash: {prevBlockHash.Length}, MerkleRoot: {merkleRoot.Length}, nBits: {nBits.Length})");
            return false;
        }

        // Reconstruct block header
        byte[] header = new byte[80];
        using (var stream = new MemoryStream(header))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(version); // 4 bytes, little-endian
            writer.Write(prevBlockHash); // 32 bytes
            writer.Write(merkleRoot); // 32 bytes
            writer.Write(nTime); // 4 bytes, little-endian
            writer.Write(nBits); // 4 bytes
            writer.Write(nonce); // 4 bytes, little-endian
        }

        // Debug: Log constructed header
        //Console.WriteLine($"  Constructed Header: {BitConverter.ToString(header).Replace("-", "")}");

        // Compute double SHA256 hash
        byte[] hash = DoubleSha256(header);
        //Console.WriteLine($"  Block Hash: {BitConverter.ToString(hash).Replace("-", "")}");

        // Compute target from nBits
        //byte[] target = ComputeTargetFromNBits(nBits);  //This version is the target for a new block
        //Console.WriteLine($"  Target: {BitConverter.ToString(target).Replace("-", "")}");

        // Compare hash to target (Bitcoin: hash <= target in big-endian)
        bool isValid = true; //CompareHashToTarget(hash, target);
        //Console.WriteLine($"  Difficulty Check: Hash <= Target? {isValid}");

        // Debug: Log result
        //Console.WriteLine($"  Result: {isValid}");
        return isValid;
    }

    private byte[] ComputeTargetFromNBits(byte[] nBits)
    {
        if (nBits.Length != 4) throw new ArgumentException("nBits must be 4 bytes");
        uint nBitsValue = BitConverter.ToUInt32(nBits, 0);
        int exponent = (int)(nBitsValue >> 24); // First byte is exponent
        uint mantissa = nBitsValue & 0xFFFFFF; // Last 3 bytes are mantissa
        if (exponent < 3) exponent = 3; // Minimum size to avoid overflow

        // Target = mantissa * 2^(8*(exponent - 3))
        byte[] target = new byte[32];
        byte[] mantissaBytes = BitConverter.GetBytes(mantissa);
        if (BitConverter.IsLittleEndian) Array.Reverse(mantissaBytes); // Convert to big-endian
        int shift = 8 * (exponent - 3);
        int mantissaLength = mantissaBytes.TakeWhile(b => b != 0).Count() + 1; // Non-zero bytes + 1
        for (int i = 0; i < mantissaLength && i < 4; i++)
        {
            if (32 - mantissaLength + i >= 0 && 32 - mantissaLength + i < 32)
                target[32 - mantissaLength + i] = mantissaBytes[i];
        }

        Console.WriteLine($"  ComputeTargetFromNBits: nBits=0x{nBitsValue:X8}, Exponent={exponent}, Mantissa=0x{mantissa:X6}, Target={BitConverter.ToString(target).Replace("-", "")}");
        return target;
    }

    private bool CompareHashToTarget(byte[] hash, byte[] target)
    {
        // Bitcoin compares hash <= target in big-endian
        for (int i = 0; i < 32; i++)
        {
            if (hash[i] < target[i]) return true;
            if (hash[i] > target[i]) return false;
        }
        return true; // Equal
    }

    private byte[] ComputeMerkleRoot(byte[] coinbaseHash, byte[] merkleBranches, byte count)
    {
        // The coinbaseHash is already BIG-ENDIAN from DoubleSha256.
        byte[] current_BE = coinbaseHash; 

        for (int i = 0; i < count; i++)
        {
            // 1. Get the branch from the list (it is LITTLE-ENDIAN)
            byte[] branch_LE = merkleBranches.Skip(i * 32).Take(32).ToArray(); 
            
            // 2. Reverse it to BIG-ENDIAN for hashing
            byte[] branch_BE = branch_LE.Reverse().ToArray();
            
            // 3. Concatenate (BIG-ENDIAN + BIG-ENDIAN)
            byte[] combined = current_BE.Concat(branch_BE).ToArray();
            
            // 4. Hash it. The output is BIG-ENDIAN, ready for the next loop.
            current_BE = DoubleSha256(combined); 
        }
        
        // Return the final BIG-ENDIAN Merkle Root
        return current_BE; 
    }

    private (bool isValid, List<(string Address, ulong Amount)> outputs) VerifyCoinbaseTransaction(byte[]? coinb1, byte[]? coinb2, ulong? coinbaseValue)
    {
        if (coinb2 == null || coinbaseValue == null)
            return (false, new List<(string, ulong)>());

        var outputs = new List<(string Address, ulong Amount)>();
        try
        {
            using var stream = new MemoryStream(coinb2);
            using var reader = new BinaryReader(stream);
            byte outputCount = reader.ReadByte(); // varint for output count
            if (outputCount > 100) return (false, outputs); // Sanity check

            ulong totalAmount = 0;
            for (int i = 0; i < outputCount; i++)
            {
                if (stream.Position >= stream.Length) return (false, outputs);
                ulong amount = reader.ReadUInt64(); // 8-byte amount
                totalAmount += amount;
                ulong scriptLen = ReadVarInt(reader);
                if (scriptLen > 100 || stream.Position + (long)scriptLen > stream.Length) return (false, outputs);
                byte[] script = reader.ReadBytes((int)scriptLen);
                string address = ScriptToAddress(script);
                outputs.Add((address, amount));
            }

            // Verify total amount does not exceed coinbase value
            bool isValid = totalAmount <= coinbaseValue.Value;
            return (isValid, outputs);
        }
        catch
        {
            return (false, outputs);
        }
    }

    private ulong ReadVarInt(BinaryReader reader)
    {
        byte b = reader.ReadByte();
        if (b < 0xFD) return b;
        if (b == 0xFD) return reader.ReadUInt16();
        if (b == 0xFE) return reader.ReadUInt32();
        return reader.ReadUInt64();
    }

    private string ScriptToAddress(byte[] script)
    {
        // Simplified: Handle P2PKH and P2WPKH
        if (script.Length == 25 && script[0] == 0x76 && script[1] == 0xA9 && script[2] == 0x14)
        {
            byte[] hash = script.Skip(3).Take(20).ToArray();
            byte[] payload = new byte[25];
            payload[0] = 0x00; // Mainnet P2PKH
            Array.Copy(hash, 0, payload, 1, 20);
            byte[] checksum = DoubleSha256(payload.Take(21).ToArray()).Take(4).ToArray();
            Array.Copy(checksum, 0, payload, 21, 4);
            return Base58Check.Encode(payload);
        }
        if (script.Length == 22 && script[0] == 0x00 && script[1] == 0x14)
        {
            byte[] program = script.Skip(2).Take(20).ToArray();
            return Bech32.Encode("bc", 0, program);
        }
        return "UNKNOWN";
    }

    private static byte[] DoubleSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hash1 = sha256.ComputeHash(data);
        return sha256.ComputeHash(hash1);
    }

    public static string FormatDifficulty(double difficulty)
    {
        if (difficulty < 1000) return difficulty.ToString("F2"); // Less than 1k, show as is
        if (difficulty < 1000000) return (difficulty / 1000).ToString("F2") + "k"; // Thousands
        if (difficulty < 1000000000) return (difficulty / 1000000).ToString("F2") + "M"; // Millions
        if (difficulty < 1000000000000) return (difficulty / 1000000000).ToString("F2") + "G"; // Billions
        if (difficulty < 1000000000000000) return (difficulty / 1000000000000).ToString("F2") + "T"; // Trillions
        if (difficulty < 1000000000000000000) return (difficulty / 1000000000000000).ToString("F2") + "P"; // Quadrillions
        return (difficulty / 1000000000000000000).ToString("F2") + "E"; // Quintillions
    }

    /// Generic helper to encrypt and send a message using the client's public or (more likely) using the shared channel secret.
    private async Task SendEncryptedMessageAsync(byte protoCmd, byte[] payload, bool isSigned = false, bool isEncryptedChannel = true, bool isEncryptedPubKey = false)
    {
        //Span<byte> finalMessageBody = new Span<byte>();
        byte[] finalMessageBody = new byte[payload.Length + 48]; //Try +48 I guess?  + LibSodium.CryptoBox.MacLen wasn't enough
        //Console.WriteLine("  Payload length: " + payload.Length);
        //Console.WriteLine("finalMessageBody: " + finalMessageBody.Length);
        //Console.WriteLine("Mac Bytes Length: " + LibSodium.CryptoBox.MacLen);

        if (isEncryptedPubKey) //encrypt using the client's public key, usually for the 0x02 handshake response message
        {
            if (_clientSessionPubKey == null) throw new InvalidOperationException("Cannot send sealed message without client session public key.");
            var clientPubKey = _clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey); // Client’s session X25519 public key
            finalMessageBody = LibSodium.CryptoBox.EncryptWithPublicKey(finalMessageBody, payload, clientPubKey).ToArray();
        }
        else if (isEncryptedChannel)  // Symmetric encryption with shared secret for other messages
        {            
            if (_channelSharedSecret == null) throw new InvalidOperationException("Cannot send encrypted message without a shared secret.");
            if (_sessionNonceSender == null) throw new InvalidOperationException("Cannot send encrypted message without a sender nonce.");
            finalMessageBody = LibSodium.CryptoBox.EncryptWithSharedKey(finalMessageBody, payload, _channelSharedSecretBytes, null, _sessionNonceSender).ToArray();
            //Console.WriteLine(" after encryption finalMessageBody.length: " + finalMessageBody.Length);
            //Console.WriteLine($"📦 Nonce: {BitConverter.ToString(_sessionNonceSender)}");
            //Console.WriteLine($"📦 Shared Key: {BitConverter.ToString(sharedKey)}");
            //Console.WriteLine($"📦 Ciphertext (channel): {BitConverter.ToString(finalMessageBody, 0, finalMessageBody.Length)}...");
        }
        else
        {
            // Unencrypted message (not typical, but handle for completeness)
            finalMessageBody = payload;
            Console.WriteLine($"📦 Plaintext payload: {BitConverter.ToString(payload, 0, Math.Min(payload.Length, 64))}...");
        }

        // Construct header
        var header = new DatumHeader
        {
            CmdLen = (uint)finalMessageBody.Length,
            IsEncryptedChannel = isEncryptedChannel,
            IsEncryptedPubKey = isEncryptedPubKey,
            IsSigned = isSigned,
            ProtoCmd = protoCmd
        };
        // Debug header fields
        //Console.WriteLine($"📋 Sending Header: Cmd=0x{protoCmd:X2}, Len={header.CmdLen}, Signed={header.IsSigned}, PubKey={header.IsEncryptedPubKey}, Channel={header.IsEncryptedChannel}");
        //Console.WriteLine($"🔑 current Session nonce sender: {Convert.ToBase64String(_sessionNonceSender)}");
        //Console.WriteLine($"🔑 current Session nonce receiver: {Convert.ToBase64String(_sessionNonceReceiver)}");
        // XOR header
        //Console.WriteLine($"📦 Plaintext header: {BitConverter.ToString(header.ToBytes())}");
        uint headerValue = BitConverter.ToUInt32(header.ToBytes(), 0);
        headerValue ^= _sendingHeaderKey;
        var xoredHeaderBytes = BitConverter.GetBytes(headerValue);
        //Console.WriteLine($"📦 XORed header: {BitConverter.ToString(xoredHeaderBytes)}");
        _sendingHeaderKey = DatumHeaderXorFeedback(_sendingHeaderKey);  //Increment the sending header for next time

        // Send header and body together
        var message = xoredHeaderBytes.Concat(finalMessageBody.ToArray()).ToArray();
        await _stream.WriteAsync(message, 0, message.Length);
        await _stream.FlushAsync();

        // Increment nonce only for channel encryption
        // TODO NEXT: Do I need to increment both recieve and send nonce's?
        if (isEncryptedChannel && _sessionNonceSender != null)
        {
            _sessionNonceSender = IncrementNonce(_sessionNonceSender);
        }
    }

    private uint DatumHeaderXorFeedback(uint i)
    {
        uint s = 0xb10cfeed;
        uint h = s;
        uint k = i;
        k *= 0xcc9e2d51;
        k = (k << 15) | (k >> 17);
        k *= 0x1b873593;
        h ^= k;
        h = (h << 13) | (h >> 19);
        h = h * 5 + 0xe6546b64;
        h ^= 4;
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        return h;
    }
}


// =================================================================================
// 4. DATUM PROTOCOL MESSAGE CLASSES
// =================================================================================
// These classes represent the data structures of the DATUM protocol. They contain
// methods for serializing to a byte array ('ToBytes') and deserializing from a
// byte array ('FromBytes'), mimicking the C structs from the reference client implementation.
// =================================================================================

/// Represents the 4-byte header at the start of every DATUM message.
/// Provides methods to pack/unpack the bitfields into a uint32.
public class DatumHeader
{
    public uint CmdLen { get; set; }              // 22 bits
    public bool IsSigned { get; set; }            // 1 bit
    public bool IsEncryptedPubKey { get; set; }   // 1 bit
    public bool IsEncryptedChannel { get; set; }  // 1 bit
    public byte ProtoCmd { get; set; }            // 5 bits

    public byte[] ToBytes()
    {
        uint val = 0;
        val |= (CmdLen & 0x3FFFFF);
        val |= (IsSigned ? 1u : 0u) << 24;
        val |= (IsEncryptedPubKey ? 1u : 0u) << 25;
        val |= (IsEncryptedChannel ? 1u : 0u) << 26;
        val |= ((uint)ProtoCmd & 0x1F) << 27;

        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, val);
        return buffer;
    }

    public static DatumHeader FromBytes(byte[] buffer)
    {
        uint val = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        return new DatumHeader
        {
            CmdLen = val & 0x3FFFFF,
            IsSigned = ((val >> 24) & 1) == 1,
            IsEncryptedPubKey = ((val >> 25) & 1) == 1,
            IsEncryptedChannel = ((val >> 26) & 1) == 1,
            ProtoCmd = (byte)((val >> 27) & 0x1F)
        };
    }
}

// CLIENT: Hello message (0x01)
public class HelloMessage
{
    public byte[] ClientSigningPubKey { get; set; } = new byte[32]; // Ed25519 public key
    public byte[] ClientEncryptPubKey { get; set; } = new byte[32]; // X25519 public key
    public byte[] ClientSessionSigningPubKey { get; set; } = new byte[32]; // Ed25519 public key
    public byte[] ClientSessionEncryptPubKey { get; set; } = new byte[32]; // X25519 public key
    public byte[]? version; // Variable length string ending with '/', max 127 bytes
    public byte[]? commitHash; // Variable length null-terminated string, max 127 bytes
    public byte[]? xorKey; // Exactly 4-byte key
    public byte[]? cryptoSignBytes; // Exactly 64 bytes (signature)
    public byte[]? cryptoBoxSealBytes; // Placeholder, assuming padding or ignored

    public static (HelloMessage? Message, int BytesConsumed) FromBytes(byte[] data)
    {
        try
        {
            const int cryptoSignBytes = 64; // CRYPTO_SIGN_BYTES  //TODO: this should just reference the LibSodium library contstants instead of hardcoded
            const int maxStringLength = 127; // Max length for version and commit hash
            const int publicKeyLength = 32; // Length of each public key
            const int xorKeyLength = 4; // Length of nk

            // Validate minimum length: public keys (128) + version (1) + '/' (1) + commit (1) + null (1) + 0xFE (1) + nk (4) + signature (64)
            const int minLength = 128 + 1 + 1 + 1 + 1 + 1 + 4 + cryptoSignBytes;
            if (data.Length < minLength)
            {
                Console.WriteLine($"❌ Hello message too short ({data.Length} bytes, expected at least {minLength})");
                return (null, -1);
            }

            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);

            var msg = new HelloMessage();

            // Step 1: Read public keys (128 bytes total)
            reader.Read(msg.ClientSigningPubKey, 0, publicKeyLength);
            reader.Read(msg.ClientEncryptPubKey, 0, publicKeyLength);
            reader.Read(msg.ClientSessionSigningPubKey, 0, publicKeyLength);
            reader.Read(msg.ClientSessionEncryptPubKey, 0, publicKeyLength);

            // Step 2: Read version string (ends with '/', max 127 bytes)
            long versionStart = stream.Position;
            byte[] versionBuffer = new byte[maxStringLength + 1]; // +1 for '/'
            int versionIndex = 0;
            while (versionIndex < maxStringLength)
            {
                if (stream.Position >= data.Length)
                {
                    Console.WriteLine($"❌ No '/' separator found in version");
                    return (null, -1);
                }
                byte b = reader.ReadByte();
                versionBuffer[versionIndex++] = b;
                if (b == (byte)'/') break;
                if (b == 0)
                {
                    Console.WriteLine($"❌ Unexpected null in version at offset {stream.Position - 1}");
                    return (null, -1);
                }
            }
            if (versionIndex == maxStringLength && versionBuffer[versionIndex - 1] != (byte)'/')
            {
                Console.WriteLine($"❌ Version string too long or missing '/'");
                return (null, -1);
            }
            msg.version = new byte[versionIndex];
            Array.Copy(versionBuffer, msg.version, versionIndex);
            Console.WriteLine($"🔓 Version: {Encoding.ASCII.GetString(msg.version)}");

            // Step 3: Read commit hash (null-terminated, max 127 bytes)
            long commitStart = stream.Position;
            byte[] commitBuffer = new byte[maxStringLength + 1]; // +1 for null
            int commitIndex = 0;
            while (commitIndex < maxStringLength)
            {
                if (stream.Position >= data.Length)
                {
                    Console.WriteLine($"❌ No null terminator for commit hash");
                    return (null, -1);
                }
                byte b = reader.ReadByte();
                commitBuffer[commitIndex++] = b;
                if (b == 0) break;
            }
            if (commitIndex == maxStringLength && commitBuffer[commitIndex - 1] != 0)
            {
                Console.WriteLine($"❌ Commit hash too long or missing null");
                return (null, -1);
            }
            msg.commitHash = new byte[commitIndex];
            Array.Copy(commitBuffer, msg.commitHash, commitIndex);
            Console.WriteLine($"🔓 Commit hash: {Encoding.ASCII.GetString(msg.commitHash, 0, commitIndex - 1)}");

            // Step 4: Handle optional git tag (if present, wrapped in '()')
            long pos = stream.Position;
            if (pos < data.Length && reader.PeekChar() == '(')
            {
                reader.ReadByte(); // Skip '('
                long tagStart = stream.Position;
                byte[] tagBuffer = new byte[maxStringLength + 1];
                int tagIndex = 0;
                while (tagIndex < maxStringLength)
                {
                    if (stream.Position >= data.Length)
                    {
                        Console.WriteLine($"❌ No null terminator for git tag");
                        return (null, -1);
                    }
                    byte b = reader.ReadByte();
                    tagBuffer[tagIndex++] = b;
                    if (b == 0) break;
                }
                if (tagIndex == maxStringLength && tagBuffer[tagIndex - 1] != 0)
                {
                    Console.WriteLine($"❌ Git tag too long or missing null");
                    return (null, -1);
                }
                if (stream.Position >= data.Length || reader.ReadByte() != ')')
                {
                    Console.WriteLine($"❌ Expected ')' after git tag at offset {stream.Position}");
                    return (null, -1);
                }
                msg.commitHash = new byte[tagIndex + commitIndex + 2]; // Include '(' and ')'
                msg.commitHash[0] = (byte)'(';
                Array.Copy(tagBuffer, 0, msg.commitHash, 1, tagIndex);
                msg.commitHash[tagIndex + 1] = (byte)')';
                Array.Copy(commitBuffer, 0, msg.commitHash, tagIndex + 2, commitIndex);
                Console.WriteLine($"🔓 Git tag: {Encoding.ASCII.GetString(tagBuffer, 0, tagIndex - 1)}");
            }
            else
            {
                // No git tag, use commit hash as is
                msg.commitHash = commitBuffer.Take(commitIndex).ToArray();
            }

            // Step 5: Check null terminator
            //if (stream.Position >= data.Length || reader.ReadByte() != 0)
            //{
            //    Console.WriteLine($"❌ Expected null at offset {stream.Position - 1}, found {(stream.Position <= data.Length ? data[stream.Position - 1].ToString("X2") : "EOF")}");
            //    return (null, -1);
            //}

            // Step 6: Check 0xFE marker
            if (stream.Position >= data.Length || reader.ReadByte() != 0xFE)
            {
                Console.WriteLine($"❌ Expected 0xFE at offset {stream.Position - 1}, found {(stream.Position <= data.Length ? data[stream.Position - 1].ToString("X2") : "EOF")}");
                return (null, -1);
            }

            // Step 7: Read XOR key (4 bytes)
            msg.xorKey = new byte[xorKeyLength];
            if (stream.Position + xorKeyLength > data.Length)
            {
                Console.WriteLine($"❌ Insufficient bytes for XOR key at offset {stream.Position}");
                return (null, -1);
            }
            reader.Read(msg.xorKey, 0, xorKeyLength);
            uint nk = BitConverter.ToUInt32(msg.xorKey, 0);
            Console.WriteLine($"🔓 XOR key (nk): 0x{nk:X8} at offset {stream.Position - xorKeyLength}");

            // Step 8: Skip padding (variable, 1–200 bytes)
            long paddingStart = stream.Position;
            int paddingLength = 0;
            while (stream.Position < data.Length - cryptoSignBytes)
            {
                reader.ReadByte();
                paddingLength++;
                if (paddingLength > 200)
                {
                    Console.WriteLine($"❌ Padding too long (>200 bytes) at offset {stream.Position}");
                    return (null, -1);
                }
            }
            if (paddingLength < 1)
            {
                Console.WriteLine($"❌ Padding too short (<1 byte) at offset {paddingStart}");
                return (null, -1);
            }
            Console.WriteLine($"🔓 Padding length: {paddingLength} bytes");

            // Step 9: Read signature (64 bytes)
            msg.cryptoSignBytes = new byte[cryptoSignBytes];
            if (stream.Position + cryptoSignBytes > data.Length)
            {
                Console.WriteLine($"❌ Insufficient bytes for signature at offset {stream.Position}");
                return (null, -1);
            }
            reader.Read(msg.cryptoSignBytes, 0, cryptoSignBytes);
            Console.WriteLine($"🔓 Signature: {BitConverter.ToString(msg.cryptoSignBytes, 0, 16)}...");

            // Step 10: Handle cryptoBoxSealBytes (assuming placeholder or padding)
            msg.cryptoBoxSealBytes = new byte[0]; // Ignore for now, adjust if needed
            Console.WriteLine($"🔓 Note: cryptoBoxSealBytes set to empty (adjust if needed)");

            // Return populated message and bytes consumed
            int bytesConsumed = (int)stream.Position;
            Console.WriteLine($"🔓 Hello message parsed successfully, consumed {bytesConsumed} bytes");
            return (msg, bytesConsumed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error parsing hello message: {ex.Message}");
            return (null, -1);
        }
    }
}
// SERVER: Handshake Response message (0x02)
public class HandshakeResponseMessage
{
    public byte[] ClientSigningPubKey { get; set; } = new byte[32];
    public byte[] ClientEncryptPubKey { get; set; } = new byte[32];
    public byte[] ClientSessionSigningPubKey { get; set; } = new byte[32];
    public byte[] ClientSessionEncryptPubKey { get; set; } = new byte[32];
    public byte[] ServerSessionSigningPubKey { get; set; } = new byte[32];
    public byte[] ServerSessionEncryptPubKey { get; set; } = new byte[32];
    public string MessageOfTheDay { get; set; } = "Hello...Neo.";
    
    // Helper to write a null-terminated string.  Seems silly, but whatever. It works.
    private void WriteNullTerminatedString(BinaryWriter writer, string s)
    {
        writer.Write(Encoding.UTF8.GetBytes(s));
        writer.Write((byte)0);
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(ClientSigningPubKey);
        writer.Write(ClientEncryptPubKey);
        writer.Write(ClientSessionSigningPubKey);
        writer.Write(ClientSessionEncryptPubKey);
        writer.Write(ServerSessionSigningPubKey);
        writer.Write(ServerSessionEncryptPubKey);
        WriteNullTerminatedString(writer, MessageOfTheDay);
        
        return stream.ToArray();
    }
}

// CLIENT: Coinbaser Fetch message (0x05, 0x10)
public class CoinbaserFetchMessage
{
    public ulong RewardValue { get; set; }

    public static CoinbaserFetchMessage FromBytes(byte[] data)
    {
        // The payload only contains the reward value.
        return new CoinbaserFetchMessage
        {
            RewardValue = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(0, 8))
        };
    }
}

// SERVER: Coinbaser Fetch Response (0x05, 0x11)
public class CoinbaserFetchResponseMessage
{
    public List<PayoutInfo> Payouts { get; set; } = new();

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)Payouts.Count); // 1 byte
        foreach (var payout in Payouts)
        {
            writer.Write(payout.Value); // 8 bytes
            byte[] script;
            if (payout.Address.StartsWith("bc1") || payout.Address.StartsWith("tb1"))
            {
                // Bech32 (SegWit) address
                var (hrp, version, program) = Bech32.Decode(payout.Address);
                if (version != 0 || program.Length != 20) // P2WPKH only
                {
                    throw new InvalidOperationException($"Unsupported Bech32 address: {payout.Address}");
                }
                script = new byte[2 + program.Length];
                script[0] = 0x00; // Witness version 0
                script[1] = (byte)program.Length; // Length (20)
                Array.Copy(program, 0, script, 2, program.Length);
            }
            else
            {
                // P2PKH address
                byte[] payload = Base58Check.Decode(payout.Address);
                if (payload.Length != 21 || payload[0] != 0x00)
                {
                    throw new InvalidOperationException($"Invalid P2PKH address: {payout.Address}");
                }
                byte[] pubkeyHash = payload.Skip(1).Take(20).ToArray();
                script = new byte[25];
                script[0] = 0x76; // OP_DUP
                script[1] = 0xA9; // OP_HASH160
                script[2] = 0x14; // Length of hash (20)
                Array.Copy(pubkeyHash, 0, script, 3, 20);
                script[23] = 0x88; // OP_EQUALVERIFY
                script[24] = 0xAC; // OP_CHECKSIG
            }
            writer.Write((byte)script.Length); // 1 byte script length
            writer.Write(script); // Script bytes
        }
        return stream.ToArray();
    }
}
public class PayoutInfo
{
    public ulong Value { get; set; }
    public string Address { get; set; } = string.Empty;
}

// CLIENT: PoW Submit message (0x05, 0x27)
public class PowSubmitMessage
{
    public byte JobId { get; set; }
    public byte CoinbaseId { get; set; }
    public bool IsBlock { get; set; }
    public bool SubsidyOnly { get; set; }
    public bool QuickDiff { get; set; }
    public byte TargetByte { get; set; }
    public uint NTime { get; set; }
    public uint Nonce { get; set; }
    public int Version { get; set; }
    public byte ExtranonceSize { get; set; }
    public byte[] Extranonce { get; set; } = new byte[12];
    public string Username { get; set; } = string.Empty;
    public byte[] Reserved { get; set; } = new byte[4];
    public byte[]? PrevBlockHash { get; set; }
    public ushort? TargetByteIndex { get; set; }
    public byte[]? NBits { get; set; }
    public byte? CoinbaserId { get; set; }
    public uint? Height { get; set; }
    public ulong? CoinbaseValue { get; set; }
    public uint? TransactionCount { get; set; }
    public uint? TotalWeight { get; set; }
    public uint? TotalSize { get; set; }
    public uint? TotalSigops { get; set; }
    public byte? MerkleBranchCount { get; set; }
    public byte[]? MerkleBranches { get; set; }
    
    //public byte[,]? Coinb1 { get; set; }
    //public byte[,]? Coinb2 { get; set; }
    public (byte[] Coinb1, byte[] Coinb2)[] CoinbasePairs { get; set; } = new (byte[], byte[])[8];
    public byte[]? SubsidyOnlyCoinb1 { get; set; }
    public byte[]? SubsidyOnlyCoinb2 { get; set; }
    public byte[]? MerkleRoot { get; set; } // Added

    public static PowSubmitMessage FromBytes(byte[] data)
    {
        if (data.Length < 30) throw new ArgumentException("Invalid PoW submission length");
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        var result = new PowSubmitMessage
        {
            JobId = reader.ReadByte(), // offset 1
            CoinbaseId = reader.ReadByte(), // offset 2
        };
        byte flags = reader.ReadByte(); // offset 3
        result.IsBlock = (flags & 0x01) != 0;
        result.SubsidyOnly = (flags & 0x02) != 0;
        result.QuickDiff = (flags & 0x04) != 0;
        result.TargetByte = reader.ReadByte(); // offset 4
        result.NTime = reader.ReadUInt32(); // offset 5
        result.Nonce = reader.ReadUInt32(); // offset 9
        result.Version = reader.ReadInt32(); // offset 13
        result.ExtranonceSize = reader.ReadByte(); // offset 17
        if (result.ExtranonceSize != 12) throw new ArgumentException($"Unsupported extranonce size: {result.ExtranonceSize}");
        result.Extranonce = reader.ReadBytes(12); // offset 18
        var usernameBytes = new List<byte>();
        while (stream.Position < stream.Length)
        {
            byte b = reader.ReadByte();
            if (b == 0) break;
            usernameBytes.Add(b);
        }
        result.Username = Encoding.UTF8.GetString(usernameBytes.ToArray());
        result.Reserved = reader.ReadBytes(4); // offset 30 + username.Length

        // Process optional sections (0x01, 0x02) until 0xFE
        bool hasMerkleData = false;
        bool hasCoinbaseData = false;
        while (stream.Position < stream.Length)
        {
            byte flag = reader.ReadByte();
            if (flag == 0xFE) break; // Terminator
            if (flag == 0x01) // Merkle branches
            {
                hasMerkleData = true;
                result.PrevBlockHash = reader.ReadBytes(32);
                result.TargetByteIndex = reader.ReadUInt16();
                result.NBits = reader.ReadBytes(4);
                result.CoinbaserId = reader.ReadByte();
                result.Height = reader.ReadUInt32();
                result.CoinbaseValue = reader.ReadUInt64();
                result.TransactionCount = reader.ReadUInt32();
                result.TotalWeight = reader.ReadUInt32();
                result.TotalSize = reader.ReadUInt32();
                result.TotalSigops = reader.ReadUInt32();
                result.MerkleBranchCount = reader.ReadByte();
                result.MerkleBranches = reader.ReadBytes((int)(result.MerkleBranchCount * 32));
            }
            else if (flag == 0x02) // Coinbase data
            {
                //TODO: Deal with subsidyOnly coinbases, which currently are not set.
                hasCoinbaseData = true;
                byte coinbaseType = reader.ReadByte();
                ushort coinb1Len = reader.ReadUInt16();
                ushort coinb2Len = reader.ReadUInt16();
                byte[] coinb1 = reader.ReadBytes(coinb1Len);
                byte[] coinb2 = reader.ReadBytes(coinb2Len);
                result.CoinbasePairs[result.CoinbaseId] = (coinb1, coinb2);
                Console.WriteLine($"Stored CoinbaseId {result.CoinbaseId}: Coinb1={coinb1Len} bytes, Coinb2={coinb2Len} bytes");
            }
            else
            {
                throw new ArgumentException($"Unknown flag: 0x{flag:X2}");
            }
        }
        if(hasCoinbaseData ^ hasMerkleData)
        {
            if (hasCoinbaseData) Console.WriteLine("*** Got coinbase without Merkle Data!!!");
            if (hasMerkleData) Console.WriteLine("*** Got Merkle Data without Coinbase data!!");
        }

        return result;
    }

    private static byte[] DoubleSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hash1 = sha256.ComputeHash(data);
        return sha256.ComputeHash(hash1);
    }

    private static byte[] ComputeMerkleRoot(byte[] coinbaseHash, byte[] merkleBranches, byte count)
    {
        byte[] current = coinbaseHash;
        for (int i = 0; i < count; i++)
        {
            byte[] branch = merkleBranches.Skip(i * 32).Take(32).ToArray();
            current = DoubleSha256(current.Concat(branch).ToArray());
        }
        return current;
    }
}

// SERVER: Share Response message (0x05, 0x8F)
// TODO: This looks incomplete.  I think.
public class ShareResponseMessage
{
    public byte Status { get; set; } // 0x50, 0x55, or 0x66
    public ushort ReasonCode { get; set; } // For rejected shares
    public uint Nonce { get; set; } // Share nonce
    public byte TargetPot { get; set; } // Difficulty exponent
    public byte JobId { get; set; } // Job ID

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0x8F);
        writer.Write(Status); // 1 byte
        writer.Write(ReasonCode); // 2 bytes, little-endian
        writer.Write(Nonce); // 4 bytes, little-endian
        writer.Write(TargetPot); // 1 byte
        writer.Write(JobId); // 1 byte
        return stream.ToArray();
    }
}

// Helper functions:
