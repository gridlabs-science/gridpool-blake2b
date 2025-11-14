using Microsoft.AspNetCore.SignalR;
using NetMQ;
using NetMQ.Sockets;
using System.Text;

namespace boot_portal.HostedServices;

public class BitcoinZmqSubscriber : BackgroundService
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting ZMQ subscriber with Poller...");

        // The 'using' blocks ensure everything is disposed when the service stops
        using (_subscriber = new SubscriberSocket())
        using (var poller = new NetMQPoller { _subscriber }) // Add the socket to the poller
        {
            _subscriber.Connect(ZMQ_ENDPOINT);
            _subscriber.Subscribe(TOPIC);
            _logger.LogInformation("Subscribed to ZMQ at {ZmqEndpoint} for topic '{Topic}'", ZMQ_ENDPOINT, TOPIC);

            // 3. Attach to the ReceiveReady event.
            // This event will fire on the Poller's dedicated thread when a message arrives.
            _subscriber.ReceiveReady += (s, e) =>
            {
                try
                {
                    // We are inside the poller thread, so we can safely use
                    // the synchronous, non-blocking TryReceive... methods.
                    // We must read all 3 frames from the socket.
                    
                    // Use ReceiveFrameBytes and check for null (in case of a spurious event)
                    var topicBytes = e.Socket.ReceiveFrameBytes(out bool more);
                    if (!more || topicBytes == null) return; 
                    
                    var blockHash = e.Socket.ReceiveFrameBytes(out more);
                    if (!more || blockHash == null) return;
                    
                    var sequenceBytes = e.Socket.ReceiveFrameBytes(out more);
                    // We don't care about 'more' on the last frame
                    
                    // Now, process the message
                    var topic = Encoding.UTF8.GetString(topicBytes);

                    if (topic == TOPIC && blockHash.Length == 32)
                    {
                        var hashHex = Convert.ToHexStringLower(blockHash);
                        _logger.LogInformation("New block detected: {HashHex}", hashHex);

                        // *** CRITICAL ***
                        // We are on a synchronous poller thread. We CANNOT await OnNewBlockAsync.
                        // We must dispatch the async work to the thread pool.
                        // We "fire and forget" this task, logging any errors.
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // Pass the service's stoppingToken
                                await OnNewBlockAsync(hashHex, stoppingToken);
                            }
                            catch (Exception taskEx)
                            {
                                _logger.LogError(taskEx, "Error processing new block {HashHex}", hashHex);
                            }
                        }, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ZMQ ReceiveReady event handler");
                }
            };

            // *** THIS IS THE CORRECTED SECTION ***

            // 1. Register a callback to stop the poller when cancellation is requested.
            //    This is what breaks the poller.Run() loop.
            stoppingToken.Register(() =>
            {
                _logger.LogInformation("Cancellation requested, stopping poller...");
                poller.Stop(); // This is thread-safe and unblocks Run()
            });

            // 2. Run the poller's *blocking* Run() method on a background thread
            //    and await its completion.
            _logger.LogInformation("Poller starting...");
            await Task.Run(() => poller.Run(), stoppingToken);
            
            // When poller.Stop() is called, poller.Run() will exit, 
            // the Task completes, and this await will unblock.

                
            }

        _logger.LogInformation("NetMQPoller stopped.");
    // 'using' blocks will dispose poller and socket here.
    }

    private async Task OnNewBlockAsync(string blockHash, CancellationToken stoppingToken)
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
            _logger.LogInformation("Last added value: {Value} - Address: {Address}", lastAdded.Value.ToString("N0"),
                lastAdded.Address);
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

        await _hubContext.Clients.All.SendAsync("UpdateWinners", DatumServer.WinnersList, stoppingToken);
        await _hubContext.Clients.All.SendAsync("UpdateOnDeck", DatumServer.OnDeckList, stoppingToken);
        Console.WriteLine("Broadcasted new lists to web UI.");
    }
}