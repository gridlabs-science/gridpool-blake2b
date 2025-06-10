using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

// DATUM Prime Server implementation for handling DATUM Protocol messages
public class DatumPrimeServer
{
    // Define the DATUM Protocol header structure (matches T_DATUM_PROTOCOL_HEADER)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct DatumProtocolHeader
    {
        private uint _cmd_len_reserved_is_signed_encrypted_cmd; // Packed bitfield

        // Properties to access bitfields
        public uint CmdLen
        {
            get => _cmd_len_reserved_is_signed_encrypted_cmd & 0x3FFFFF; // 22 bits
            set => _cmd_len_reserved_is_signed_encrypted_cmd = (uint)(_cmd_len_reserved_is_signed_encrypted_cmd & ~0x3FFFFF) | (value & 0x3FFFFF); // TODO: not sure if this cast is in the right place
        }

        public byte Reserved
        {
            get => (byte)((_cmd_len_reserved_is_signed_encrypted_cmd >> 22) & 0x3); // 2 bits
            set => _cmd_len_reserved_is_signed_encrypted_cmd = (_cmd_len_reserved_is_signed_encrypted_cmd & ~(0x3U << 22)) | ((uint)(value & 0x3) << 22);
        }

        public bool IsSigned
        {
            get => ((_cmd_len_reserved_is_signed_encrypted_cmd >> 24) & 0x1) == 1; // 1 bit
            set => _cmd_len_reserved_is_signed_encrypted_cmd = (_cmd_len_reserved_is_signed_encrypted_cmd & ~(0x1U << 24)) | (value ? 1U << 24 : 0);
        }

        public bool IsEncryptedPubkey
        {
            get => ((_cmd_len_reserved_is_signed_encrypted_cmd >> 25) & 0x1) == 1; // 1 bit
            set => _cmd_len_reserved_is_signed_encrypted_cmd = (_cmd_len_reserved_is_signed_encrypted_cmd & ~(0x1U << 25)) | (value ? 1U << 25 : 0);
        }

        public bool IsEncryptedChannel
        {
            get => ((_cmd_len_reserved_is_signed_encrypted_cmd >> 26) & 0x1) == 1; // 1 bit
            set => _cmd_len_reserved_is_signed_encrypted_cmd = (_cmd_len_reserved_is_signed_encrypted_cmd & ~(0x1U << 26)) | (value ? 1U << 26 : 0);
        }

        public byte ProtoCmd
        {
            get => (byte)((_cmd_len_reserved_is_signed_encrypted_cmd >> 27) & 0x1F); // 5 bits
            set => _cmd_len_reserved_is_signed_encrypted_cmd = (_cmd_len_reserved_is_signed_encrypted_cmd & ~(0x1FU << 27)) | ((uint)(value & 0x1F) << 27);
        }
    }

    // Constants for protocol commands and subcommands
    private const byte PROTO_CMD_HELLO = 0x01;
    private const byte PROTO_CMD_HANDSHAKE_RESPONSE = 0x02;
    private const byte PROTO_CMD_MINING = 0x05;
    private const byte MINING_SUBCMD_COINBASER_FETCH = 0x10;
    private const byte MINING_SUBCMD_COINBASER_FETCH_RESPONSE = 0x11;
    private const byte MINING_SUBCMD_POW_SUBMIT = 0x27;
    private const byte MINING_SUBCMD_SHARE_RESPONSE = 0x8F;
    private const byte POW_SHARE_RESPONSE_ACCEPTED = 0x50;

    // Server configuration
    private const string DEFAULT_PAYOUT_ADDRESS = "mpuPt3FvAfwQFxd6BmPrwuRBbdMgmDSGfH";
    private const string COINBASE_TAG = "Boot protocol";
    private const string UNIQUE_ID = "21";
    private const uint MIN_DIFFICULTY = 1; // Example value, adjust as needed
    private const string MOTD = "Welcome to DATUM Prime Server!";

    private readonly ECDsa _serverKeyPair; // Server's static ECDSA key pair for signing
    private readonly ECDiffieHellman _serverDhKeyPair; // For deriving session keys
    private TcpListener _listener;

    public DatumPrimeServer(int port)
    {
        // Generate static ECDSA key pair for signing
        _serverKeyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Console.WriteLine("Generated ECDSA key pair for server.");

        // Generate ECDH key pair for session key derivation
        _serverDhKeyPair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Console.WriteLine("Generated ECDH key pair for session encryption.");

        // Export public key for DATUM Gateway config.json
        ExportPublicKey();

        // Start TCP listener
        _listener = new TcpListener(System.Net.IPAddress.Any, port);
    }

