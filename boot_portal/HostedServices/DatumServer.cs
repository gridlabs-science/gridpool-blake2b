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
    public static readonly object _OnDeckListLock = new object();
    public static BestShareRecord BestShare { get; set; } = new() { Difficulty = 0 };
    
    // Store the hex string for the UI
    public static string ServerPubKeyHex { get; private set; } = string.Empty;

    public static IHubContext<PoolStatsHub> HubContext { get; private set; } = null!;

    // Testing stuff:
    public static readonly int RESET_THRESHOLD = 1000000000;
    

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

    /*public static async void resetRound()
    {
        await BitcoinZmqSubscriber.OnNewBlockAsync("testBlock", stoppingToken);
    }*/

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        _logger.LogInformation("\ud83d\ude80 DATUM Prime Server started. Key: {KeyShort}...", ServerPubKeyHex.Substring(0, 16));
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(stoppingToken);
            var clientHandler = new ClientHandler(client, _serverKey, _serverXKey, _poolConfig, stoppingToken);
            _ = Task.Run(clientHandler.HandleClientAsync, stoppingToken);
        }
    }

    // ... inside DatumServer class ...

    /// <summary>
    /// Processes a share received via the HTTP API.
    /// Validates the PoW, updates state, saves to disk, and notifies UI.
    /// </summary>
    public static async Task<bool> ProcessApiShareAsync(Models.ShareSubmissionDto share)
    {
        // 1. VALIDATION LOGIC
        // TODO: Implement actual PoW verification here.
        // You need to reconstruct the Merkle Root from (Coinbase + MerklePath)
        // Then hash the HeaderHex (with that Root) and check against Target.
        
        // Pseudo-check for now:
        if (share.Difficulty < 1) return false; 
        
        // 2. UPDATE STATE
        bool isNewRecord = false;

        lock (_stateLock)
        {
            // A. Update OnDeckList (The queue for the next block)
            // Logic: Is this miner already on deck? If so, add diff. If not, insert.
            var existingEntry = OnDeckList.FirstOrDefault(x => x.Address == share.MinerAddress);
            if (existingEntry != null)
            {
                // This is a simplification. Usually you accumulate 'share.Difficulty'
                // based on pool weighting logic.
                // For a "Highest Difficulty Wins" pool, you replace if higher.
                if (share.Difficulty > existingEntry.Difficulty)
                {
                    existingEntry.Difficulty = share.Difficulty;
                }
            }
            else
            {
                OnDeckList.Add(new PayoutInfo 
                { 
                    Address = share.MinerAddress, 
                    Difficulty = share.Difficulty 
                    // Add 'Value' calculation here based on pool rules
                });
            }

            // B. Check for Best Share Record (For the UI)
            if (share.Difficulty > BestShare.Difficulty)
            {
                isNewRecord = true;
                BestShare = new BestShareRecord
                {
                    Difficulty = share.Difficulty,
                    MinerAddress = share.MinerAddress,
                    Timestamp = DateTime.UtcNow
                };
            }
            
            // C. Save to Disk
            // We call the existing SaveState method
            // (Note: SaveState takes a lock, so we might need to extract the logic 
            // inside SaveState to a private method '_saveStateInternal' to avoid recursive locking 
            // if we are already inside a lock here. 
            // OR: Just release lock before saving. Let's do the latter for safety).
        }
        
        // Save state outside the detailed logic lock, or rely on SaveState's internal lock.
        // Since SaveState has its own lock, we are safe to call it here.
        SaveState();

        // 3. BROADCAST TO UI
        if (HubContext != null)
        {
            // Send updated stats to web clients
            if (isNewRecord)
            {
                await HubContext.Clients.All.SendAsync("UpdateRecord", BestShare);
            }
            
            // Optional: Send a "New Share" blip to the UI
            // await HubContext.Clients.All.SendAsync("ShareReceived", share.MinerAddress);
        }

        return true;
    }
}