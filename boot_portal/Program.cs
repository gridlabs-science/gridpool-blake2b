// Program.cs

using System.Buffers.Binary;
using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using LibSodium;
using System;
using System.IO;
using System.CommandLine.IO;

// =================================================================================
// 1. MAIN PROGRAM ENTRY POINT
// =================================================================================
// This class is responsible for parsing command-line arguments, managing the
// server's primary cryptographic key, and starting the TCP server.
// =================================================================================
// JSON configuration class for boot_portal_config.json

using NSec.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Security;

public static class CryptoUtils
{
    public static byte[] ComputeSharedSecretForCryptoBox(byte[] serverPrivateKey, byte[] clientPublicKey)
    {
        // Step 1: X25519 key agreement
        //byte[] rawSharedSecret = LibSodium.ScalarMult.Mult(serverPrivateKey, clientPublicKey);
        byte[] rawSharedSecret = new byte[LibSodium.CryptoBox.SharedKeyLen];
        LibSodium.CryptoBox.CalculateSharedKey(rawSharedSecret, clientPublicKey, serverPrivateKey);
        return rawSharedSecret;
        //if (rawSharedSecret == null || rawSharedSecret.Length != 32)
        {
        //    throw new InvalidOperationException("Failed to compute X25519 shared secret");
        }
        //return rawSharedSecret;
        // Step 2: hsalsa20 transformation (mimics crypto_box_beforenm)
        byte[] zeroNonce = new byte[16]; // crypto_core_hsalsa20_NONCEBYTES
        byte[] zeroKey = new byte[32];   // crypto_core_hsalsa20_KEYBYTES
        byte[] output = new byte[32];    // crypto_core_hsalsa20_OUTPUTBYTES
                                         //Sodium.CryptoCore.HSalsa20(output, zeroNonce, rawSharedSecret, zeroKey);
        //Sodium.StreamEncryption.Encrypt
        

        return output;
    }
}

public class PoolConfig
{
    [JsonPropertyName("pool_payout_script")]
    public string PoolPayoutScript { get; set; } = "mpuPt3FvAfwQFxd6BmPrwuRBbdMgmDSGfH";

    [JsonPropertyName("coinbase_tag")]
    public string CoinbaseTag { get; set; } = "Boot protocol";

    [JsonPropertyName("prime_id")]
    public uint PrimeId { get; set; } = 21;

    [JsonPropertyName("min_diff")]
    public ulong MinDiff { get; set; } = 1024;
}

public class ServerConfig
{
    [JsonPropertyName("ed25519_private_key")]
    public string? Ed25519PrivateKey { get; set; }

    [JsonPropertyName("x25519_private_key")]
    public string? X25519PrivateKey { get; set; }
}

public class Program
{
    private const int DatumPort = 3008;
    private const string ConfigFilePath = "boot_portal_config.json";

    //private PoolConfig? _poolConfig;

    // Utility function to convert bytes to hex string
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

            // Start the server
            var server = new DatumServer(IPAddress.Any, DatumPort, ed25519Key, x25519Key, _poolConfig);
            await server.StartAsync();
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
// 3. CLIENT HANDLER CLASS (REVISED WITH CORRECT NSEC API USAGE)
// =================================================================================
public class ClientHandler
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Key _ed25519LongTermKey; // The server's main Ed25519 key.
    //private readonly Key _ed25519KeyLongTerm; // The server's long-term Ed25519 key.
    private readonly Key _x25519KeyLongTerm; // The server's long-term x25519 key.

    // --- Per-Session State ---
    private PublicKey? _clientSessionPubKey;
    private Key? _serverSessionSigningKey; //ed25519
    private Key? _serverSessionEncryptKey; //x25519
    private SharedSecret? _channelSharedSecret; // The key for symmetric encryption
    private byte[]? _channelSharedSecretBytes;
    private byte[]? _sessionNonceSender; // Server’s send nonce (client’s receive nonce)
    private UInt32 _sendingHeaderKey;
    private UInt32 _receivingHeaderKey;
    private HelloMessage? _helloMessage;
    private PoolConfig _poolConfig;

