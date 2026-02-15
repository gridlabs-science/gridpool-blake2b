using System.Buffers.Binary;
using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using boot_portal;
using boot_portal.HostedServices;
using boot_portal.Utils;
using NSec.Cryptography;
using Microsoft.AspNetCore.SignalR;


// =================================================================================
// 1. MAIN PROGRAM ENTRY POINT
// =================================================================================
// This class is responsible for parsing command-line arguments, managing the
// server's primary cryptographic key, and starting the TCP server.
// =================================================================================
// JSON configuration class for boot_portal_config.json

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

    [JsonPropertyName("winners_list_size")]
    public int WinnersListSize { get; set; } = 3;

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
    private static int DatumPort = 3008;  //Defaults to 3008.  Should get set by config file.
    private const string ConfigFilePath = "boot_portal_config.json";
    public static ulong BLOCK_REWARD = 312_500_000;  //TODO: Need to detect this from the blockchain, so it gracefully handles the next epoch
    public static int TeamSize = 16;

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
                    ReadOnlySpan<byte> privateKey = Convert.FromBase64String(ed25519KeySource);
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
            var combinedPubKeyHex = Convert.ToHexString(combinedPubKey).ToLower(); // 128 hex characters

            //Now load or setup the pool config options, like default payout address and coinbase tag
            PoolConfig _poolConfig = LoadPoolConfig(ConfigFilePath);
            Program.TeamSize = _poolConfig.WinnersListSize;

            Console.WriteLine("\n====================== IMPORTANT ======================");
            Console.WriteLine("Copy this combined public key (Ed25519 + X25519, hex-encoded) into your DATUM Gateway's config.json:");
            Console.WriteLine($"🔑 Server Public Key (Hex): {combinedPubKeyHex}");
            Console.WriteLine("\nSave these private keys to reuse this server identity later:");
            Console.WriteLine($"🔒 Ed25519 Private Key (Base64): {Convert.ToBase64String(ed25519PrivKeyBytes)}");
            Console.WriteLine($"🔒 X25519 Private Key (Base64): {Convert.ToBase64String(x25519PrivKeyBytes)}"); //x25519Key.Export(KeyBlobFormat.RawPrivateKey); // 32 bytes
            Console.WriteLine("=======================================================\n");

            //UI Server stuff:
            var builder = WebApplication.CreateBuilder(args);
            builder.Configuration.AddJsonFile("boot_portal_config.json", optional: false, reloadOnChange: true);

            builder.WebHost.UseUrls("http://0.0.0.0:5000", "https://0.0.0.0:5001");

            builder.Services.AddRazorPages(); // For serving simple HTML pages
            builder.Services.AddControllers();
            builder.Services.AddSignalR();    // For real-time updates
            
            // Start your background services
            //builder.Services.AddHostedService<BitcoinZmqSubscriber>();
            // *** START CONFIGURABLE SERVICE SECTION ***

            // 1. Read the source from appsettings.json
            string notificationSource = builder.Configuration["NotificationSource"] ?? "MempoolSpace";

            //Console.WriteLine("Using notification source: {Source}", notificationSource);

            // 2. Conditionally register the correct hosted service
            if (notificationSource.Equals("ZMQ", StringComparison.OrdinalIgnoreCase))
            {
                builder.Services.AddHostedService<BitcoinZmqSubscriber>();
                Console.WriteLine("Block Notification source set to ZMQ");
            }
            else if (notificationSource.Equals("MempoolSpace", StringComparison.OrdinalIgnoreCase))
            {
                builder.Services.AddHostedService<MempoolSpaceSocketSubscriber>();
                Console.WriteLine("Block Notification source set to Mempool.Space Web Socket API");
            }
            else
            {
                Console.WriteLine("Unknown NotificationSource.  Check the boot_portal_settings.json config file");
                builder.Services.AddHostedService<MempoolSpaceSocketSubscriber>();
            }

            // *** END CONFIGURABLE SERVICE SECTION ***
            builder.Services.AddHostedService<DatumServer>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILogger<DatumServer>>();
                var hubContext = serviceProvider.GetRequiredService<IHubContext<PoolStatsHub>>();
                // The 'serviceProvider' allows you to get other services if needed,
                // but here we just use the local variables from Program.cs

                // We pass in all the necessary constructor arguments here:
                return new DatumServer(
                    IPAddress.Any,
                    DatumPort,
                    ed25519Key,
                    x25519Key,
                    _poolConfig,
                    hubContext,
                    logger);
            });

            var app = builder.Build();

            // 2. Configure the web app
            app.UseStaticFiles(); // Serve static files like CSS, JS, images
            app.UseRouting();
            app.MapRazorPages(); // Use a simple page system
            app.MapControllers();

            // 3. Tell the app where your SignalR Hub lives
            app.MapHub<PoolStatsHub>("/poolStatsHub"); // This is the URL your JS will use
            
            // Runs and blocks this thread while all other services run
            // Graceful shutdown is handled by the "AddHostedService" call above
            await app.RunAsync();

            // TODO: Start the Stratum V1 and V2 servers as well, or with .config options just start the chosen servers.
            
            // TODO: Also start the peer to peer node so we can actually connect to the boot-protocol network

            Console.WriteLine("All services stopped.");
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
    private static string clientPayoutAddress = "";

    private static CancellationToken stoppingToken;


    public ClientHandler(TcpClient client, Key serverLongTermKey, Key serverLongTermXKey, PoolConfig poolConfig, CancellationToken st)
    {
        _client = client;
        _stream = client.GetStream();
        _ed25519LongTermKey = serverLongTermKey;
        _receivingHeaderKey = 0xDC871829; // initial send header key ... changed by handshake function
        _sendingHeaderKey = 0;
        _x25519KeyLongTerm = serverLongTermXKey;
        _poolConfig = poolConfig;
        clientPayoutAddress = _poolConfig.PoolPayoutScript; //Temporary, just until we get the client's first PoW share, which has their address/username.  
        stoppingToken = st;
        Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} connected.");
    }

    public async Task HandleClientAsync()
    {

        try
        {
            // We only peek the protocol once at the very start of the connection
            bool protocolDetermined = false;
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
                // --- NEW: Protocol Detection Logic ---
                if (!protocolDetermined)
                {
                    // Stratum V1 JSON usually starts with '{' (0x7B)
                    // We check if the first byte is '{'. 
                    if (headerBuffer[0] == 0x7B) 
                    {
                        Console.WriteLine($"🔀 Stratum V1 detected from {_client.Client.RemoteEndPoint}. Forwarding to Gateway...");
                        
                        // Hand off control to the proxy method. 
                        // We pass the 4 bytes we already read so they aren't lost.
                        await HandleStratumProxyAsync(headerBuffer, bytesRead);
                        
                        // Once the proxy session ends, we break the loop and disconnect.
                        break; 
                    }
                    protocolDetermined = true;
                }
                // -------------------------------------
                if (bytesRead < 4)
                {
                    Console.WriteLine($"⚠️ Partial header received ({bytesRead} bytes): {BitConverter.ToString(headerBuffer, 0, bytesRead)}");
                    break;
                }
                //Console.WriteLine($"📥 Received header bytes: {BitConverter.ToString(headerBuffer)}");
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
                //Console.WriteLine($"📦 Received body ({bytesRead} bytes)");
                
                // Step 3: Decrypt the body
                byte[]? decryptedBody = null;
                //TODO: This if-else could be more robust, and check header.isEncryptedChannel as well
                if (header.IsEncryptedPubKey)
                {
                    //Console.WriteLine("Decrypting Signed message");
                    decryptedBody = DecryptSigned(bodyBuffer, bytesRead);
                    // Verify cmd_len matches decrypted body length
                    //TODO: change "48" to actually reference the libsodium constant instead.
                    //Modified (+48) to account for CryptoBoxSealBytes, the signature that is added to the encrypted payload.
                    if (header.CmdLen != decryptedBody.Length + 48) { Console.WriteLine($"⚠️ Header cmd_len ({header.CmdLen}) does not match decrypted body length ({decryptedBody.Length})"); break; }
                }  //      We need to use a different decryption key depending on the header.protoCmmd
                else if (header.IsEncryptedChannel)
                {
                    //Console.WriteLine("Decrypting Standard message");
                    decryptedBody = DecryptStandard(bodyBuffer, bytesRead);
                    // Verify cmd_len matches decrypted body length
                    //TODO: change "16" to actually reference the libsodium constant instead.
                    //Modified (+16) to account for MAC bytes, the signature that is added to the encrypted payload.  I think.
                    if (decryptedBody == null) Console.WriteLine("decrypted body is null");
                    if (header.CmdLen != decryptedBody.Length + 16) { Console.WriteLine($"⚠️ Header cmd_len ({header.CmdLen}) does not match decrypted body length ({decryptedBody.Length})"); break;}

                }
                if (decryptedBody == null)
                {
                    Console.WriteLine(" Header info: Cmd=" + (header.ProtoCmd) + " / CmdLen=" + header.CmdLen + " / isSigned=" + header.IsSigned + " / isEncryptedPubKey=" + header.IsEncryptedPubKey + " / isEncryptedChannel=" + header.IsEncryptedChannel);
                    Console.WriteLine($"❌ Failed to decrypt body for client {_client.Client.RemoteEndPoint}");
                    Console.WriteLine(BitConverter.ToString(bodyBuffer));
                    
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

    /// <summary>
    /// Forwards traffic bi-directionally between the connected Client and the Onsite Gateway.
    /// Handles the "Handover" of the first 4 bytes transparently.
    /// </summary>
    private async Task HandleStratumProxyAsync(byte[] initialBuffer, int initialCount)
    {
        // CONFIGURATION
        string gatewayIp = "192.168.1.223"; // Ensure this IP is reachable
        int gatewayPort = 23334;         // Ensure this is the STRATUM (Plaintext) port, not DATUM

        Console.WriteLine($"🔄 Proxy: Connecting client {_client.Client.RemoteEndPoint} to Gateway ({gatewayIp}:{gatewayPort})...");

        using (var gatewayClient = new System.Net.Sockets.TcpClient())
        {
            try
            {
                // 1. Attempt to connect to the Gateway
                // We use a small timeout logic here to fail fast if the server is down
                var connectTask = gatewayClient.ConnectAsync(gatewayIp, gatewayPort);
                if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                {
                    throw new TimeoutException("Timed out waiting for Gateway response.");
                }
                await connectTask; // Re-await to propagate exceptions if failed

                Console.WriteLine("✅ Proxy: Connected to Gateway. Starting pipe...");

                using (var gatewayStream = gatewayClient.GetStream())
                {
                    // 2. Replay the initial bytes (The 'Header' we peeked)
                    // We MUST write this before hooking up the pipes.
                    if (initialCount > 0)
                    {
                        await gatewayStream.WriteAsync(initialBuffer, 0, initialCount);
                        await gatewayStream.FlushAsync(); // Force push
                    }

                    // 3. Define the Pipe CancellationToken
                    // This token cancels the copy operation if one side disconnects
                    using (var cts = new CancellationTokenSource())
                    {
                        // Task A: Miner -> Gateway (Append to the stream we already started)
                        var clientToGateway = CopyStreamWithCloseAsync(_stream, gatewayStream, cts.Token, "Miner->Gateway");

                        // Task B: Gateway -> Miner
                        var gatewayToClient = CopyStreamWithCloseAsync(gatewayStream, _stream, cts.Token, "Gateway->Miner");

                        // 4. Wait for EITHER side to close the connection
                        await Task.WhenAny(clientToGateway, gatewayToClient);
                        
                        // Cancel the other task so we don't leave hanging sockets
                        cts.Cancel(); 
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Proxy Error: {ex.GetType().Name} - {ex.Message}");
                // Optional: Send a Stratum Error back to the miner so they know why they were dropped
                // var errorJson = "{\"id\":null,\"result\":null,\"error\":[20,\"Internal Proxy Error\",null]}\n";
                // byte[] errBytes = System.Text.Encoding.UTF8.GetBytes(errorJson);
                // await _stream.WriteAsync(errBytes, 0, errBytes.Length);
            }
            finally
            {
                Console.WriteLine($"🛑 Proxy: Session ended for {_client.Client.RemoteEndPoint}");
            }
        }
    }

    // Helper to copy streams and detect closure
    private async Task CopyStreamWithCloseAsync(Stream source, Stream destination, CancellationToken token, string name)
    {
        try
        {
            // Use a smaller buffer for Stratum (low latency)
            // Stratum messages are small; 4KB is plenty.
            await source.CopyToAsync(destination, 4096, token);
        }
        catch (OperationCanceledException) { /* Expected on shutdown */ }
        catch (IOException) { /* Connection broke */ }
        catch (Exception ex) { Console.WriteLine($"⚠️ Pipe Error ({name}): {ex.Message}"); }
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
            Console.WriteLine($"🔌 Client {_client.Client.RemoteEndPoint} has a problem.  Or something.");
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
        //Console.WriteLine("[SEND} Sending client configuration message 0x05/0x99");
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
        fetchResponse.Payouts = DatumServer.WinnersList;
        ulong mySats = fetchRequest.RewardValue - (DatumServer.WinnersList.Last().Value * (ulong)DatumServer.WinnersList.Count);
        //Console.WriteLine($"mySats {mySats}, team {DatumServer.WinnersList.Last().Value} * {DatumServer.WinnersList.Count}");
        ulong total = mySats + DatumServer.WinnersList.Last().Value * (ulong)DatumServer.WinnersList.Count;
        //Console.WriteLine($"total = {total}");
        //Console.WriteLine($"Reward= {fetchRequest.RewardValue}");
        if (total > fetchRequest.RewardValue) Console.WriteLine("Reward too big!!!");
        
        var myPayout = new PayoutInfo
        {
            Value = mySats,
            Address = clientPayoutAddress //this inserts the client's payout address into spot '0' in the coinbase TX
        };
        fetchResponse.Payouts = new List<PayoutInfo>(DatumServer.WinnersList);  //Insert my own "pools" payout address into the first slot. TODO:  Is this line redundent??  I think so. 
        fetchResponse.Payouts.Insert(0, myPayout);
        
        /*fetchResponse.Payouts.Add(new PayoutInfo
        {
            Value = fetchRequest.RewardValue,
            Address = "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y"
        });
        */

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
        //Check for proper address and usernames:
        //  using _poolConfig.PoolPayoutScript as default fallback
        string[] parts = powSubmit.Username.Split('.');
        string? validatedAddress = null;
        // 1. Check Priority: Address2 (Second part of username: address.address2.worker)
        if (parts.Length >= 2 && IsValidAddress(parts[1]))
        {
            validatedAddress = parts[1];
        }

        // 2. Check Priority: Address1 (First part of username: address.worker OR address)
        // Only check if we haven't found a valid address yet
        if (validatedAddress == null && parts.Length >= 1 && IsValidAddress(parts[0]))
        {
            validatedAddress = parts[0];
        }

        // 3. Fallback: Use Pool Default
        if (validatedAddress == null)
        {
            //Console.WriteLine($"[Warning] Invalid or missing address in username '{powSubmit.Username}'. Using default.");
            validatedAddress = clientPayoutAddress; 
        }

        powSubmit.Address = validatedAddress;

        if (powSubmit.PrevBlockHash == null)  //This is just a nonce update, does not include complete header info
        {

            JobCache[powSubmit.JobId].CoinbaseId = powSubmit.CoinbaseId;  
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
            if (powSubmit.SubsidyOnlyCoinb1 != null) //This share includes subsidy only coinbase data
            {
                JobCache[powSubmit.JobId].SubsidyOnlyCoinb1 = powSubmit.SubsidyOnlyCoinb1;
                JobCache[powSubmit.JobId].SubsidyOnlyCoinb2 = powSubmit.SubsidyOnlyCoinb2;
            }
            else if (!powSubmit.SubsidyOnly && powSubmit.CoinbasePairs[powSubmit.CoinbaseId].Coinb1 != null)  // Got a new coinbase with this one
            {
                //Console.WriteLine("New coinbase data");
                JobCache[powSubmit.JobId].CoinbasePairs[powSubmit.CoinbaseId] = powSubmit.CoinbasePairs[powSubmit.CoinbaseId];
            }
            powSubmit = JobCache[powSubmit.JobId];  //Copies back over the Merkle Branch info.
        }
        else JobCache[powSubmit.JobId] = powSubmit;  //New job, with complete header info.  
        //TODO: Technically, there is the very edge case that a miner could reuse old coinbase info with a new job and merkle branches.  This case isn't handled right now.

        

        // Now compute the latest Merkle Root.  We have to do this for every share submission, since the extranonce changes every time.
        byte[] Coinb1; 
        byte[] Coinb2;
        if (powSubmit.SubsidyOnly)
        {
            Coinb1 = powSubmit.SubsidyOnlyCoinb1;
            Coinb2 = powSubmit.SubsidyOnlyCoinb2;
        }
        else
        {
            Coinb1 = powSubmit.CoinbasePairs[powSubmit.CoinbaseId].Coinb1;
            Coinb2 = powSubmit.CoinbasePairs[powSubmit.CoinbaseId].Coinb2;
        }
        byte[] coinbaseTx = Coinb1.Concat(powSubmit.Extranonce).Concat(Coinb2).ToArray();

        if (powSubmit.QuickDiff)
        {
            //Console.WriteLine("   using quickdiff");
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
                if (!BitConverter.IsLittleEndian) Array.Reverse(magicBytes);   // pk_u16le writes LE

                Array.Copy(magicBytes, 0, coinbaseTx, quickDiffOffset, 2);
            }

            // ----- quickdiff target byte -----
            // The client uses the *quick* difficulty that the miner was asked for
            byte quickPot = FloorPoT(powSubmit.TargetByte);   // you already have this helper
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
            if (powSubmit.TargetByteIndex.HasValue)
            {
                int idx = powSubmit.TargetByteIndex.Value;
                if (idx >= 0 && idx < coinbaseTx.Length)
                {
                    coinbaseTx[idx] = powSubmit.TargetByte;
                }
                else
                    Console.WriteLine($"TargetByteIndex {idx} out of range (coinbase size {coinbaseTx.Length})");
            }
        }

        byte[] coinbaseHash = DoubleSha256(coinbaseTx);
        powSubmit.MerkleRoot = ComputeMerkleRoot(coinbaseHash, powSubmit.MerkleBranches, powSubmit.MerkleBranchCount.Value);
        JobCache[powSubmit.JobId].MerkleRoot = powSubmit.MerkleRoot; //For completeness, I guess.

        // Reconstruct block header
        byte[] header = new byte[80];
        using (var stream = new MemoryStream(header))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(powSubmit.Version); // 4 bytes
            writer.Write(powSubmit.PrevBlockHash); // 32 bytes
            writer.Write(powSubmit.MerkleRoot);
            writer.Write(powSubmit.NTime); // 4 bytes
            writer.Write(powSubmit.NBits); // 4 bytes
            writer.Write(powSubmit.Nonce); // 4 bytes
        }
        //if (powSubmit.SubsidyOnly) Console.WriteLine("*** Got subsidy only coinbase message!");

        // Verify header

        // Compute hash
        byte[] testHash = DoubleSha256(header);  //testHeader

        // Achieved difficulty (hash-based)
        BigInteger hashInt = 0;
        for (int i = testHash.Length - 1; i >= 0; i--)
        {
            hashInt = (hashInt << 8) | testHash[i];
        }
        BigInteger maxTarget = BigInteger.Pow(2, 224) - 1;
        BigInteger achievedDifficultyBig = hashInt == 0 ? 0 : maxTarget / hashInt;

        //Console.WriteLine($"   -> ✅ Received PoW submission: JobID={powSubmit.JobId}, CoinbaseID={powSubmit.CoinbaseId}, IsBlock={powSubmit.IsBlock}, SubsidyOnly={powSubmit.SubsidyOnly}, QuickDiff={powSubmit.QuickDiff}, Username={powSubmit.Username}");

        double difficulty = (double)achievedDifficultyBig;

        // Check against the global best record
        // This handles saving to disk and updating the UI automatically
        await DatumServer.UpdateBestShareIfNewRecord(difficulty, powSubmit.Username);
        if (difficulty > BestDiff)
        {
            //This share is the best we've seen from this client, so maybe update the client payout address if this share provided a new one.  
            clientPayoutAddress = powSubmit.Address;  //this stores the client's payout address once we get their first share, so we can stick this in the coinbaserFetch message
            BestDiff = difficulty;
            //This is sort of a hack for DATUM, to allow different stratum miners to "play".  Best share get's their address put in this client's coinbase[0]. 
            //The flow/states of clientPayoutAddress are:
            // Bootup: = _poolConfig.poolPayoutScript 
            // After first PoW share = miner address.  If 
        }

        // TODO: For testing only.  Remove this when finished. 
        if (difficulty > DatumServer.RESET_THRESHOLD)
        {
            // Reset the lists for the next round.  Record payouts.
            // Build the new lists and execute the swap in memory, with locks, like ZMQSubscriber
            //await BitcoinZmqSubscriber.OnNewBlockAsync("testBlock", stoppingToken);
            //await _hubContext.Clients.All.SendAsync("UpdateWinners", DatumServer.WinnersList, stoppingToken);
            //await _hubContext.Clients.All.SendAsync("UpdateOnDeck", DatumServer.OnDeckList, stoppingToken);

            // Reset BestDiff for this client, so new small miners can compete to control this client's payout address

            // Search the Winner's list, and add payouts to each miner's running total

            // Tally total payouts so far

            // Update percentages of payouts for each address
        }
        // Update client's share total using PoT.


        // 2. Verify Block Header
        bool isHeaderValid = VerifyBlockHeader(powSubmit.Version, powSubmit.NTime, powSubmit.Nonce, powSubmit.PrevBlockHash, powSubmit.MerkleRoot, powSubmit.NBits);
        //Console.WriteLine($"   -> Header valid: {isHeaderValid}");

        // 3. Extract Username
        //string minerAddress = powSubmit.Username;  //TODO: ideally we extract this from the coinbase transaction
        //Console.WriteLine($"   -> Miner address: {minerAddress}");

        // 4. Verify Coinbase Transaction
        var (isValidCoinbase, outputs) = VerifyCoinbaseTransaction(Coinb1, Coinb2, powSubmit.CoinbaseValue);
        //Console.WriteLine($"   -> Coinbase valid: {isValidCoinbase}");

        //Console.WriteLine($"POW: {powSubmit.JobId}\t{powSubmit.CoinbaseId}\t{difficulty}\t{powSubmit.Username}\n");

        //Evaluate how to credit this share, whether to add to on-deck or not
        // ... inside HandlePowSubmitAsync ...

        if (isHeaderValid)
        {
            if (true) // isValidCoinbase
            {
                // 🔒 LOCK STARTS HERE
                // Only one thread can enter this block at a time.
                bool newWinner = false;
                lock (DatumServer._OnDeckListLock) 
                {
                    int j = 0;                    
                    int listSizeBefore = DatumServer.OnDeckList.Count;
                    
                    // 1. Find insertion point
                    for (int i = 0; i < DatumServer.OnDeckList.Count; i++) 
                    {
                        if (DatumServer.OnDeckList[i].Difficulty < difficulty) break;
                        else j++;
                    }

                    // 2. Insert if qualified
                    if (j < _poolConfig.WinnersListSize)
                    {
                        PayoutInfo newPayout = new PayoutInfo();
                        newPayout.Address = powSubmit.Address; 
                        newPayout.Username = powSubmit.Username;
                        newPayout.Difficulty = difficulty;
                        newPayout.DiffString = FormatDifficulty(difficulty);
                        //newPayout.Value = 
                        
                        DatumServer.OnDeckList.Insert(j, newPayout);
                        newWinner = true;
                    }

                    // 3. Trim the list (Robust Fix)
                    // Changed 'if' to 'while' to auto-correct the list if it's already over-sized
                    while (DatumServer.OnDeckList.Count > _poolConfig.WinnersListSize)
                    {
                        DatumServer.OnDeckList.RemoveAt(DatumServer.OnDeckList.Count - 1);
                    }

                    int listSizeAfter = DatumServer.OnDeckList.Count;
                    if(true)
                    {
                        var reward = Program.BLOCK_REWARD / ((ulong)DatumServer.OnDeckList.Count + 1);
                        for (int i = 0; i < DatumServer.OnDeckList.Count; i++)
                        {
                            DatumServer.OnDeckList[i].Value = reward;
                        }
                    }

                    
                } 
                // 🔓 LOCK ENDS HERE
                // 4. Save State (Assuming this is fast/synchronous)
                if (newWinner)
                {
                    DatumServer.SaveState(); 
                    
                    // Console I/O can be slow, you might want to move this out of the lock
                    // or keep it if debugging is critical.
                    //Console.WriteLine("-----------------------------------------------------------");
                    for (int i = 0; i < DatumServer.OnDeckList.Count; i++)
                    {
                        //Console.WriteLine($"{i}\t{DatumServer.OnDeckList[i].DiffString}\t{DatumServer.OnDeckList[i].Address}");
                    }
                }
            }
        
        }

        
        //Console.WriteLine("-----------------------------------------");

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
        await DatumServer.HubContext.Clients.All.SendAsync("UpdateOnDeck", DatumServer.OnDeckList);
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
        // coinbaseHash is raw 32-byte little-endian from DoubleSha256
        byte[] current = coinbaseHash;

        for (int i = 0; i < count; i++)
        {
            // Extract branch (already little-endian, as sent by client)
            byte[] branch = merkleBranches.Skip(i * 32).Take(32).ToArray();

            // DO NOT REVERSE — keep little-endian
            byte[] combined = current.Concat(branch).ToArray();

            // Hash → output is little-endian
            current = DoubleSha256(combined);
        }

        return current; // little-endian Merkle root
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

    // Local function to validate an address candidate
    bool IsValidAddress(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        try
        {
            // Check format based on prefix
            if (candidate.StartsWith("bc1") || candidate.StartsWith("tb1"))
            {
                // Use your updated Bech32.Decode
                Bech32.Decode(candidate); 
                return true;
            }
            else
            {
                // Fallback to Base58Check (P2PKH/P2SH)
                // Basic length check to avoid costly math on obvious junk
                if (candidate.Length < 26 || candidate.Length > 35) return false;
                
                Base58Check.Decode(candidate);
                return true;
            }
        }
        catch (Exception)
        {
            // Address format is invalid (Format, Checksum, or Length exception)
            return false;
        }
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
            //Console.WriteLine($"🔓 Version: {Encoding.ASCII.GetString(msg.version)}");

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
            //Console.WriteLine($"🔓 Commit hash: {Encoding.ASCII.GetString(msg.commitHash, 0, commitIndex - 1)}");

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
                //Console.WriteLine($"🔓 Git tag: {Encoding.ASCII.GetString(tagBuffer, 0, tagIndex - 1)}");
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
            //Console.WriteLine($"🔓 XOR key (nk): 0x{nk:X8} at offset {stream.Position - xorKeyLength}");

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
            //Console.WriteLine($"🔓 Padding length: {paddingLength} bytes");

            // Step 9: Read signature (64 bytes)
            msg.cryptoSignBytes = new byte[cryptoSignBytes];
            if (stream.Position + cryptoSignBytes > data.Length)
            {
                Console.WriteLine($"❌ Insufficient bytes for signature at offset {stream.Position}");
                return (null, -1);
            }
            reader.Read(msg.cryptoSignBytes, 0, cryptoSignBytes);
            //Console.WriteLine($"🔓 Signature: {BitConverter.ToString(msg.cryptoSignBytes, 0, 16)}...");

            // Step 10: Handle cryptoBoxSealBytes (assuming placeholder or padding)
            msg.cryptoBoxSealBytes = new byte[0]; // Ignore for now, adjust if needed
            //Console.WriteLine($"🔓 Note: cryptoBoxSealBytes set to empty (adjust if needed)");

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
        
        // Write the count of payouts (1 byte)
        writer.Write((byte)Payouts.Count); 
        
        foreach (var payout in Payouts)
        {
            writer.Write(payout.Value); // 8 bytes (amount)
            
            // --- MODIFICATION: Extract the actual address part ---
            // Miners often provide addresses in the format "address.workerName".
            // We only need the part before the first dot for script generation.
            string address = payout.Address;
            int dotIndex = address.IndexOf('.');
            if (dotIndex != -1)
            {
                // Trim the string to include only the part before the dot
                address = address.Substring(0, dotIndex);
            }
            // --- END MODIFICATION ---

            byte[] script;
            
            // Check if the address is Bech32 (SegWit) using the cleaned 'address'
            if (address.StartsWith("bc1") || address.StartsWith("tb1"))
            {
                // Bech32 (SegWit) address
                var (hrp, version, program) = Bech32.Decode(address); // Use cleaned 'address'
                // --- START MODIFICATION: Support for P2TR (Taproot) ---
                if (version == 0 && program.Length != 20) // Only P2WPKH (20 bytes) is explicitly allowed here
                {
                    // This check is fine if you ONLY want to support P2WPKH for V0, but V0 also supports 32-byte P2WSH.
                    // It's better to check for supported versions and lengths.
                    if (version == 0 && program.Length != 32)
                    {
                        // P2WSH is V0, 32 bytes. If you don't support it, keep this check.
                        throw new InvalidOperationException($"Unsupported Bech32 address: P2WSH or invalid V0 length {payout.Address}");
                    }
                }
                else if (version == 1 && program.Length != 32)
                {
                    // This is a P2TR (Taproot) address with a weird length?
                    throw new InvalidOperationException($"Unsupported Bech32m address: invalid V0 length? {payout.Address}");
                }
                else if (version >= 2)
                {
                    // Unsupported Witness Version (V2 and up)
                    throw new InvalidOperationException($"Unsupported Bech32 address: Invalid version {version} for {payout.Address}");
                }
                // --- END MODIFICATION ---
                byte witnessVersionOpCode;
                switch (version)
                {
                    case 0:
                        witnessVersionOpCode = 0x00; // OP_0
                        break;
                    case 1:
                        witnessVersionOpCode = 0x51; // OP_1
                        break;
                    default:
                        // Use the payout.Address for the error message
                        throw new InvalidOperationException($"Unsupported witness version {version} for SegWit address: {payout.Address}");
                }
                // The script construction logic for SegWit addresses is identical for all versions (V0, V1, etc.)
                // It's: [version] [program_length] [program_bytes]
                script = new byte[2 + program.Length];
                script[0] = witnessVersionOpCode; // Use the decoded Witness version
                script[1] = (byte)program.Length; // Length (20 for V0 P2WPKH, 32 for V1 P2TR)
                Array.Copy(program, 0, script, 2, program.Length);
                if(version == 1)
                {
                    //Console.WriteLine($" script = {Convert.ToHexStringLower(script)}");
                }

            }
            else
            {
                // P2PKH address
                Console.WriteLine($"Decoding Base58 payout.Address: {payout.Address}");
                Console.WriteLine($"Decoding Base58 address: {address}");
                byte[] payload = Base58Check.Decode(address); // Use cleaned 'address'
                if (payload.Length != 21 || payload[0] != 0x00)
                {
                    // Use payout.Address for the error message to show the original input
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
    public ulong Value { get; set; }  //in Satoshis, or 1/100,000,000 BTC
    public string Address { get; set; } = string.Empty;
    public string Username {get; set; } = string.Empty;
    public double Difficulty { get; set; } = 0;
    public string DiffString { get; set; } = "0";
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
    public string Address {get; set; } = string.Empty;
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
        //Console.WriteLine($"POW share from: {result.Username}");
        
        string address = result.Username;
        int dotIndex = address.IndexOf('.');
        if (dotIndex != -1)
        {
            // Trim the string to include only the part before the dot
            address = address.Substring(0, dotIndex);
        }
        result.Address = address;
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
                
                if(result.CoinbaseId == 255)
                {
                    //Console.WriteLine($"result.CoinbaseID={result.CoinbaseId}");
                    //Console.WriteLine($"result.cb only = {result.SubsidyOnly}");
                    result.SubsidyOnlyCoinb1 = coinb1;
                    result.SubsidyOnlyCoinb2 = coinb2;
                }
                else
                {
                    result.CoinbasePairs[result.CoinbaseId] = (coinb1, coinb2);
                }
                
                //Console.WriteLine($"Stored CoinbaseId {result.CoinbaseId}: Coinb1={coinb1Len} bytes, Coinb2={coinb2Len} bytes");
            }
            else
            {
                throw new ArgumentException($"Unknown flag: 0x{flag:X2}");
            }
        }
        if(hasCoinbaseData ^ hasMerkleData)
        {
            //if (hasCoinbaseData) Console.WriteLine("*** Got coinbase without Merkle Data!!!");
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
