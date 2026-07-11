using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using boot_portal.Models;

namespace boot_portal.Services;

public sealed class BootNatPortMappingService
{
    private const int NatTraversalPort = 5351;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private readonly ILogger<BootNatPortMappingService> _logger;

    public BootNatPortMappingService(ILogger<BootNatPortMappingService> logger)
    {
        _logger = logger;
    }

    public async Task<BootPortMappingResponse> TryMapAsync(BootPortMappingRequest request, PoolConfig config, CancellationToken cancellationToken)
    {
        int peerTcpPort = NormalizePort(request.PeerTcpPort ?? config.PeerListenerPort);
        int peerUdpPort = NormalizePort(request.PeerUdpPort ?? config.PeerUdpPort);
        int lifetimeSeconds = Math.Clamp(request.LifetimeSeconds <= 0 ? 3600 : request.LifetimeSeconds, 120, 86_400);
        HashSet<string> protocols = NormalizeProtocols(request.Protocols);
        List<GatewayCandidate> gateways = GetGatewayCandidates();

        var response = new BootPortMappingResponse
        {
            AttemptedAtUtc = DateTime.UtcNow,
            LifetimeSeconds = lifetimeSeconds,
            PeerTcpPort = peerTcpPort,
            PeerUdpPort = peerUdpPort,
            GatewayCount = gateways.Count
        };

        if (peerTcpPort <= 0)
        {
            response.Warnings.Add("peer_listener_port is not configured; TCP peer port mapping was skipped.");
        }

        if (peerUdpPort <= 0)
        {
            response.Warnings.Add("peer_udp_port is not configured; UDP relay port mapping was skipped.");
        }

        if (gateways.Count == 0)
        {
            response.Warnings.Add("No IPv4 default gateway was discovered.");
            response.Summary = "No gateway available for PCP/NAT-PMP mapping.";
            return response;
        }

        foreach (GatewayCandidate gateway in gateways)
        {
            if (peerTcpPort > 0)
            {
                await TryMapTransportAsync(response, gateway, protocols, protocolNumber: 6, transport: "tcp", peerTcpPort, lifetimeSeconds, cancellationToken);
            }

            if (peerUdpPort > 0)
            {
                await TryMapTransportAsync(response, gateway, protocols, protocolNumber: 17, transport: "udp", peerUdpPort, lifetimeSeconds, cancellationToken);
            }
        }

        response.Summary = BuildSummary(response);
        return response;
    }

    private async Task TryMapTransportAsync(
        BootPortMappingResponse response,
        GatewayCandidate gateway,
        HashSet<string> protocols,
        byte protocolNumber,
        string transport,
        int port,
        int lifetimeSeconds,
        CancellationToken cancellationToken)
    {
        if (protocols.Contains("pcp"))
        {
            response.Results.Add(await TryPcpMapAsync(gateway, protocolNumber, transport, port, lifetimeSeconds, cancellationToken));
            if (response.Results.Last().Success)
            {
                return;
            }
        }

        if (protocols.Contains("nat-pmp"))
        {
            response.Results.Add(await TryNatPmpMapAsync(gateway, protocolNumber, transport, port, lifetimeSeconds, cancellationToken));
        }
    }

