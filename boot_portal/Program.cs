// Program.cs

using System;
using System.Buffers.Binary;
using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

// =================================================================================
// 1. MAIN PROGRAM ENTRY POINT
// =================================================================================
// This class is responsible for parsing command-line arguments, managing the
// server's primary cryptographic key, and starting the TCP server.
// =================================================================================
public class Program
{
    // The default port for the DATUM protocol.
    private const int DatumPort = 3008;

    // Async Main method allows us to use 'await' for network operations.
    public static async Task Main(string[] args)
    {
        // --- Command-Line Argument Setup ---
        // We use System.CommandLine to define and parse arguments.
        var rootCommand = new RootCommand("DATUM Prime C# Server");
        var privateKeyOption = new Option<string?>(
            name: "--private-key",
            description: "The Base64 encoded private key for the server. If not provided, a new key pair will be generated."
        );
        rootCommand.AddOption(privateKeyOption);

        // This handler is executed when the program is run.
        rootCommand.SetHandler(async (privateKeyBase64) =>
        {
            Key serverKey;

            // --- Key Management ---
            // The server needs a long-term Ed25519 key pair. This is used to sign critical
            // messages and prove the server's identity.
            if (!string.IsNullOrEmpty(privateKeyBase64))
            {
                // If a private key is provided, import it.
                // This allows the server to maintain a persistent identity across restarts.
                try
                {
                    var privateKeyBytes = Convert.FromBase64String(privateKeyBase64);
                    serverKey = Key.Import(SignatureAlgorithm.Ed25519, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
                    Console.WriteLine("✅ Successfully loaded server key from command line argument.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to load private key: {ex.Message}");
                    return;
                }
            }
            else
            {
                // If no key is provided, generate a new one.
                // This is useful for testing or initial setup. The public key must be
                // given to the client (DATUM Gateway) to allow it to connect.
                serverKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                Console.WriteLine("⚠️ No private key provided. Generated a new temporary key pair.");
                
                // Export the keys in Base64 format for easy copying.
                var pubKeyBytes = serverKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
                var privKeyBytes = serverKey.Export(KeyBlobFormat.RawPrivateKey);
                
                Console.WriteLine("\n====================== IMPORTANT ======================");
                Console.WriteLine("Copy this public key into your DATUM Gateway's config.json:");
                Console.WriteLine($"🔑 Server Public Key (Base64): {Convert.ToBase64String(pubKeyBytes)}");
                Console.WriteLine("\nSave this private key to reuse this server identity later:");
                Console.WriteLine($"🔒 Server Private Key (Base64): {Convert.ToBase64String(privKeyBytes)}");
                Console.WriteLine("=======================================================\n");
            }
            
            // --- Start the Server ---
            var server = new DatumServer(IPAddress.Any, DatumPort, serverKey);
            await server.StartAsync();

        }, privateKeyOption);

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

    public DatumServer(IPAddress address, int port, Key serverKey)
    {
        _listener = new TcpListener(address, port);
        _serverKey = serverKey;
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
            var clientHandler = new ClientHandler(client, _serverKey);

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

    // --- Per-Session State ---
    private PublicKey? _clientSessionPubKey;
    private Key? _serverSessionKey;
    private SharedSecret? _channelSharedSecret; // The key for symmetric encryption (AEAD).

    public ClientHandler(TcpClient client, Key serverLongTermKey)
    {
        _client = client;
        _stream = client.GetStream();
        _serverLongTermKey = serverLongTermKey;
    }

    public async Task HandleClientAsync()
    {
        try
        {
            while (_client.Connected)
            {
                var headerBuffer = new byte[4];
                int bytesRead = await _stream.ReadAsync(headerBuffer, 0, headerBuffer.Length);
                if (bytesRead == 0) break;

                var header = DatumHeader.FromBytes(headerBuffer);
                var bodyBuffer = new byte[header.CmdLen];
                await _stream.ReadExactlyAsync(bodyBuffer, 0, bodyBuffer.Length);
                await ProcessMessageAsync(header, bodyBuffer);
            }
        }
        catch (IOException) { Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} disconnected."); }
        catch (Exception ex) { Console.WriteLine($"💥 An error occurred with client {_client.Client.RemoteEndPoint}: {ex.Message}\n{ex.StackTrace}"); }
        finally { _client.Close(); }
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
    private async Task HandleHelloAsync(DatumHeader header, byte[] encryptedBody)
    {
        Console.WriteLine("   -> Received HELLO (0x01). Decrypting and processing...");

        byte[]? plaintext;
        try
        {
            // --- Decryption ---
            // CORRECTION 1: Converting the server's Ed25519 key to an X25519 key for decryption.
            // NSec requires an explicit export/import of the key seed to perform this conversion.
            var serverEd25519Seed = _serverLongTermKey.Export(KeyBlobFormat.RawPrivateKey);
            using var serverX25519PrivateKey = Key.Import(KeyAgreementAlgorithm.X25519, serverEd25519Seed, KeyBlobFormat.RawPrivateKey);

            // CORRECTION 2: The correct method is AeadAlgorithm.OpenSealedBox, not TryOpenSealedBox.
            // It throws a CryptographicException on failure, so we use a try-catch block.
            plaintext = AeadAlgorithm.OpenSealedBox(serverX25519PrivateKey, encryptedBody);
            plaintext = XChaCha20Poly1305.OpenSealedBox(serverX25519PrivateKey, encryptedBody);
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
        _channelSharedSecret = KeyAgreementAlgorithm.X25519.Agree(_serverSessionKey, _clientSessionPubKey);

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
        using var symmetricKey = Key.Import(aead, _channelSharedSecret.Export(), KeyBlobFormat.RawSymmetricKey);

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
        using var symmetricKey = Key.Import(aead, _channelSharedSecret.Export(), KeyBlobFormat.RawSymmetricKey);
        
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