    public ClientHandler(TcpClient client, Key serverLongTermKey, Key serverLongTermXKey, PoolConfig poolConfig)
    {
        _client = client;
        _stream = client.GetStream();
        _ed25519LongTermKey = serverLongTermKey;
        _sendingHeaderKey = 0xDC871829; // initial send header key ... changed by handshake function
        //_ed25519KeyLongTerm = serverLongTermEdKey;
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
                Console.WriteLine($"📥 Received header bytes: {BitConverter.ToString(headerBuffer)}");


                //Console.WriteLine($"🔑 Extracted XOR key: 0x{xorKey:X8}");

                // Step 1.2: Decode header with XOR key
                uint headerValue = BitConverter.ToUInt32(headerBuffer, 0); // Read as little-endian
                headerValue ^= _sendingHeaderKey; // XOR as 32-bit integer
                var deXoredHeaderBytes = BitConverter.GetBytes(headerValue); // Convert back to bytes
                Console.WriteLine($"📥 De-XORed header bytes: {BitConverter.ToString(deXoredHeaderBytes)}");

                // Parse header
                var header = DatumHeader.FromBytes(deXoredHeaderBytes);
                Console.WriteLine($"📋 Parsed header: Cmd={header.ProtoCmd}, Len={header.CmdLen}, Signed={header.IsSigned}, EncryptedPubKey={header.IsEncryptedPubKey}, EncryptedChannel={header.IsEncryptedChannel}");


                // Step 2: Read up to max encrypted body length (798 bytes)
                //const int maxBodyLength = 1024; // Max encrypted body length
                var bodyBuffer = new byte[header.CmdLen];
                bytesRead = await _stream.ReadAsync(bodyBuffer, 0, bodyBuffer.Length);
                if (bytesRead == 0)
                {
                    Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} disconnected (no body).");
                    break;
                }
                Console.WriteLine($"📦 Received encrypted body ({bytesRead} bytes)");

                //      We need to use a different decryption key depending on the header.protoCmmd
                // Step 3: Decrypt the body
                byte[]? decryptedBody;
                if (header.IsEncryptedPubKey) { decryptedBody = DecryptSigned(bodyBuffer, bytesRead); }
                else { decryptedBody = DecryptStandard(bodyBuffer, bytesRead); }

                if (decryptedBody == null)
                {
                    Console.WriteLine($"❌ Failed to decrypt body for client {_client.Client.RemoteEndPoint}");
                    break;
                }
                Console.WriteLine($"🔓 Decrypted body ({decryptedBody.Length} bytes)");



                // Verify cmd_len matches decrypted body length
                //TODO: change "48" to actually reference the libsodium constant instead.
                if (header.CmdLen != decryptedBody.Length+48)  //No longer modified with new LibSodium library  //Modified to account for CryptoBoxSealBytes, the signature that is added to the encrypted payload.
                {
                    Console.WriteLine($"⚠️ Header cmd_len ({header.CmdLen}) does not match decrypted body length ({decryptedBody.Length})");
                    break;
                }

                // Parse hello message
                // Step 4: Parse hello message
                
                // Process message
                Console.WriteLine($"[RECV] Command: 0x{header.ProtoCmd:X2}, Length: {header.CmdLen} bytes");
                switch (header.ProtoCmd)
                {
                    case 0x01: await HandleHelloAsync(header, decryptedBody); break;
                    case 0x05: await HandleMiningCommandAsync(header, decryptedBody); break;
                    default: Console.WriteLine($"⚠️ Received unknown command: 0x{header.ProtoCmd:X2}"); break;
                }
                //await HandleHelloAsync(header, body); break;
                //await ProcessMessageAsync(header, decryptedBody);

                // Step 5?: Extract XOR key (nk) from decrypted body
                //TODO: I probably have other operations I need to do to this before it's good to use
                //  see datum_protocol.c lines 1058 and 156
                //_sendingHeaderKey = ExtractXorKey(decryptedBody);
                //_receivingHeaderKey = ~_sendingHeaderKey; // Matches client's datum_header_xor_feedback(~nk)  

