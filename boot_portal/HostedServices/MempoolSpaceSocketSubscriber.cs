using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes; // Add this for simple JSON parsing

namespace boot_portal.HostedServices;

public class MempoolSpaceSocketSubscriber : BackgroundService
{
    private const string MEMPOOL_ENDPOINT = "wss://mempool.space/api/v1/ws";
    private readonly ILogger<MempoolSpaceSocketSubscriber> _logger;
    private readonly IHubContext<PoolStatsHub> _hubContext;

    public MempoolSpaceSocketSubscriber(ILogger<MempoolSpaceSocketSubscriber> logger, IHubContext<PoolStatsHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
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
                        // Now we have the full message, convert it
                        var message = Encoding.UTF8.GetString(ms.ToArray());
                        ProcessMessage(message, stoppingToken);
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

    private void ProcessMessage(string message, CancellationToken stoppingToken)
    {
        try
        {
            // Use System.Text.Json.Nodes for easy, dynamic parsing
            var jsonNode = JsonNode.Parse(message);

            // ** CORRECTED LOGIC **
            // Check if the "blocks" key exists and is an array
            if (jsonNode?["blocks"] is JsonArray blocksArray)
            {
                // Iterate over each block in the array
                // (Usually, it's just one, but this is safer)
                foreach (var blockNode in blocksArray)
                {
                    string blockHash = blockNode?["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(blockHash))
                    {
                        _logger.LogInformation("New block from mempool.space: {HashHex}", blockHash);
                        
                        // Dispatch the async work to the thread pool
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await OnNewBlockAsync(blockHash, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing new block {HashHex}", blockHash);
                            }
                        }, stoppingToken);
                    }
                }
            }
            // You can add 'else if' here to handle other message types, like pings
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
    private async Task OnNewBlockAsync(string blockHash, CancellationToken stoppingToken)
    {
        // Your custom logic: e.g., fetch block details via RPC, update jobs
        _logger.LogInformation("Processing block {BlockHash}...", blockHash);
        return;
        // 1. Create the NEW list in a local variable.
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
            //_logger.LogInformation("Last added value: {Value} - Address: {Address}", lastAdded.Value.ToString("N0"), lastAdded.Address);
            onDeckMiner.Difficulty = 0;
        }

        // 3. The "Swap".
        DatumServer.WinnersList = newWinnersList;

        // 4. (Your comment noted resetting this, so I've left it commented)
        //DatumServer.OnDeckList = new List<PayoutInfo>();
        DatumServer.SaveState();

        await _hubContext.Clients.All.SendAsync("UpdateWinners", DatumServer.WinnersList, stoppingToken);
        await _hubContext.Clients.All.SendAsync("UpdateOnDeck", DatumServer.OnDeckList, stoppingToken);
        //Console.WriteLine("Broadcasted new lists to web UI.");
    }
}