    // Export public key in PEM format for DATUM Gateway config.json
    private void ExportPublicKey()
    {
        var publicKeyParameters = _serverKeyPair.ExportSubjectPublicKeyInfo();
        string pem = "-----BEGIN PUBLIC KEY-----\n" +
                     Convert.ToBase64String(publicKeyParameters) +
                     "\n-----END PUBLIC KEY-----";
        Console.WriteLine("Server Public Key (add to DATUM Gateway config.json):\n" + pem);
    }

    public void Start()
    {
        _listener.Start();
        Console.WriteLine("Server listening on port...");

        while (true) {
            try
            {
                using TcpClient client = _listener.AcceptTcpClient();
                using NetworkStream stream = client.GetStream();
                Console.WriteLine("Client connected.");

                HandleClient(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
            }
        }

        _listener.Stop();
    }

    private void HandleClient(NetworkStream stream)
    {
        byte[] buffer = new byte[4 * 1024 * 1024]; // Max 4MB as per protocol
        while (true)
        {
            // Read header (4 bytes)
            int bytesRead = stream.Read(buffer, 0, 4);
            if (bytesRead < 4) break; // Connection closed or error

            // Deserialize header
            DatumProtocolHeader header = DeserializeStruct<DatumProtocolHeader>(buffer, 0);

            // Read payload based on cmdLen
            if (header.CmdLen > buffer.Length)
            {
                Console.WriteLine("Error: Payload too large.");
                break;
            }

            bytesRead = stream.Read(buffer, 0, (int)header.CmdLen);
            if (bytesRead < (int)header.CmdLen)
            {
                Console.WriteLine("Error: Incomplete payload.");
                break;
            }

            // Process message based on protoCmd
            switch (header.ProtoCmd)
            {
                case PROTO_CMD_HELLO:
                    HandleHello(stream, buffer, header);
                    break;
                case PROTO_CMD_MINING:
                    HandleMiningCommand(stream, buffer, header);
                    break;
                default:
                    Console.WriteLine($"Unknown proto_cmd: {header.ProtoCmd}");
                    break;
            }
        }
    }

    // Handle CLIENT hello (0x01) message
    private void HandleHello(NetworkStream stream, byte[] payload, DatumProtocolHeader header)
    {
        if (!header.IsEncryptedPubkey)
        {
            Console.WriteLine("Hello message not encrypted with server public key.");
            return;
        }

        //TODO: actually do this.  // Simulate decryption with server private key (simplified)
        // In practice, decrypt payload using _serverKeyPair private key
        Console.WriteLine("Received hello message. Assuming valid client public key and session key.");

        // Extract client public key and session key (placeholder; parse actual payload)
        //TODO: actually do this.
        byte[] clientPublicKey = new byte[33]; // Example size for compressed EC key
        byte[] clientSessionKey = new byte[33];
        Array.Copy(payload, 0, clientPublicKey, 0, 33);
        Array.Copy(payload, 33, clientSessionKey, 0, 33);

        // Generate pool-side session key
        var poolSessionKey = _serverDhKeyPair.PublicKey.ExportSubjectPublicKeyInfo();

        // Prepare handshake response (0x02)
        byte[] responsePayload = Encoding.UTF8.GetBytes(
            $"{Convert.ToBase64String(clientPublicKey)}|" +
            $"{Convert.ToBase64String(clientSessionKey)}|" +
            $"{Convert.ToBase64String(poolSessionKey)}|" +
            $"{COINBASE_TAG}|{UNIQUE_ID}|{MIN_DIFFICULTY}|{MOTD}");

        // Sign response
        byte[] signature = _serverKeyPair.SignData(responsePayload, HashAlgorithmName.SHA256);

        // Encrypt with client session key (simplified; use ECDH-derived shared secret)
        //TODO: actually do this.
        // For demo, assume payload is encrypted

        // Build header
        DatumProtocolHeader responseHeader = new DatumProtocolHeader
        {
            CmdLen = (uint)(responsePayload.Length + signature.Length),
            IsSigned = true,
            IsEncryptedChannel = true,
            ProtoCmd = PROTO_CMD_HANDSHAKE_RESPONSE
        };

        // SendMessage(stream, responsePayload, signature);
        //TODO: actually do this.
    }

    // Handle CLIENT mining commands (0x05)
    private void HandleMiningCommand(NetworkStream stream, byte[] payload, DatumProtocolHeader header)
    {
        // Extract subcommand (first byte of payload)
        byte subcommand = payload[0];

        switch (subcommand)
        {
            case MINING_SUBCMD_COINBASER_FETCH:
                HandleCoinbaserFetch(stream, payload);
                break;
            case MINING_SUBCMD_POW_SUBMIT:
                HandlePowSubmit(stream, payload);
                break;
            default:
                Console.WriteLine($"Unknown mining subcommand: {subcommand}");
                break;
        }
    }

    // Handle CLIENT coinbaser fetch (0x05, subcmd=0x10)
    private void HandleCoinbaserFetch(NetworkStream stream, byte[] payload)
    {
        // Extract block reward (simplified; assume uint64 at offset 1)
        ulong blockReward = BitConverter.ToUInt64(payload, 1);
        Console.WriteLine($"Received coinbaser fetch with block reward: {blockReward}");

        // Prepare response (0x05, subcmd=0x0x11)
        // Return single payout address (100% to default)
        string payoutList = $"1:{DEFAULT_PAYOUT_ADDRESS};100";
        byte[] responsePayload = Encoding.UTF8.GetBytes(payoutList);

        DatumProtocolHeader responseHeader = new DatumProtocolHeader
        {
            CmdLen = (uint)responsePayload.Length,
            IsSigned = false,
            IsEncryptedChannel = false, // As per protocol //TODO: Is this actually right?  Shouldn't it be encrypted?
            ProtoCmd = PROTO_CMD_MINING,
        };

        byte[] fullPayload = new byte[1 + responsePayload.Length];
        fullPayload[0] = MINING_SUBCMD_COINBASER_FETCH_RESPONSE;
        Array.Copy(responsePayload, 0, fullPayload, 1, responsePayload.Length);

        SendMessage(stream, fullPayload);
    }

    // Handle CLIENT proof-of-work submission (0x05, subcmd=0x27)
    private void HandlePowSubmit(NetworkStream stream, byte[] payload)
    {
        // Extract difficulty (simplified; assume uint32 at offset 1)
        uint difficulty = BitConverter.ToUInt32(payload, 1);
        Console.WriteLine($"Debug: Proof of work achieved difficulty: {difficulty}");

        // Prepare share response (0x05, subcmd=0x8F)
        byte[] responsePayload = new byte[] { POW_SHARE_RESPONSE_ACCEPTED };

        DatumProtocolHeader responseHeader = new DatumProtocolHeader
        {
            CmdLen = (uint)responsePayload.Length,
            IsSigned = false,
            IsEncryptedChannel = false,
            ProtoCmd = PROTO_CMD_MINING,
        };

        byte[] fullPayload = new byte[1 + responsePayload.Length];
        fullPayload[0] = MINING_SUBCMD_SHARE_RESPONSE;
        Array.Copy(responsePayload, 0, fullPayload, 1, responsePayload.Length);

        SendMessage(stream, fullPayload);
    }

    // Utility to send a message
    private void SendMessage(NetworkStream stream, byte[] payload, byte[] signature = null)
    {
        DatumProtocolHeader header = new DatumProtocolHeader
        {
            CmdLen = (uint)payload.Length,
            IsSigned = signature != null,
            IsEncryptedChannel = false, // Adjust based on encryption
            ProtoCmd = payload[0] == MINING_SUBCMD_COINBASER_FETCH_RESPONSE || 
                      payload[0] == MINING_SUBCMD_SHARE_RESPONSE ? PROTO_CMD_MINING : payload[0],
        };

        byte[] headerBytes = StructToByteArray(header);
        stream.Write(headerBytes, 0, headerBytes.Length);

        stream.Write(payload, 0, payload.Length);

        if (signature != null)
            stream.Write(signature, 0, signature.Length);
    }

    // Convert structure to byte array
    static byte[] StructToByteArray<T>(T structure) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] result = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structure, ptr, true);
            Marshal.Copy(ptr, result, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return result;
    }

    // Deserialize byte array to structure
    static T DeserializeStruct<T>(byte[] buffer, int offset) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(buffer, offset, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static void Main(string[] args)
    {
        try
        {
            DatumPrimeServer server = new DatumPrimeServer(8080); // Listen on port 8080
            server.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server failed: {ex.Message}");
        }
    }
}