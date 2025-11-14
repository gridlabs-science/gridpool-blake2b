using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using NSec.Cryptography;

namespace boot_portal.HostedServices;

public class DatumServer: BackgroundService
{
    private readonly TcpListener _listener;

    // TODO: I should at least standardize the naming convention of these keys. Can they just be made readable to the client handler threads?
    private readonly Key _serverKey; // The server's long-term Ed25519 key.
    private readonly Key _serverXKey; //The server's long-term x25519 key.

    private PoolConfig _poolConfig;

    public static List<PayoutInfo> WinnersList { get; set; } = [];
    public static List<PayoutInfo> OnDeckList { get; } = [];

    public static IHubContext<PoolStatsHub> HubContext { get; private set; } = null!;
    private readonly ILogger<DatumServer> _logger;
    
    public DatumServer(IPAddress address, int port, Key serverKey, Key serverXKey, PoolConfig poolConfig, IHubContext<PoolStatsHub> hubContext, ILogger<DatumServer> logger)
    {
        _listener = new TcpListener(address, port);
        _serverKey = serverKey;
        _serverXKey = serverXKey;
        _poolConfig = poolConfig;
        HubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        _logger.LogInformation("\ud83d\ude80 DATUM Prime Server started on port {ListenerLocalEndpoint}. Waiting for connections...", _listener.LocalEndpoint.Serialize());
        
        if (false)
        {
            //TODO: If we can connect to a seed server, then get current Winners List data from them
        }
        else
        {
            //Otherwise, just load the _pool_config default solo address to start the WL with something
            //TODO: This Value *should* get overwritten right away, but I'm not sure in all cases.  Hardcoded current subsidy for now.
            // "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y"
            WinnersList.Add(new PayoutInfo
            {
                Value = Program.BLOCK_REWARD / 2,
                Address = _poolConfig.PoolPayoutScript
            });
        }
        
        var dummyShare = new PayoutInfo
        {
            Address = _poolConfig.PoolPayoutScript,
            Difficulty = 0 //Just initializing the first "share" at 0 diff, so the other comparison logic works.
        };
        OnDeckList.Add(dummyShare);  //insert the new share into the next winners list
        
        while (!stoppingToken.IsCancellationRequested)
        {
            // Asynchronously wait for a client to connect.
            var client = await _listener.AcceptTcpClientAsync(stoppingToken);
            _logger.LogInformation("\\n\ud83d\udd17 Client connected from {ClientRemoteEndPoint}", client.Client.RemoteEndPoint?.Serialize());
            
            // Create a handler for the new client.
            var clientHandler = new ClientHandler(client, _serverKey, _serverXKey, _poolConfig);

            // Run the client handler on a background thread so the server
            // can immediately go back to listening for more connections.
            _ = Task.Run(clientHandler.HandleClientAsync, stoppingToken);
        }
    }
}