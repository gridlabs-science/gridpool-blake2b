using NetMQ;
using NetMQ.Sockets;

// Define the same port as your subscriber uses
const string ZMQ_ENDPOINT = "tcp://127.0.0.1:28332";
const string TOPIC = "hashblock";

// Sample 32-byte block hash (64 hex characters)
var fakeBlockHashHex = "00000000000000000005a76387d8cc681d451a99520a7776b7e01f6002f2316e";
var fakeBlockHashBytes = Convert.FromHexString(fakeBlockHashHex);

Console.WriteLine($"Starting ZMQ Publisher on {ZMQ_ENDPOINT}. Press Enter to publish a new block.");

// Publisher socket uses Bind()
using (var publisher = new PublisherSocket())
{
    publisher.Bind(ZMQ_ENDPOINT);
    
    // Give the publisher time to bind and the subscriber time to connect
    Task.Delay(1000).Wait(); 

    while (true)
    {
        Console.ReadLine();

        // Frame 1: Topic (string)
        publisher.SendFrame(TOPIC); 
        // Frame 2: Block Hash (byte[])
        publisher.SendFrame(fakeBlockHashBytes); 
        
        Console.WriteLine($"Published message: Topic='{TOPIC}', Hash='{fakeBlockHashHex}'");
    }
}