                // Step 4: Extract XOR key and update header keys
                /*uint nk = ExtractXorKey(decryptedBody);
                if (nk == 0)
                {
                    Console.WriteLine($"❌ Failed to extract XOR key for client {_client.Client.RemoteEndPoint}");
                    break;
                }
                _sendingHeaderKey = DatumHeaderXorFeedback(~nk); // Client's sending_header_key
                _receivingHeaderKey = DatumHeaderXorFeedback(nk); // Client's receiving_header_key
                Console.WriteLine($"🔑 Updated XOR keys: Sending=0x{_sendingHeaderKey:X8}, Receiving=0x{_receivingHeaderKey:X8}");              
                */
                //Now we can send it to the appropriate command handler:
                //await ProcessMessageAsync(header, decryptedBody);
            }
        }
        catch (IOException) { Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} disconnected."); }
        catch (Exception ex) { Console.WriteLine($"💥 An error occurred with client {_client.Client.RemoteEndPoint}: {ex.Message}\n{ex.StackTrace}"); }
        finally { _client.Close(); }
    }

    private byte[]? DecryptSigned(byte[] encryptedBody, int bytesRead)
    {
        //Console.WriteLine($"📦 Ciphertext first all bytes: {BitConverter.ToString(encryptedBody, 0, 32)}");
        //Console.WriteLine($"📦 Ciphertext first all bytes: {BitConverter.ToString(encryptedBody)}");
        try
        {
            const int CryptoBoxSealBytes = 48; // 48 (32 ephemeral PK + 16 Poly1305 tag)
            if (bytesRead < CryptoBoxSealBytes)
            {
                Console.WriteLine($"❌ Ciphertext too short: {bytesRead} bytes");
                return null;
            }

            // Use the X25519 key pair directly
            var privateKeyBytes = _x25519KeyLongTerm.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes
            var publicKeyBytes = _x25519KeyLongTerm.PublicKey.Export(KeyBlobFormat.RawPublicKey); // 32 bytes
            //var serverKeyPair = new LibSodium.(publicKeyBytes, privateKeyBytes);

            // Truncate input to actual length
            var cipherText = encryptedBody.AsSpan(0, bytesRead).ToArray();

            // Decrypt using crypto_box_seal_open
            //var decrypted = Sodium.SealedPublicKeyBox.Open(cipherText, serverKeyPair);
            //Span<byte> decrypted = new Span<byte>();
            byte[] decrypted = new byte[encryptedBody.Length - CryptoBoxSealBytes];
            LibSodium.CryptoBox.DecryptWithPrivateKey(decrypted, cipherText, privateKeyBytes);
            if (decrypted == null)
            {
                Console.WriteLine("❌ Decryption failed: Sodium.SealedPublicKeyBox.Open returned null");
                return null;
            }

            Console.WriteLine($"🔓 Decrypted {decrypted.Length} bytes");
            //Console.WriteLine($"-> {BitConverter.ToString(decrypted)}");
            Console.WriteLine($"🔓 Client signing public key:    {BitConverter.ToString(decrypted, 0, 16)}...");
            Console.WriteLine($"🔓 Client encryption public key: {BitConverter.ToString(decrypted, 32, 16)}...");
            int i = 128; // Skip public keys
            int j = i;
            while(decrypted[j] != '/') j++;
            string version = Encoding.ASCII.GetString(decrypted, i, j - i);
            i += version.Length; // Skip null
            j = i;
            while(decrypted[j] != (byte)0) j++;
            string commitHash = Encoding.ASCII.GetString(decrypted, i, j - i);
            Console.WriteLine($"🔓 Version: {version}, Commit Hash: {commitHash}");
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
        return null;
    }

    /*private uint ExtractXorKey(byte[] decryptedBody)
    {
        try
        {
            // Minimum length: public keys (128) + version (1) + '/' (1) + commit (1) + null (1) + 0xFE (1) + nk (4)
            const int minLength = 128 + 1 + 1 + 1 + 1 + 1 + 4; // 137 bytes
            if (decryptedBody.Length < minLength)
            {
                Console.WriteLine($"❌ Decrypted body too short ({decryptedBody.Length} bytes)");
                return 0;
            }

            // Step 1: Skip public keys (fixed 128 bytes)
            int i = 128;
            Console.WriteLine($"🔓 Message at offset 128: {BitConverter.ToString(decryptedBody, i, 16)}...");

            // Step 2: Find '/' separator (version + commit hash)
            int versionStart = i;
            while (i < decryptedBody.Length && decryptedBody[i] != '/')
            {
                if (decryptedBody[i] == 0)
                {
                    Console.WriteLine($"❌ Unexpected null in version at offset {i}");
                    return 0;
                }
                i++;
            }
            if (i >= decryptedBody.Length)
            {
                Console.WriteLine($"❌ No '/' separator found");
                return 0;
            }
            string version = Encoding.ASCII.GetString(decryptedBody, versionStart, i - versionStart);
            Console.WriteLine($"🔓 Version: {version}");
            i++; // '/'

            // Step 3: Skip commit hash until null
            int commitStart = i;
            while (i < decryptedBody.Length && decryptedBody[i] != 0) i++;
            if (i >= decryptedBody.Length)
            {
                Console.WriteLine($"❌ No null terminator for commit hash");
                return 0;
            }
            string commitHash = Encoding.ASCII.GetString(decryptedBody, commitStart, i - commitStart);
            Console.WriteLine($"🔓 Commit hash: {commitHash}");
            i++; // Null

            // Step 4: Handle optional git tag
            if (i < decryptedBody.Length && decryptedBody[i] == '(')
            {
                i++; // '('
                int tagStart = i;
                while (i < decryptedBody.Length && decryptedBody[i] != 0) i++;
                if (i >= decryptedBody.Length)
                {
                    Console.WriteLine($"❌ No null terminator for git tag");
                    return 0;
                }
                string gitTag = Encoding.ASCII.GetString(decryptedBody, tagStart, i - tagStart);
                Console.WriteLine($"🔓 Git tag: {gitTag}");
                i++; // Null
                if (i >= decryptedBody.Length || decryptedBody[i] != ')')
                {
                    Console.WriteLine($"❌ Expected ')' at offset {i}");
                    return 0;
                }
                i++; // ')'
            }

            // Step 5: Check final null
            //if (i >= decryptedBody.Length || decryptedBody[i] != 0)
            //{
            //    Console.WriteLine($"❌ Expected null at offset {i}, found {(i < decryptedBody.Length ? decryptedBody[i].ToString("X2") : "EOF")}");
            //    return 0;
            //}
            //i++; // Null

            // Step 6: Check 0xFE marker
            if (i >= decryptedBody.Length || decryptedBody[i] != 0xFE)
            {
                Console.WriteLine($"❌ Expected 0xFE at offset {i}, found {(i < decryptedBody.Length ? decryptedBody[i].ToString("X2") : "EOF")}");
                return 0;
            }
            i++; // 0xFE

            // Step 7: Extract nk
            if (i + 4 > decryptedBody.Length)
            {
                Console.WriteLine($"❌ Insufficient bytes for nk at offset {i}");
                return 0;
            }
            uint nk = BitConverter.ToUInt32(decryptedBody, i);
            Console.WriteLine($"🔓 XOR key (nk): 0x{nk:X8} at offset {i}");
            return nk;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error extracting XOR key: {ex.Message}");
            return 0;
        }
    }*/
    private UInt32 ExtractXorKey(byte[] decryptedBody)
    {
        // Find 0xFE marker after version, '/', commit hash, and optional tag
        int i = 0;
        // Skip public keys (128 bytes)
        /*i += 128;
        Console.WriteLine(" test start");


        // Find first null terminator (end of version)
        while (i < decryptedBody.Length && decryptedBody[i] != '/')
        {
            i++;
            Console.Write(Encoding.ASCII.GetString(decryptedBody, i, 1));
        } 
        if (i >= decryptedBody.Length) return 0;
        i++; // Skip null
        Console.WriteLine(" test 0");


        // Skip '/' separator
        if (i >= decryptedBody.Length || decryptedBody[i] != '\0') return 0;
        i++;
        Console.WriteLine(" test 1");
        

        // Skip commit hash
        while (i < decryptedBody.Length && decryptedBody[i] != '\0') i++;
        if (i >= decryptedBody.Length) return 0;
        i++; // Skip null
        Console.WriteLine(" test 2");

        // Check for optional tag
        if (i < decryptedBody.Length && decryptedBody[i] == '(')
        {
            i++;
            while (i < decryptedBody.Length && decryptedBody[i] != 0) i++;
            if (i >= decryptedBody.Length) return 0;
            i++; // Skip null
            if (i < decryptedBody.Length && decryptedBody[i] == ')') i++;
            Console.WriteLine(" test 3");
        }
*/
        i = 128; // Skip public keys
        int j = i;
        while(decryptedBody[j] != '/') j++;
        string version = Encoding.ASCII.GetString(decryptedBody, i, j - i);
        i += version.Length; // Skip null
        j = i;
        while(decryptedBody[j] != (byte)0) j++;
        string commitHash = Encoding.ASCII.GetString(decryptedBody, i, j - i);
        Console.WriteLine($"🔓 Version: {version}, Commit Hash: {commitHash}");
        i = j;

        // Check for final null
        if (i >= decryptedBody.Length || decryptedBody[i] != '\0') return 0;
        i++;
        Console.WriteLine(" test 4");

        // Check for 0xFE
        if (i >= decryptedBody.Length || decryptedBody[i] != 0xFE) return 0;
        i++;
        Console.WriteLine(" test 5");

        // Extract 4-byte XOR key (nk)
        if (i + 4 > decryptedBody.Length) return 0;
        Console.WriteLine(" test 6");
        return BitConverter.ToUInt32(decryptedBody, i); // Assume little-endian
    }

    /*private async Task ProcessMessageAsync(DatumHeader header, byte[] body)
    {
        Console.WriteLine($"[RECV] Command: 0x{header.ProtoCmd:X2}, Length: {header.CmdLen} bytes");
        switch (header.ProtoCmd)
        {
            case 0x01: await HandleHelloAsync(header, body); break;
            case 0x05: await HandleMiningCommandAsync(header, body); break;
            default: Console.WriteLine($"⚠️ Received unknown command: 0x{header.ProtoCmd:X2}"); break;
        }
    }
    */
    /*private async Task ProcessMessageAsync(DatumHeader header, byte[] decryptedBody)
    {
        Console.WriteLine($"[RECV] Command: 0x{header.ProtoCmd:X2}, Length: {header.CmdLen} bytes");
        if (header.ProtoCmd == 1) // Handshake
        {
            
        }
        else if (header.ProtoCmd == 5) await HandleMiningCommandAsync(header, decryptedBody);
        else
        {
            Console.WriteLine("header.ProtoCmd = " + header.ProtoCmd);
            throw new InvalidOperationException("Unknown command from client");
        }
    }*/
/*
    private void DeriveSharedSecret(byte[] clientSessionX25519PubKey)
    {
        //var serverPrivateKeyBytes = _serverSessionSigningKey.Export(KeyBlobFormat.RawPrivateKey);
        //var clientPublicKeyBytes = clientSessionX25519PubKey;

        //_channelSharedSecret = KeyAgreementAlgorithm.X25519.Agree(_serverSessionSigningKey, new PublicKey(clientPublicKeyBytes, KeyBlobFormat.RawPublicKey));
        _channelSharedSecret = KeyAgreementAlgorithm.X25519.Agree(_serverSessionSigningKey, _clientSessionPubKey, new SharedSecretCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

    }
*/
    private byte[] InitializeNonce(uint nk, byte[] clientSessionEd25519PubKey)
    {
        var nonce = new byte[24]; // crypto_box_NONCEBYTES = 24
        nk -= 42;
        nk ^= BitConverter.ToUInt32(clientSessionEd25519PubKey, 7); // Offset 7 (8th byte)
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

    private void IncrementNonce(byte[] nonce)
    {
        // Increment nonce as a little-endian 192-bit integer
        for (int i = 0; i < nonce.Length; i++)
        {
            if (++nonce[i] != 0) break;
        }
    }


    /// <summary>
    /// Handles the initial handshake message from the client.
    /// This version contains the corrected NSec API calls.
    /// </summary>
    private async Task HandleHelloAsync(DatumHeader header, byte[] decryptedBody)
    {
        Console.WriteLine("   -> Received HELLO (0x01). Processing...");
        //I need to make sure all of these are initialized based on this message:
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
        // Fields are populated in FromBytes
        // --- Per-Session State ---
        //private PublicKey? _clientSessionPubKey;
        //private Key? _serverSessionSigningKey;
        //private Key? _serverSessionEncryptKey;
        //private SharedSecret? _channelSharedSecret; // The key for symmetric encryption
        //private byte[]? _sessionNonceSender; // Server’s send nonce (client’s receive nonce)
        //private UInt32 _sendingHeaderKey;
        //private UInt32 _receivingHeaderKey;

        //Initialize a new ed25519 key for signing the session messages with
        _serverSessionSigningKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        _clientSessionPubKey = PublicKey.Import(KeyAgreementAlgorithm.X25519, _helloMessage.ClientSessionEncryptPubKey, KeyBlobFormat.RawPublicKey);
        // Generate server session key if not set
        if (_serverSessionEncryptKey == null)
        {
            _serverSessionEncryptKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        }
        _channelSharedSecretBytes = CryptoUtils.ComputeSharedSecretForCryptoBox(_serverSessionEncryptKey.Export(KeyBlobFormat.RawPrivateKey), _clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey));
        _channelSharedSecret = SharedSecret.Import(_channelSharedSecretBytes, SharedSecretBlobFormat.RawSharedSecret);
        //_channelSharedSecret = KeyAgreementAlgorithm.X25519.Agree(_serverSessionEncryptKey, _clientSessionPubKey, new SharedSecretCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        //var sharedKey = Sodium.SecretBox.GenerateSharedKey(serverPrivateKey, clientPublicKey);
        //Sodium.SecretBox.
        //_channelSharedSecret = SharedSecret.Import(sharedKey, SharedSecretBlobFormat.RawSharedSecret);

        Console.WriteLine("//////////////  SHARED KEY PRECOMP   /////////////");
        //var x25519PubKeyBytes = _clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey); // 32 bytes        
        //var x25519PrivKeyBytes = _serverSessionEncryptKey.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes
        Console.WriteLine($"🔒 X25519 Server Pub Key (Base64): {Convert.ToBase64String(_serverSessionEncryptKey.Export(KeyBlobFormat.RawPublicKey))}");
        Console.WriteLine($"🔒 X25519 Server Pri Key (Base64): {Convert.ToBase64String(_serverSessionEncryptKey.Export(KeyBlobFormat.RawPrivateKey))}");
        Console.WriteLine($"🔒 X25519 Client Pub Key (Base64): {Convert.ToBase64String(_clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey))}"); //x25519Key.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes
        Console.WriteLine($"🔒 X25519 Shared Raw Key (Base64): {Convert.ToBase64String(_channelSharedSecretBytes)}");
        //Console.WriteLine($"🔒 X25519 Shared NSecKey (Base64): {Convert.ToBase64String(_channelSharedSecret.Export(SharedSecretBlobFormat.NSecSharedSecret))}");
        //Console.WriteLine($"🔒 X25519 Shared Raw Key (Base64): {Convert.ToBase64String(_channelSharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret))}");
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

        //var sharedKey = Sodium.CryptoBox.GenerateSharedKey(serverPrivateKey, clientPublicKey);

        Console.WriteLine("//////////////  SHARED KEY PRECOMP   /////////////");
        uint nk = BitConverter.ToUInt32(_helloMessage.xorKey, 0);
        _sendingHeaderKey = DatumHeaderXorFeedback(~nk);
        _receivingHeaderKey = DatumHeaderXorFeedback(nk);
        Console.WriteLine($"🔑 Updated XOR keys: Sending=0x{_sendingHeaderKey:X8}, Receiving=0x{_receivingHeaderKey:X8}");
        // Initialize nonce        
        if (nk == 0) throw new InvalidOperationException("Failed to extract XOR key");
        _sessionNonceSender = InitializeNonce(nk, _helloMessage.ClientSessionSigningPubKey);

        // Send response
        //_clientSessionPubKey = PublicKey.Import(KeyAgreementAlgorithm.X25519, helloMsg.SessionPubKey, KeyBlobFormat.RawPublicKey);
        //_serverSessionSigningKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        //_channelSharedSecret = KeyAgreementAlgorithm.X25519.Agree(_serverSessionSigningKey, _clientSessionPubKey, new SharedSecretCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var responsePayload = new HandshakeResponseMessage { /* ... payload initialization ... */ };
        // (The rest of the response generation logic is unchanged, but the encryption call will be fixed in the helper method)
        responsePayload.ClientSigningPubKey = _helloMessage.ClientSigningPubKey;
        responsePayload.ClientEncryptPubKey = _helloMessage.ClientEncryptPubKey;
        responsePayload.ClientSessionSigningPubKey = _helloMessage.ClientSessionSigningPubKey;
        responsePayload.ClientSessionEncryptPubKey = _helloMessage.ClientSessionEncryptPubKey;
        responsePayload.ServerSessionSigningPubKey = _serverSessionSigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey); //ed25519
        responsePayload.ServerSessionEncryptPubKey = _serverSessionEncryptKey.PublicKey.Export(KeyBlobFormat.RawPublicKey); //x25519
                                                                                                                            //responsePayload.CoinbaseTag = "Boot protocol";
                                                                                                                            //responsePayload.Uid = "21";
                                                                                                                            //responsePayload.MinDifficulty = 1.0;
                                                                                                                            //responsePayload.MessageOfTheDay = "Welcome to the DATUM Prime C# Server!";


        //var responsePayloadBytes = responsePayload.ToBytes();

        //var signature = SignatureAlgorithm.Ed25519.Sign(_ed25519LongTermKey, responsePayloadBytes);
        //var signedPayload = signature.Concat(responsePayloadBytes).ToArray();

        var responsePayloadBytes = responsePayload.ToBytes();
        //Console.WriteLine($"📦 Response payload: {BitConverter.ToString(responsePayloadBytes)}");

        var signature = SignatureAlgorithm.Ed25519.Sign(_ed25519LongTermKey, responsePayloadBytes);
        //Console.WriteLine($"📦 Signature: {BitConverter.ToString(signature)}");

        //var signedPayload = signature.Concat(responsePayloadBytes).ToArray();
        //Console.WriteLine($"📦 Signed payload: {BitConverter.ToString(signedPayload)}");
        var signedPayload = responsePayloadBytes.Concat(signature).ToArray();
        //Console.WriteLine($"📦 Signed payload (corrected): {BitConverter.ToString(signedPayload)}");


        var serverPubKey = _ed25519LongTermKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        //Console.WriteLine($"🔑 Server Ed25519 pub key: {BitConverter.ToString(serverPubKey)}");

        // Use the generic helper which now has the correct encryption logic
        await SendEncryptedMessageAsync(0x02, signedPayload, true, false, true);

        Console.WriteLine($"[SEND] Handshake Response (0x02), length " + signedPayload.Length);
        
        // Send 0x99 client configure message
        //var poolConfig = LoadPoolConfig("config.json"); // Adjust path as needed
        await SendClientConfigureAsync(_poolConfig);
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
        if (poolScriptBytes.Length > 255)
        {
            Console.WriteLine($"⚠️ Pool payout script too long ({poolScriptBytes.Length} bytes), truncating to 255");
            Array.Resize(ref poolScriptBytes, 255);
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
        Console.WriteLine($"📦 Client configure payload (before signing): {BitConverter.ToString(payloadBytes)}");

        // Generate signature
        var signature = SignatureAlgorithm.Ed25519.Sign(_serverSessionSigningKey, payloadBytes);
        Console.WriteLine($"📦 Signature: {BitConverter.ToString(signature)}");

        // Append signature
        var signedPayload = payloadBytes.Concat(signature).ToArray();
        Console.WriteLine($"📦 Signed payload: {BitConverter.ToString(signedPayload)}");

        // Send encrypted message (mining command 0x05, channel encryption)
        await SendEncryptedMessageAsync(0x05, signedPayload, isSigned: true, isEncryptedChannel: true, isEncryptedPubKey: false);
    }


    /// <summary>
    /// Handles all mining-related commands (sub-commands under 0x05).
    /// </summary>
    private async Task HandleMiningCommandAsync(DatumHeader header, byte[] encryptedBody)
    {
        if (_channelSharedSecretBytes == null) { /* Error handling */ return; }

        // --- Decryption ---
        var aead = AeadAlgorithm.XChaCha20Poly1305;

        // CORRECTION 3: A SharedSecret must be imported into a Key object before use.
        // We use a `using` block for proper disposal of the sensitive key material.
        //using var symmetricKey = Key.Import(aead, _channelSharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret), KeyBlobFormat.RawSymmetricKey);

        var nonce = encryptedBody.Take(aead.NonceSize).ToArray();
        var ciphertext = encryptedBody.Skip(aead.NonceSize).ToArray();

        // CORRECTION 4: The correct method is Decrypt, not TryDecrypt. It returns null on failure.
        var plaintext = new byte[0];  //aead.Decrypt(symmetricKey, nonce, null, ciphertext);

        if (plaintext == null)
        {
            Console.WriteLine("❌ MINING COMMAND DECRYPTION FAILED (authentication tag check failed).");
            // Depending on protocol rules, you might want to close the connection here.
            return;
        }

        byte subCmd = plaintext[0];
        byte[] subCmdPayload = plaintext.Skip(1).ToArray();

        Console.WriteLine($"   -> Received Mining Command (0x05), Sub-Command: 0x{subCmd:X2}");

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
        Console.WriteLine($"   -> Coinbase Fetch request with total reward: {fetchRequest.RewardValue / 100_000_000.0} BTC");
        var fetchResponse = new CoinbaserFetchResponseMessage();
        fetchResponse.Payouts.Add(new PayoutInfo
        {
            Value = fetchRequest.RewardValue,
            Address = "mpuPt3FvAfwQFxd6BmPrwuRBbdMgmDSGfH"
        });
        var responsePayload = new byte[] { 0x11 }.Concat(fetchResponse.ToBytes()).ToArray();
        await SendEncryptedMessageAsync(0x05, responsePayload, true, true, false);
        Console.WriteLine($"[SEND] Coinbaser Fetch Response (0x05, 0x11)");
    }

    private async Task HandlePowSubmitAsync(byte[] payload)
    {
        var powSubmit = PowSubmitMessage.FromBytes(payload);
        Console.WriteLine($"   -> ✅ Received Proof of Work submission with difficulty: {powSubmit.Difficulty}");
        var shareResponse = new ShareResponseMessage { Status = 0x50 };
        var responsePayload = new byte[] { 0x8F, shareResponse.Status };
        await SendEncryptedMessageAsync(0x05, responsePayload, true, true, false);
        Console.WriteLine($"[SEND] Share Response [ACCEPTED] (0x05, 0x8F)");
    }

    /// <summary>
    /// Generic helper to encrypt and send a message using the shared channel secret.
    /// This now contains the corrected NSec API calls.
    /// </summary>
    private async Task SendEncryptedMessageAsync(byte protoCmd, byte[] payload, bool isSigned = false, bool isEncryptedChannel = true, bool isEncryptedPubKey = false)
    {
        //Span<byte> finalMessageBody = new Span<byte>();
        byte[] finalMessageBody = new byte[payload.Length+48]; //Try +48 I guess?  + LibSodium.CryptoBox.MacLen wasn't enough
        Console.WriteLine("  Payload length: " + payload.Length);
        Console.WriteLine("finalMessageBody: " + finalMessageBody.Length);
        Console.WriteLine("Mac Bytes Length: " + LibSodium.CryptoBox.MacLen);

        if (isEncryptedPubKey)
        {
            // Public-key encryption for 0x02 handshake response
            if (_clientSessionPubKey == null) throw new InvalidOperationException("Cannot send sealed message without client session public key.");
            var clientPubKey = _clientSessionPubKey.Export(KeyBlobFormat.RawPublicKey); // Client’s session X25519 public key
            //finalMessageBody = Sodium.CryptoBox.Seal(payload, clientPubKey);
            //finalMessageBody = LibSodium.SealedPublicKeyBox.Create(payload, clientPubKey);
            finalMessageBody = LibSodium.CryptoBox.EncryptWithPublicKey(finalMessageBody, payload, clientPubKey).ToArray();
            //Console.WriteLine($"📦 Sealed ciphertext (public-key): {BitConverter.ToString(finalMessageBody.ToArray(), 0, Math.Min(finalMessageBody.Length, 64))}...");
            Console.WriteLine($"📦 Client public-key: {BitConverter.ToString(clientPubKey, 0, Math.Min(clientPubKey.Length, 64))}...");
        }
        else if (isEncryptedChannel)
        {
            // Symmetric encryption with shared secret for other messages
            if (_channelSharedSecret == null) throw new InvalidOperationException("Cannot send encrypted message without a shared secret.");
            if (_sessionNonceSender == null) throw new InvalidOperationException("Cannot send encrypted message without a sender nonce.");

            //var sharedKey = _channelSharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret); // 32 bytes
            var nonce = _sessionNonceSender; // 24 bytes
            finalMessageBody = LibSodium.CryptoBox.EncryptWithSharedKey(finalMessageBody, payload, _channelSharedSecretBytes, null, nonce).ToArray();
            Console.WriteLine(" after encryption finalMessageBody.length: " + finalMessageBody.Length);

            //finalMessageBody = nonce.Concat(ciphertext).ToArray();
            Console.WriteLine($"📦 Nonce: {BitConverter.ToString(nonce)}");
            //Console.WriteLine($"📦 Shared Key: {BitConverter.ToString(sharedKey)}");
            Console.WriteLine($"📦 Ciphertext (channel): {BitConverter.ToString(finalMessageBody, 0, finalMessageBody.Length)}...");
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
        Console.WriteLine($"📋 Sending Header: Cmd=0x{protoCmd:X2}, Len={header.CmdLen}, Signed={header.IsSigned}, PubKey={header.IsEncryptedPubKey}, Channel={header.IsEncryptedChannel}");

        // XOR header
        Console.WriteLine($"📦 Plaintext header: {BitConverter.ToString(header.ToBytes())}");
        uint headerValue = BitConverter.ToUInt32(header.ToBytes(), 0);
        headerValue ^= _sendingHeaderKey;
        var xoredHeaderBytes = BitConverter.GetBytes(headerValue);
        Console.WriteLine($"📦 XORed header: {BitConverter.ToString(xoredHeaderBytes)}");
        _sendingHeaderKey = DatumHeaderXorFeedback(_sendingHeaderKey);

        // Send header and body together
        var message = xoredHeaderBytes.Concat(finalMessageBody.ToArray()).ToArray();
        await _stream.WriteAsync(message, 0, message.Length);
        await _stream.FlushAsync();

        // Increment nonce only for channel encryption
        if (isEncryptedChannel)
        {
            IncrementNonce(_sessionNonceSender);
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
// byte array ('FromBytes'), mimicking the C structs from the reference implementation.
// =================================================================================

/// <summary>
/// Represents the 4-byte header at the start of every DATUM message.
/// Provides methods to pack/unpack the bitfields into a uint32.
/// </summary>
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
    public byte[] version; // Variable length string ending with '/', max 127 bytes
    public byte[] commitHash; // Variable length null-terminated string, max 127 bytes
    public byte[] xorKey; // Exactly 4-byte key
    public byte[] cryptoSignBytes; // Exactly 64 bytes (signature)
    public byte[] cryptoBoxSealBytes; // Placeholder, assuming padding or ignored

    public static (HelloMessage? Message, int BytesConsumed) FromBytes(byte[] data)
    {
        try
        {
            const int cryptoSignBytes = 64; // CRYPTO_SIGN_BYTES
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
    //public string CoinbaseTag { get; set; } = string.Empty;
    //public string Uid { get; set; } = string.Empty;
    //public double MinDifficulty { get; set; }
    public string MessageOfTheDay { get; set; } = "Hello...Neo.";
    
    // Helper to write a null-terminated string.
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
        //WriteNullTerminatedString(writer, CoinbaseTag);
        //WriteNullTerminatedString(writer, Uid);
        //writer.Write(MinDifficulty);
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
        
        // First, write the number of payouts.
        writer.Write((byte)Payouts.Count);

        // Then, write each payout struct.
        foreach (var payout in Payouts)
        {
            writer.Write(payout.Value);
            writer.Write(Encoding.UTF8.GetBytes(payout.Address));
            writer.Write((byte)0); // Null terminator for the address string.
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
    public ulong JobId { get; set; }
    // ... other fields exist but we only care about difficulty
    public double Difficulty { get; set; }

    public static PowSubmitMessage FromBytes(byte[] data)
    {
        // Based on the C struct layout, we need to read past the initial fields
        // to get to the difficulty.
        // T_DATUM_POW_SUBMIT_CMD {
        //   uint64_t job_id; // 8 bytes
        //   uint64_t nonce; // 8 bytes
        //   uint32_t time; // 4 bytes
        //   uint32_t version; // 4 bytes
        //   double difficulty; // 8 bytes
        //   ...
        // }
        const int difficultyOffset = 8 + 8 + 4 + 4;
        return new PowSubmitMessage
        {
            Difficulty = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(difficultyOffset, 8))
        };
    }
}

// SERVER: Share Response message (0x05, 0x8F)
public class ShareResponseMessage
{
    public byte Status { get; set; }
}