    private async Task<BootPortMappingResult> TryPcpMapAsync(
        GatewayCandidate gateway,
        byte protocolNumber,
        string transport,
        int port,
        int lifetimeSeconds,
        CancellationToken cancellationToken)
    {
        var result = CreateResult("pcp", gateway, transport, port, lifetimeSeconds);
        try
        {
            byte[] request = new byte[60];
            request[0] = 2;
            request[1] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4, 4), (uint)lifetimeSeconds);
            WriteIpv4MappedAddress(request.AsSpan(8, 16), gateway.LocalAddress);
            RandomNumberGenerator.Fill(request.AsSpan(24, 12));
            request[36] = protocolNumber;
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(40, 2), (ushort)port);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(42, 2), (ushort)port);

            byte[] response = await SendUdpRequestAsync(gateway.GatewayAddress, request, cancellationToken);
            if (response.Length < 24 || response[0] != 2 || response[1] != 0x81)
            {
                result.Message = "Invalid PCP response.";
                return result;
            }

            result.ResultCode = response[3];
            result.Success = response[3] == 0;
            result.LifetimeSeconds = (int)BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(4, 4));
            if (response.Length >= 44)
            {
                result.MappedExternalPort = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(42, 2));
            }
            if (response.Length >= 60)
            {
                result.ExternalAddress = FormatIpv4MappedAddress(response.AsSpan(44, 16));
            }
            result.Message = result.Success ? "PCP mapping accepted." : $"PCP mapping rejected with result code {result.ResultCode}.";
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            result.Message = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogDebug(ex, "PCP {Transport} mapping failed for {Gateway}:{Port}.", transport, gateway.GatewayAddress, port);
        }

        return result;
    }

    private async Task<BootPortMappingResult> TryNatPmpMapAsync(
        GatewayCandidate gateway,
        byte protocolNumber,
        string transport,
        int port,
        int lifetimeSeconds,
        CancellationToken cancellationToken)
    {
        var result = CreateResult("nat-pmp", gateway, transport, port, lifetimeSeconds);
        try
        {
            byte opcode = protocolNumber == 17 ? (byte)1 : (byte)2;
            byte[] request = new byte[12];
            request[0] = 0;
            request[1] = opcode;
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)port);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6, 2), (ushort)port);
            BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8, 4), (uint)lifetimeSeconds);

            byte[] response = await SendUdpRequestAsync(gateway.GatewayAddress, request, cancellationToken);
            if (response.Length < 16 || response[0] != 0 || response[1] != opcode + 128)
            {
                result.Message = "Invalid NAT-PMP response.";
                return result;
            }

            result.ResultCode = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
            result.Success = result.ResultCode == 0;
            result.MappedExternalPort = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(10, 2));
            result.LifetimeSeconds = (int)BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(12, 4));
            result.Message = result.Success ? "NAT-PMP mapping accepted." : $"NAT-PMP mapping rejected with result code {result.ResultCode}.";
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            result.Message = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogDebug(ex, "NAT-PMP {Transport} mapping failed for {Gateway}:{Port}.", transport, gateway.GatewayAddress, port);
        }

        return result;
    }

    private static async Task<byte[]> SendUdpRequestAsync(IPAddress gateway, byte[] payload, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Connect(gateway, NatTraversalPort);
        await udp.SendAsync(payload, payload.Length);
        UdpReceiveResult received = await udp.ReceiveAsync(cancellationToken).AsTask().WaitAsync(RequestTimeout, cancellationToken);
        return received.Buffer;
    }

    private static BootPortMappingResult CreateResult(string protocol, GatewayCandidate gateway, string transport, int port, int lifetimeSeconds)
    {
        return new BootPortMappingResult
        {
            Protocol = protocol,
            Transport = transport,
            Gateway = gateway.GatewayAddress.ToString(),
            LocalAddress = gateway.LocalAddress.ToString(),
            InternalPort = port,
            RequestedExternalPort = port,
            LifetimeSeconds = lifetimeSeconds
        };
    }

    private static List<GatewayCandidate> GetGatewayCandidates()
    {
        var candidates = new List<GatewayCandidate>();
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            IPAddress? localAddress = properties.UnicastAddresses
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            if (localAddress == null)
            {
                continue;
            }

            foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
            {
                if (gateway.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    candidates.Add(new GatewayCandidate(gateway.Address, localAddress));
                }
            }
        }

        return candidates
            .GroupBy(candidate => candidate.GatewayAddress.ToString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static int NormalizePort(int port) => port is > 0 and <= 65535 ? port : 0;

    private static HashSet<string> NormalizeProtocols(IEnumerable<string>? protocols)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string protocol in protocols ?? [])
        {
            string value = protocol.Trim().ToLowerInvariant();
            if (value is "pcp" or "nat-pmp")
            {
                normalized.Add(value);
            }
        }

        if (normalized.Count == 0)
        {
            normalized.Add("pcp");
            normalized.Add("nat-pmp");
        }

        return normalized;
    }

    private static void WriteIpv4MappedAddress(Span<byte> destination, IPAddress address)
    {
        destination.Clear();
        destination[10] = 0xff;
        destination[11] = 0xff;
        address.GetAddressBytes().CopyTo(destination[12..]);
    }

    private static string FormatIpv4MappedAddress(ReadOnlySpan<byte> source)
    {
        if (source.Length < 16)
        {
            return string.Empty;
        }

        return new IPAddress(source[12..16]).ToString();
    }

    private static string BuildSummary(BootPortMappingResponse response)
    {
        return $"TCP mapped={response.TcpMapped}; UDP mapped={response.UdpMapped}; attempts={response.Results.Count}; gateways={response.GatewayCount}.";
    }

    private sealed record GatewayCandidate(IPAddress GatewayAddress, IPAddress LocalAddress);
}
