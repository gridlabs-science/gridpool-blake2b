using Microsoft.AspNetCore.SignalR;
using NetMQ;
using NetMQ.Sockets;

namespace boot_portal.HostedServices;

public class BitcoinZmqSubscriber: IHostedService
{
    private SubscriberSocket? _subscriber;
    private const string ZMQ_ENDPOINT = "tcp://127.0.0.1:28332"; // From bitcoin.conf
    private const string TOPIC = "hashblock"; // Subscribe to block hashes
    private readonly ILogger<BitcoinZmqSubscriber> _logger;
    private readonly IHubContext<PoolStatsHub> _hubContext;
    
    public BitcoinZmqSubscriber(ILogger<BitcoinZmqSubscriber> logger, IHubContext<PoolStatsHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriber = new SubscriberSocket();
        _subscriber.Connect(ZMQ_ENDPOINT);
        _subscriber.Subscribe(TOPIC); // Subscribe to the topic

        _logger.LogInformation("Subscribed to ZMQ at {ZmqEndpoint} for topic '{Topic}'", ZMQ_ENDPOINT, TOPIC);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Receive message (blocks until data arrives)
            var topic = _subscriber.ReceiveFrameString(); // e.g., "hashblock"
            var blockHash = _subscriber.ReceiveFrameBytes(); // 32-byte hash
            var sequenceBytes = _subscriber.ReceiveFrameBytes();

            if (topic == TOPIC && blockHash.Length == 32)
            {
                var hashHex = Convert.ToHexStringLower(blockHash);
                
                _logger.LogInformation("New block detected: {HashHex}", hashHex);
                
                // Trigger your server logic (e.g., update job templates, notify miners)
                await OnNewBlockAsync(hashHex);
            }
            else
            {
                _logger.LogInformation("Unexpected message: Topic={Topic}, Length={BlockHashLength}", topic, blockHash.Length);
            }
        }
    }

    private async Task OnNewBlockAsync(string blockHash)
    {
        // Your custom logic: e.g., fetch block details via RPC, update jobs
        _logger.LogInformation("Processing block {BlockHash}...", blockHash);
        // 1. Create the NEW list in a local variable.
        //    Reader threads CANNOT see this variable.
        //    They are all still happily reading the OLD static WinnersList.
        var newWinnersList = new List<PayoutInfo>();

        // 2. FULLY populate this new, private list.
        var reward = Program.BLOCK_REWARD / ((ulong)DatumServer.OnDeckList.Count + 1);
        foreach (var onDeckMiner in DatumServer.OnDeckList)
        {
            newWinnersList.Add(new PayoutInfo
            {
                Value = reward,
                Address = onDeckMiner.Address,
                Difficulty = onDeckMiner.Difficulty,
                DiffString = onDeckMiner.DiffString
            });
            var lastAdded = newWinnersList.Last();
            _logger.LogInformation("Last added value: {Value} - Address: {Address}", lastAdded.Value.ToString("N0"), lastAdded.Address);
            onDeckMiner.Difficulty = 0;
        }

        // 3. The "Swap". This is a single, instantaneous, atomic operation.
        //    All requests *after* this line will see the new list.
        //    All requests *before* this line saw the old list.
        //    No locks. No waiting.
        DatumServer.WinnersList = newWinnersList;

        // 4. Reset the OnDeckList for the next round.
        //DatumServer.OnDeckList = new List<PayoutInfo>();


        //Console.WriteLine($"{i}\t{DatumServer.WinnersList[i].Value}\t{DatumServer.WinnersList[i].Address}");
        //DatumServer.OnDeckList[i].Difficulty = 0;
        //Console.WriteLine($"{i}\t{DatumServer.OnDeckList[i].Difficulty}\t{DatumServer.OnDeckList[i].Address}");

        await _hubContext.Clients.All.SendAsync("UpdateWinners", DatumServer.WinnersList);
        await _hubContext.Clients.All.SendAsync("UpdateOnDeck", DatumServer.OnDeckList);
        Console.WriteLine("Broadcasted new lists to web UI.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscriber?.Dispose();
        
        return Task.CompletedTask;
    }
}