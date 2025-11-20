using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NSec.Cryptography;

namespace boot_portal.HostedServices;

public class DatumServer : BackgroundService
{
    private readonly TcpListener _listener;
    private readonly Key _serverKey; 
    private readonly Key _serverXKey;
    private PoolConfig _poolConfig;
    private readonly ILogger<DatumServer> _logger;
    
    // Thread locking object to prevent file corruption
    private static readonly object _stateLock = new object();
    private const string StateFilePath = "pool_state.json";

    // State Data
    public static List<PayoutInfo> WinnersList { get; set; } = [];
    public static List<PayoutInfo> OnDeckList { get; set; } = [];
    public static BestShareRecord BestShare { get; set; } = new() { Difficulty = 0 };
    
    // Store the hex string for the UI
    public static string ServerPubKeyHex { get; private set; } = string.Empty;

    public static IHubContext<PoolStatsHub> HubContext { get; private set; } = null!;

    public DatumServer(IPAddress address, int port, Key serverKey, Key serverXKey, PoolConfig poolConfig, IHubContext<PoolStatsHub> hubContext, ILogger<DatumServer> logger)
    {
        _listener = new TcpListener(address, port);
        _serverKey = serverKey;
        _serverXKey = serverXKey;
        _poolConfig = poolConfig;
        HubContext = hubContext;
        _logger = logger;

        // Calculate the Hex Public Key once on startup for the UI
        GeneratePubKeyHex();
        
        // Load previous state from disk
        LoadState();
    }

    private void GeneratePubKeyHex()
    {
        var ed25519PubKeyBytes = _serverKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var x25519PubKeyBytes = _serverXKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var combinedPubKey = new byte[ed25519PubKeyBytes.Length + x25519PubKeyBytes.Length];
        Buffer.BlockCopy(ed25519PubKeyBytes, 0, combinedPubKey, 0, ed25519PubKeyBytes.Length);
        Buffer.BlockCopy(x25519PubKeyBytes, 0, combinedPubKey, ed25519PubKeyBytes.Length, x25519PubKeyBytes.Length);
        ServerPubKeyHex = Convert.ToHexString(combinedPubKey).ToLower();
    }

    private void LoadState()
    {
        lock (_stateLock)
        {
            if (File.Exists(StateFilePath))
            {
                try
                {
                    var json = File.ReadAllText(StateFilePath);
                    var state = JsonSerializer.Deserialize<PoolState>(json);
                    if (state != null)
                    {
                        WinnersList = state.WinnersList;
                        OnDeckList = state.OnDeckList;
                        BestShare = state.BestShare ?? new BestShareRecord();
                        _logger.LogInformation("✅ Loaded pool state from disk.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Failed to load state: {ex.Message}");
                }
            }
        }

        // Fallback: Initialize defaults if no file exists
        _logger.LogInformation("⚠️ No state file found. Initializing defaults.");
        WinnersList.Add(new PayoutInfo
        {
            Value = Program.BLOCK_REWARD / 2,
            Address = _poolConfig.PoolPayoutScript
        });
        OnDeckList.Add(new PayoutInfo
        {
            Address = _poolConfig.PoolPayoutScript,
            Difficulty = 0
        });
    }

    public static void SaveState()
    {
        lock (_stateLock)
        {
            try
            {
                var state = new PoolState
                {
                    WinnersList = WinnersList,
                    OnDeckList = OnDeckList,
                    BestShare = BestShare
                };
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StateFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving state: {ex.Message}");
            }
        }
    }

    // Call this from your ClientHandler when a share is submitted
    public static async Task UpdateBestShareIfNewRecord(double difficulty, string minerAddress)
    {
        if (difficulty > BestShare.Difficulty)
        {
            BestShare = new BestShareRecord
            {
                Difficulty = difficulty,
                MinerAddress = minerAddress,
                Timestamp = DateTime.UtcNow
            };
            
            // 1. Save to Disk
            SaveState();

            // 2. Broadcast to UI immediately
            await HubContext.Clients.All.SendAsync("UpdateRecord", BestShare);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        _logger.LogInformation("\ud83d\ude80 DATUM Prime Server started. Key: {KeyShort}...", ServerPubKeyHex.Substring(0, 16));
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(stoppingToken);
            var clientHandler = new ClientHandler(client, _serverKey, _serverXKey, _poolConfig);
            _ = Task.Run(clientHandler.HandleClientAsync, stoppingToken);
        }
    }
}