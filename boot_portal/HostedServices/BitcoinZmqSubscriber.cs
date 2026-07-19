using Microsoft.AspNetCore.SignalR;
using NetMQ;
using NetMQ.Sockets;
using System.Text;
using System.Threading.Channels;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot_portal.HostedServices;

public class BitcoinZmqSubscriber : BackgroundService
{
    private static readonly TimeSpan DuplicateNotificationWindow = TimeSpan.FromSeconds(30);
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
        Channel<BitcoinBlockNotification> notifications = Channel.CreateUnbounded<BitcoinBlockNotification>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
        Task processor = ProcessNotificationsAsync(notifications.Reader, stoppingToken);

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

                        if (!notifications.Writer.TryWrite(new BitcoinBlockNotification(
                            hashHex,
                            null,
                            transportReceivedUtc)))
                        {
                            _logger.LogWarning("Dropped ZMQ hashblock notification for {HashHex} during shutdown.", hashHex);
                        }
                    }
                    else if (topic == RawBlockTopic && messageBytes.Length >= 80)
                    {
                        string headerHex = Convert.ToHexString(messageBytes.AsSpan(0, 80)).ToLowerInvariant();
                        string hashHex = BitcoinHashes.ComputeBlockHashFromHeader(headerHex);
                        _logger.LogInformation("New raw block header detected: {HashHex}", hashHex);
                        if (!notifications.Writer.TryWrite(new BitcoinBlockNotification(
                            hashHex,
                            headerHex,
                            transportReceivedUtc)))
                        {
                            _logger.LogWarning("Dropped ZMQ rawblock notification for {HashHex} during shutdown.", hashHex);
                        }
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
                notifications.Writer.TryComplete();
                poller.Stop(); // This is thread-safe and unblocks Run()
            });

            // 2. Run the poller's *blocking* Run() method on a background thread
            //    and await its completion.
            _logger.LogInformation("Poller starting...");
            await Task.Run(() => poller.Run(), stoppingToken);
            notifications.Writer.TryComplete();
            await processor;
            
            // When poller.Stop() is called, poller.Run() will exit, 
            // the Task completes, and this await will unblock.

                
            }

        _logger.LogInformation("NetMQPoller stopped.");
    // 'using' blocks will dispose poller and socket here.
    }

    private async Task ProcessNotificationsAsync(
        ChannelReader<BitcoinBlockNotification> notifications,
        CancellationToken stoppingToken)
    {
        var recentBlocks = new RecentBitcoinBlockNotifications(DuplicateNotificationWindow);

        await foreach (BitcoinBlockNotification notification in notifications.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(notification.HeaderHex))
                {
                    _stateService.ObserveLocalChainTipHeader(
                        notification.HeaderHex,
                        "zmq-rawblock",
                        notification.TransportReceivedUtc,
                        blockHeight: null);
                }

                if (!recentBlocks.TryAccept(notification.BlockHash, notification.TransportReceivedUtc))
                {
                    _logger.LogDebug(
                        "Ignored duplicate ZMQ block notification for {BlockHash}.",
                        notification.BlockHash);
                    continue;
                }

                await OnNewBlockAsync(notification.BlockHash, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ZMQ block notification {BlockHash}", notification.BlockHash);
            }
        }
    }

    public async Task OnNewBlockAsync(string blockHash, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing block {BlockHash}...", blockHash);
        await _stateService.ObserveChainTipAsync(blockHash, "zmq", null);
    }
}

public sealed class RecentBitcoinBlockNotifications
{
    private readonly TimeSpan _retention;
    private readonly Dictionary<string, DateTime> _acceptedAt = new(StringComparer.OrdinalIgnoreCase);

    public RecentBitcoinBlockNotifications(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        _retention = retention;
    }

    public bool TryAccept(string blockHash, DateTime receivedUtc)
    {
        DateTime cutoffUtc = receivedUtc - _retention;
        foreach (string expiredHash in _acceptedAt
                     .Where(entry => entry.Value < cutoffUtc)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            _acceptedAt.Remove(expiredHash);
        }

        if (_acceptedAt.ContainsKey(blockHash))
        {
            return false;
        }

        _acceptedAt[blockHash] = receivedUtc;
        return true;
    }
}

internal sealed record BitcoinBlockNotification(
    string BlockHash,
    string? HeaderHex,
    DateTime TransportReceivedUtc);
