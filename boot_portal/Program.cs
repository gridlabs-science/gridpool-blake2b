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
using Sodium;
using System;
using System.IO;

// =================================================================================
// 1. MAIN PROGRAM ENTRY POINT
// =================================================================================
// This class is responsible for parsing command-line arguments, managing the
// server's primary cryptographic key, and starting the TCP server.
// =================================================================================
// JSON configuration class for boot_portal_config.json
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
                Console.WriteLine("⚠️ No Ed25519 private key provided. Generated a new temporary Ed25519 key pair.");
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
                Console.WriteLine("⚠️ No X25519 private key provided. Generated a new temporary X25519 key pair.");
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

            Console.WriteLine("\n====================== IMPORTANT ======================");
            Console.WriteLine("Copy this combined public key (Ed25519 + X25519, hex-encoded) into your DATUM Gateway's config.json:");
            Console.WriteLine($"🔑 Server Public Key (Hex): {combinedPubKeyHex}");
            Console.WriteLine("\nSave these private keys to reuse this server identity later:");
            Console.WriteLine($"🔒 Ed25519 Private Key (Base64): {Convert.ToBase64String(ed25519PrivKeyBytes)}");
            Console.WriteLine($"🔒 X25519 Private Key (Base64): {Convert.ToBase64String(x25519PrivKeyBytes)}");
            Console.WriteLine("=======================================================\n");

            // Start the server
            var server = new DatumServer(IPAddress.Any, DatumPort, ed25519Key, x25519Key);
            await server.StartAsync();
        }, ed25519PrivateKeyOption, x25519PrivateKeyOption);

        await rootCommand.InvokeAsync(args);
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

    public DatumServer(IPAddress address, int port, Key serverKey, Key serverXKey)
    {
        _listener = new TcpListener(address, port);
        _serverKey = serverKey;
        _serverXKey = serverXKey;
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
            var clientHandler = new ClientHandler(client, _serverKey, _serverXKey);

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
    private readonly Key _serverLongTermKey; // The server's main Ed25519 key.
    //private readonly Key _ed25519KeyLongTerm; // The server's long-term Ed25519 key.
    private readonly Key _x25519KeyLongTerm; // The server's long-term x25519 key.

    // --- Per-Session State ---
    private PublicKey? _clientSessionPubKey;
    private Key? _serverSessionKey;
    private SharedSecret? _channelSharedSecret; // The key for symmetric encryption (AEAD).
    private UInt32 _sendingHeaderKey;

    public ClientHandler(TcpClient client, Key serverLongTermKey, Key serverLongTermXKey)
    {
        _client = client;
        _stream = client.GetStream();
        _serverLongTermKey = serverLongTermKey;
        _sendingHeaderKey = 0xDC871829; // initial send header key ... changed by handshake function
        //_ed25519KeyLongTerm = serverLongTermEdKey;
        _x25519KeyLongTerm = serverLongTermXKey;
    }

    public async Task HandleClientAsync()
    {
        try
        {
            while (_client.Connected)
            {
                //TODO:  I just realized that the client code sets an initial, hard coded value for the header key on the hello message:
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
                Console.WriteLine($"📋 Parsed header: Cmd={header.ProtoCmd}, Len={header.CmdLen}, Signed={header.IsSigned}, Encrypted={header.IsEncryptedPubKey}");


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

                // Step 3: Decrypt the body
                byte[]? decryptedBody = DecryptBody(bodyBuffer, bytesRead);
                if (decryptedBody == null)
                {
                    Console.WriteLine($"❌ Failed to decrypt body for client {_client.Client.RemoteEndPoint}");
                    break;
                }
                Console.WriteLine($"🔓 Decrypted body ({decryptedBody.Length} bytes)");

                

                // Verify cmd_len matches decrypted body length
                if (header.CmdLen != decryptedBody.Length + 48)  //Modified to account for CryptoBoxSealBytes, the signature that is added to the encrypted payload.
                {
                    Console.WriteLine($"⚠️ Header cmd_len ({header.CmdLen}) does not match decrypted body length ({decryptedBody.Length})");
                    break;
                }

                // Step 5?: Extract XOR key (nk) from decrypted body
                //TODO: I probably have other operations I need to do to this before it's good to use
                //  see datum_protocol.c lines 1058 and 156
                _sendingHeaderKey = ExtractXorKey(decryptedBody);

                //Now we can send it to the appropriate command handler:
                await ProcessMessageAsync(header, bodyBuffer);


            }
        }
        catch (IOException) { Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} disconnected."); }
        catch (Exception ex) { Console.WriteLine($"💥 An error occurred with client {_client.Client.RemoteEndPoint}: {ex.Message}\n{ex.StackTrace}"); }
        finally { _client.Close(); }
    }

    private byte[]? DecryptBody(byte[] encryptedBody, int bytesRead)
    {
        Console.WriteLine($"📦 Ciphertext first 32 bytes: {BitConverter.ToString(encryptedBody, 0, 32)}");
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
            var serverKeyPair = new Sodium.KeyPair(publicKeyBytes, privateKeyBytes);

            // Truncate input to actual length
            var cipherText = encryptedBody.AsSpan(0, bytesRead).ToArray();

            // Decrypt using crypto_box_seal_open
            var decrypted = Sodium.SealedPublicKeyBox.Open(cipherText, serverKeyPair);
            if (decrypted == null)
            {
                Console.WriteLine("❌ Decryption failed: Sodium.SealedPublicKeyBox.Open returned null");
                return null;
            }

            Console.WriteLine($"🔓 Decrypted {decrypted.Length} bytes");
            //Console.WriteLine($"-> {BitConverter.ToString(decrypted)}");
            Console.WriteLine($"🔓 Client signing public key:    {BitConverter.ToString(decrypted, 0, 16)}...");
            Console.WriteLine($"🔓 Client encryption public key: {BitConverter.ToString(decrypted, 64, 16)}...");
            //int i = 128; // Skip public keys
            //string version = Encoding.ASCII.GetString(decrypted, i, Array.IndexOf(decrypted, (byte)0, i) - i);
            //i += version.Length + 1; // Skip null
            //if (decrypted[i] == '/') i++;
            //string commitHash = Encoding.ASCII.GetString(decrypted, i, Array.IndexOf(decrypted, (byte)0, i) - i);
            //Console.WriteLine($"🔓 Version: {version}, Commit Hash: {commitHash}");
            return decrypted;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Decryption error: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private UInt32 ExtractXorKey(byte[] decryptedBody)
    {
        // Find 0xFE marker after version, '/', commit hash, and optional tag
        int i = 0;
        // Skip public keys (128 bytes)
        i += 128;

        // Find first null terminator (end of version)
        while (i < decryptedBody.Length && decryptedBody[i] != 0) i++;
        if (i >= decryptedBody.Length) return 0;
        i++; // Skip null

        // Skip '/' separator
        if (i >= decryptedBody.Length || decryptedBody[i] != '/') return 0;
        i++;

        // Skip commit hash
        while (i < decryptedBody.Length && decryptedBody[i] != 0) i++;
        if (i >= decryptedBody.Length) return 0;
        i++; // Skip null

        // Check for optional tag
        if (i < decryptedBody.Length && decryptedBody[i] == '(')
        {
            i++;
            while (i < decryptedBody.Length && decryptedBody[i] != 0) i++;
            if (i >= decryptedBody.Length) return 0;
            i++; // Skip null
            if (i < decryptedBody.Length && decryptedBody[i] == ')') i++;
        }

        // Check for final null
        if (i >= decryptedBody.Length || decryptedBody[i] != 0) return 0;
        i++;

        // Check for 0xFE
        if (i >= decryptedBody.Length || decryptedBody[i] != 0xFE) return 0;
        i++;

        // Extract 4-byte XOR key (nk)
        if (i + 4 > decryptedBody.Length) return 0;
        return BitConverter.ToUInt32(decryptedBody, i); // Assume little-endian
    }

    private async Task ProcessMessageAsync(DatumHeader header, byte[] body)
    {
        Console.WriteLine($"[RECV] Command: 0x{header.ProtoCmd:X2}, Length: {header.CmdLen} bytes");
        switch (header.ProtoCmd)
        {
            case 0x01: await HandleHelloAsync(header, body); break;
            case 0x05: await HandleMiningCommandAsync(header, body); break;
            default: Console.WriteLine($"⚠️ Received unknown command: 0x{header.ProtoCmd:X2}"); break;
        }
    }

    /// <summary>
    /// Handles the initial handshake message from the client.
    /// This version contains the corrected NSec API calls.
    /// </summary>
    private async Task HandleHelloAsync(DatumHeader header, byte[] decryptedBody)
    {
        Console.WriteLine("   -> Received HELLO (0x01). Decrypting and processing...");

        byte[]? plaintext;
        try
        {
            // --- Decryption ---
            // CORRECTION 1: Converting the server's Ed25519 key to an X25519 key for decryption.
            // NSec requires an explicit export/import of the key seed to perform this conversion.
            //var serverEd25519Seed = _serverLongTermKey.Export(KeyBlobFormat.RawPrivateKey);
            //using var serverX25519PrivateKey = Key.Import(KeyAgreementAlgorithm.X25519, serverEd25519Seed, KeyBlobFormat.RawPrivateKey);
            
            // Create a BouncyCastle XSalsa20-Poly1305 engine
            //ICipherParameters keyParamWithIv = new Ed25519PrivateKeyParameters(serverEd25519Seed);
            
            //var cipher = new XSalsa20Engine();
            //cipher.Init(false, keyParamWithIv);
            //var plainTextData = new byte[encryptedBody.Length];

            //for (var j = 0; j < encryptedBody.Length; j++)
            //{
            //    plainTextData[j] = cipher.ReturnByte(encryptedBody[j]);
            //}

            plaintext = decryptedBody;
        }
        catch (CryptographicException ex)
        {
            Console.WriteLine($"❌ HELLO DECRYPTION FAILED: {ex.Message}. Closing connection.");
            _client.Close();
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ An unexpected error occurred during HELLO decryption: {ex.Message}");
            _client.Close();
            return;
        }

        var helloMsg = HelloMessage.FromBytes(plaintext);
        Console.WriteLine($"   -> Hello from client agent: {helloMsg.Agent}");

        _clientSessionPubKey = PublicKey.Import(KeyAgreementAlgorithm.X25519, helloMsg.SessionPubKey, KeyBlobFormat.RawPublicKey);
        _serverSessionKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        _channelSharedSecret = KeyAgreementAlgorithm.X25519.Agree(_serverSessionKey, _clientSessionPubKey, new SharedSecretCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport});

        var responsePayload = new HandshakeResponseMessage { /* ... payload initialization ... */ };
        // (The rest of the response generation logic is unchanged, but the encryption call will be fixed in the helper method)
        responsePayload.ClientPubKey = helloMsg.ClientPubKey;
        responsePayload.ClientSessionPubKey = helloMsg.SessionPubKey;
        responsePayload.PoolSessionPubKey = _serverSessionKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        responsePayload.CoinbaseTag = "Boot protocol";
        responsePayload.Uid = "21";
        responsePayload.MinDifficulty = 1.0;
        responsePayload.MessageOfTheDay = "Welcome to the DATUM Prime C# Server!";
        var responsePayloadBytes = responsePayload.ToBytes();

        var signature = SignatureAlgorithm.Ed25519.Sign(_serverLongTermKey, responsePayloadBytes);
        var signedPayload = signature.Concat(responsePayloadBytes).ToArray();

        // Use the generic helper which now has the correct encryption logic
        await SendEncryptedMessageAsync(0x02, signedPayload, true, true);
        
        Console.WriteLine($"[SEND] Handshake Response (0x02)");
    }

    /// <summary>
    /// Handles all mining-related commands (sub-commands under 0x05).
    /// </summary>
    private async Task HandleMiningCommandAsync(DatumHeader header, byte[] encryptedBody)
    {
        if (_channelSharedSecret == null) { /* Error handling */ return; }

        // --- Decryption ---
        var aead = AeadAlgorithm.XChaCha20Poly1305;
        
        // CORRECTION 3: A SharedSecret must be imported into a Key object before use.
        // We use a `using` block for proper disposal of the sensitive key material.
        using var symmetricKey = Key.Import(aead, _channelSharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret), KeyBlobFormat.RawSymmetricKey);

        var nonce = encryptedBody.Take(aead.NonceSize).ToArray();
        var ciphertext = encryptedBody.Skip(aead.NonceSize).ToArray();
        
        // CORRECTION 4: The correct method is Decrypt, not TryDecrypt. It returns null on failure.
        var plaintext = aead.Decrypt(symmetricKey, nonce, null, ciphertext);

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
        fetchResponse.Payouts.Add(new PayoutInfo {
            Value = fetchRequest.RewardValue, Address = "mpuPt3FvAfwQFxd6BmPrwuRBbdMgmDSGfH"
        });
        var responsePayload = new byte[] { 0x11 }.Concat(fetchResponse.ToBytes()).ToArray();
        await SendEncryptedMessageAsync(0x05, responsePayload);
        Console.WriteLine($"[SEND] Coinbaser Fetch Response (0x05, 0x11)");
    }

    private async Task HandlePowSubmitAsync(byte[] payload)
    {
        var powSubmit = PowSubmitMessage.FromBytes(payload);
        Console.WriteLine($"   -> ✅ Received Proof of Work submission with difficulty: {powSubmit.Difficulty}");
        var shareResponse = new ShareResponseMessage { Status = 0x50 };
        var responsePayload = new byte[] { 0x8F, shareResponse.Status };
        await SendEncryptedMessageAsync(0x05, responsePayload);
        Console.WriteLine($"[SEND] Share Response [ACCEPTED] (0x05, 0x8F)");
    }
    
    /// <summary>
    /// Generic helper to encrypt and send a message using the shared channel secret.
    /// This now contains the corrected NSec API calls.
    /// </summary>
    private async Task SendEncryptedMessageAsync(byte protoCmd, byte[] payload, bool isSigned = false, bool isEncryptedChannel = true)
    {
        if (_channelSharedSecret == null) throw new InvalidOperationException("Cannot send encrypted message without a shared secret.");

        var aead = AeadAlgorithm.XChaCha20Poly1305;
        
        // CORRECTION 3 (applied to sending): A SharedSecret must be imported into a Key object.
        using var symmetricKey = Key.Import(aead, _channelSharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret), KeyBlobFormat.RawSymmetricKey);
        
        var nonce = new byte[aead.NonceSize];
        RandomNumberGenerator.Fill(nonce);

        // The Encrypt method correctly takes the Key object.
        var ciphertext = aead.Encrypt(symmetricKey, nonce, null, payload);
        var finalMessageBody = nonce.Concat(ciphertext).ToArray();

        var header = new DatumHeader
        {
            CmdLen = (uint)finalMessageBody.Length,
            IsEncryptedChannel = isEncryptedChannel,
            IsSigned = isSigned,
            ProtoCmd = protoCmd
        };


        Console.WriteLine($"📦 Ciphertext out to client: {BitConverter.ToString(header.ToBytes(), 0, 4)}...");
        Console.WriteLine($"📦 Ciphertext first 32 bytes: {BitConverter.ToString(finalMessageBody, 0, 4)}...");
        await _stream.WriteAsync(header.ToBytes());
        await _stream.WriteAsync(finalMessageBody);
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
    public byte[] ClientPubKey { get; set; } = new byte[32]; // ed25519
    public byte[] SessionPubKey { get; set; } = new byte[32]; // x25519
    public string Agent { get; set; } = string.Empty;

    public static HelloMessage FromBytes(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        var msg = new HelloMessage();
        reader.Read(msg.ClientPubKey, 0, 32);
        reader.Read(msg.SessionPubKey, 0, 32);

        // Read the null-terminated string for the agent.
        var agentBytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0)
        {
            agentBytes.Add(b);
        }
        msg.Agent = Encoding.UTF8.GetString(agentBytes.ToArray());
        return msg;
    }
}

// SERVER: Handshake Response message (0x02)
public class HandshakeResponseMessage
{
    public byte[] ClientPubKey { get; set; } = new byte[32];
    public byte[] ClientSessionPubKey { get; set; } = new byte[32];
    public byte[] PoolSessionPubKey { get; set; } = new byte[32];
    public string CoinbaseTag { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public double MinDifficulty { get; set; }
    public string MessageOfTheDay { get; set; } = string.Empty;
    
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

        writer.Write(ClientPubKey);
        writer.Write(ClientSessionPubKey);
        writer.Write(PoolSessionPubKey);
        WriteNullTerminatedString(writer, CoinbaseTag);
        WriteNullTerminatedString(writer, Uid);
        writer.Write(MinDifficulty);
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