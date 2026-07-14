using Microsoft.AspNetCore.SignalR;
using NetMQ;
using NetMQ.Sockets;
using System.Text;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot_portal.HostedServices;

public class BitcoinZmqSubscriber : BackgroundService
{
    private SubscriberSocket? _subscriber;
    private const string HashBlockTopic = "hashblock";
    private const string RawBlockTopic = "rawblock";
    private readonly PoolConfig _poolConfig;
    private readonly ILogger<BitcoinZmqSubscriber> _logger;
    private readonly IHubContext<PoolStatsHub> _hubContext;
    private readonly BootProtocolStateService _stateService;

    public BitcoinZmqSubscriber(
        PoolConfig poolConfig,
        ILogger<BitcoinZmqSubscriber> logger,
        IHubContext<PoolStatsHub> hubContext,
        BootProtocolStateService stateService)
    {
        _poolConfig = poolConfig;
        _logger = logger;
        _hubContext = hubContext;
        _stateService = stateService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting ZMQ subscriber with Poller...");

        // The 'using' blocks ensure everything is disposed when the service stops
        using (_subscriber = new SubscriberSocket())
        using (var poller = new NetMQPoller { _subscriber }) // Add the socket to the poller
        {
            string hashBlockEndpoint = string.IsNullOrWhiteSpace(_poolConfig.BitcoinZmqEndpoint)
                ? "tcp://127.0.0.1:28332"
                : _poolConfig.BitcoinZmqEndpoint.Trim();
            string rawBlockEndpoint = _poolConfig.BitcoinZmqRawBlockEndpoint?.Trim() ?? string.Empty;
            _subscriber.Connect(hashBlockEndpoint);
            if (!string.IsNullOrWhiteSpace(rawBlockEndpoint) &&
                !string.Equals(rawBlockEndpoint, hashBlockEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                _subscriber.Connect(rawBlockEndpoint);
            }
            _subscriber.Subscribe(HashBlockTopic);
            if (!string.IsNullOrWhiteSpace(rawBlockEndpoint))
            {
                _subscriber.Subscribe(RawBlockTopic);
            }
            _logger.LogInformation(
                "Subscribed to Bitcoin ZMQ hashblock={HashBlockEndpoint}, rawblock={RawBlockEndpoint}",
                hashBlockEndpoint,
                string.IsNullOrWhiteSpace(rawBlockEndpoint) ? "disabled" : rawBlockEndpoint);

            // 3. Attach to the ReceiveReady event.
            // This event will fire on the Poller's dedicated thread when a message arrives.
            _subscriber.ReceiveReady += (s, e) =>
            {
                DateTime transportReceivedUtc = DateTime.UtcNow;
                try
                {
                    // We are inside the poller thread, so we can safely use
                    // the synchronous, non-blocking TryReceive... methods.
                    // We must read all 3 frames from the socket.
                    
                    // Use ReceiveFrameBytes and check for null (in case of a spurious event)
                    var topicBytes = e.Socket.ReceiveFrameBytes(out bool more);
                    if (!more || topicBytes == null) return; 
                    
                    var messageBytes = e.Socket.ReceiveFrameBytes(out more);
                    if (!more || messageBytes == null) return;
                    
                    var sequenceBytes = e.Socket.ReceiveFrameBytes(out more);
                    // We don't care about 'more' on the last frame
                    
                    // Now, process the message
                    var topic = Encoding.UTF8.GetString(topicBytes);

                    if (topic == HashBlockTopic && messageBytes.Length == 32)
                    {
                        var hashHex = BitcoinHashes.ToLikelyDisplayHashHex(messageBytes);
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
                    else if (topic == RawBlockTopic && messageBytes.Length >= 80)
                    {
                        string headerHex = Convert.ToHexString(messageBytes.AsSpan(0, 80)).ToLowerInvariant();
                        string hashHex = BitcoinHashes.ComputeBlockHashFromHeader(headerHex);
                        _logger.LogInformation("New raw block header detected: {HashHex}", hashHex);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                _stateService.ObserveLocalChainTipHeader(
                                    headerHex,
                                    "zmq-rawblock",
                                    transportReceivedUtc,
                                    blockHeight: null);
                                await OnNewBlockAsync(hashHex, stoppingToken);
                            }
                            catch (Exception taskEx)
                            {
                                _logger.LogError(taskEx, "Error processing raw block header {HashHex}", hashHex);
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

    public async Task OnNewBlockAsync(string blockHash, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing block {BlockHash}...", blockHash);
        await _stateService.ObserveChainTipAsync(blockHash, "zmq", null);
    }
}
