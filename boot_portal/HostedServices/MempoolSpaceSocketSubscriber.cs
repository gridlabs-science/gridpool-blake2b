using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using boot_portal.Services;

namespace boot_portal.HostedServices;

public class MempoolSpaceSocketSubscriber : BackgroundService
{
    private const string MEMPOOL_ENDPOINT = "wss://mempool.space/api/v1/ws";
    private readonly ILogger<MempoolSpaceSocketSubscriber> _logger;
    private readonly IHubContext<PoolStatsHub> _hubContext;
    private readonly BootProtocolStateService _stateService;

    public MempoolSpaceSocketSubscriber(
        ILogger<MempoolSpaceSocketSubscriber> logger,
        IHubContext<PoolStatsHub> hubContext,
        BootProtocolStateService stateService)
    {
        _logger = logger;
        _hubContext = hubContext;
        _stateService = stateService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting mempool.space WebSocket subscriber...");

        // This is the OUTER RECONNECT LOOP
        // It keeps running as long as the service is not stopped.
        while (!stoppingToken.IsCancellationRequested)
        {
            // Put the 'using' block INSIDE the loop.
            // This creates a fresh socket for each new connection attempt.
            using (var socket = new ClientWebSocket())
            try
            {
                // 1. CONNECT FIRST
                // This is the call that changes the state to WebSocketState.Open
                _logger.LogInformation("Connecting to {Endpoint}...", MEMPOOL_ENDPOINT);
                await socket.ConnectAsync(new Uri(MEMPOOL_ENDPOINT), stoppingToken);
                _logger.LogInformation("Connected to mempool.space WebSocket.");

                // 2. Subscribe to new blocks (after connecting)
                var subscribeMessage = "{\"action\": \"want\", \"data\": [\"blocks\"]}";
                var messageBytes = Encoding.UTF8.GetBytes(subscribeMessage);
                await socket.SendAsync(messageBytes, WebSocketMessageType.Text, true, stoppingToken);
                _logger.LogInformation("Subscribed to 'blocks' topic.");

                // 3. Start the INNER LISTENING LOOP
                // This loop runs as long as *this specific connection* is open.
                var buffer = new ArraySegment<byte>(new byte[4096]); // 4KB buffer
                while (socket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    // Use a MemoryStream to build the complete message from chunks
                    await using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(buffer, stoppingToken);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogWarning("WebSocket connection closed by remote host. Reconnecting...");
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                            break; // Break inner 'do' loop
                        }

                        // Write the received chunk to the memory stream
                        ms.Write(buffer.Array, buffer.Offset, result.Count);

                    } while (!result.EndOfMessage); // Loop until the full message is received

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break; // Break outer 'while (socket.State == Open)' loop to trigger reconnect
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(ms.ToArray());
                        await ProcessMessageAsync(message, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // This happens on a clean shutdown
                break; // Exit the outer 'while' loop
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error with mempool.space WebSocket. Reconnecting in 10s...");
            }

            // Wait 10 seconds before attempting to reconnect
            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10000, stoppingToken);
            }
        } // End of the outer 'while' loop

        _logger.LogInformation("Mempool.space WebSocket subscriber stopped.");
    }

    private async Task ProcessMessageAsync(string message, CancellationToken stoppingToken)
    {
        try
        {
            var jsonNode = JsonNode.Parse(message);

            var startupBlocks = ParseBlocks(jsonNode?["blocks"]);
            if (startupBlocks.Count > 0)
            {
                foreach (var block in SelectRelevantStartupBlocks(startupBlocks))
                {
                    _logger.LogInformation("New block from mempool.space backlog: {HashHex}", block.Hash);
                    await OnNewBlockAsync(block.Hash, block.Height, stoppingToken);
                }
            }

            (string? Hash, long? Height) liveBlock = ParseBlock(jsonNode?["block"]);
            if (!string.IsNullOrWhiteSpace(liveBlock.Hash))
            {
                _logger.LogInformation("New live block from mempool.space: {HashHex}", liveBlock.Hash);
                await OnNewBlockAsync(liveBlock.Hash, liveBlock.Height, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse mempool.space message: {Message}", message);
        }
    }

    // --- This is your logic, copied from BitcoinZmqSubscriber ---
    // Note: For a cleaner design, this logic should be moved to a
    // new, shared service (e.g., INewBlockProcessor) and injected
    // into BOTH of your subscriber classes.
    private async Task OnNewBlockAsync(string blockHash, long? blockHeight, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing block {BlockHash}...", blockHash);
        await _stateService.ObserveChainTipAsync(blockHash, "mempool.space", blockHeight);
    }

    private IReadOnlyList<(string Hash, long? Height)> SelectRelevantStartupBlocks(List<(string Hash, long? Height)> blocks)
    {
        if (blocks.Count == 0)
        {
            return [];
        }

        List<(string Hash, long? Height)> ordered = blocks
            .OrderBy(block => block.Height ?? long.MaxValue)
            .ThenBy(block => block.Hash, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string currentTip = _stateService.GetNetworkStatus().CurrentTipBlockHash ?? string.Empty;
        int currentIndex = ordered.FindIndex(block =>
            string.Equals(block.Hash, currentTip, StringComparison.OrdinalIgnoreCase));

        if (currentIndex >= 0)
        {
            return ordered
                .Skip(currentIndex + 1)
                .ToList();
        }

        // If we cannot anchor the backlog to our current tip, just take the newest block
        // instead of replaying an entire recent-history snapshot as if each one were newly found.
        return [ordered[^1]];
    }

    private static List<(string Hash, long? Height)> ParseBlocks(JsonNode? blocksNode)
    {
        var result = new List<(string Hash, long? Height)>();
        if (blocksNode is not JsonArray blocksArray)
        {
            return result;
        }

        foreach (JsonNode? blockNode in blocksArray)
        {
            (string? Hash, long? Height) parsed = ParseBlock(blockNode);
            string? hash = parsed.Hash;
            if (string.IsNullOrWhiteSpace(hash))
            {
                continue;
            }

            result.Add((hash, parsed.Height));
        }

        return result;
    }

    private static (string? Hash, long? Height) ParseBlock(JsonNode? blockNode)
    {
        return (
            blockNode?["id"]?.GetValue<string>(),
            blockNode?["height"]?.GetValue<long?>());
    }
}
