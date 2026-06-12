using System.Net;
using System.Net.Sockets;
using boot_portal.Models;

namespace boot_portal.Services;

public sealed class BootPeerUdpRelayService : BackgroundService
{
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly BootPeerSessionManager _sessionManager;
    private readonly ILogger<BootPeerUdpRelayService> _logger;
    private UdpClient? _udpClient;

    public BootPeerUdpRelayService(
        PoolConfig poolConfig,
        BootProtocolStateService stateService,
        BootPeerSessionManager sessionManager,
        ILogger<BootPeerUdpRelayService> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_poolConfig.EnablePeerSync ||
            !_poolConfig.EnablePeerPersistentSessions ||
            !_poolConfig.EnablePeerUdpFastRelay ||
            _poolConfig.PeerUdpBindPort <= 0)
        {
            _logger.LogInformation("V3 UDP fast relay is disabled.");
            return;
        }

        _udpClient = new UdpClient(AddressFamily.InterNetwork);
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _poolConfig.PeerUdpBindPort));
        _logger.LogInformation(
            "V3 UDP fast relay listening on UDP port {Port}, max datagram {MaxDatagramBytes} bytes.",
            _poolConfig.PeerUdpBindPort,
            _poolConfig.PeerUdpMaxDatagramBytes);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult received = await _udpClient.ReceiveAsync(stoppingToken);
                await HandleDatagramAsync(received.Buffer, received.RemoteEndPoint, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "V3 UDP fast relay listener stopped by socket error.");
        }
    }

    public async Task RelayShareAsync(BootShareProof proof, string? sourceEndpoint, CancellationToken cancellationToken)
    {
        if (!_poolConfig.EnablePeerUdpFastRelay || _udpClient == null)
        {
            return;
        }

        if (!BootPeerUdpShareCodec.TryEncode(proof, _poolConfig, out byte[] payload, out string reason))
        {
            _logger.LogDebug("Skipped V3 UDP relay for share {ShareId}: {Reason}", proof.ShareId, reason);
            return;
        }

        List<BootPeerUdpDatagramTarget> targets = _sessionManager.BuildUdpDatagramTargets(
            payload,
            sourceEndpoint,
            _poolConfig.PeerUdpMaxDatagramBytes);
        if (targets.Count == 0)
        {
            return;
        }

        foreach (BootPeerUdpDatagramTarget target in targets)
        {
            try
            {
                if (target.Datagram.Length > _poolConfig.PeerUdpMaxDatagramBytes)
                {
                    _stateService.UpdatePeerUdpHeartbeat(target.RemoteEndpoint, target.RemoteNodeId, "udp-too-large", success: false, DateTime.UtcNow);
                    continue;
                }

                await _udpClient.SendAsync(target.Datagram, target.Datagram.Length, target.Host, target.Port);
                _stateService.UpdatePeerUdpHeartbeat(target.RemoteEndpoint, target.RemoteNodeId, "udp-relayed", success: true, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "Failed V3 UDP relay to {Peer}.", target.RemoteEndpoint);
                _stateService.UpdatePeerUdpHeartbeat(target.RemoteEndpoint, target.RemoteNodeId, "udp-error", success: false, DateTime.UtcNow);
            }
        }
    }

    private async Task HandleDatagramAsync(byte[] datagram, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        if (datagram.Length > _poolConfig.PeerUdpMaxDatagramBytes)
        {
            _logger.LogDebug(
                "Rejected oversized V3 UDP datagram from {RemoteEndPoint}: {Bytes} bytes.",
                remoteEndPoint,
                datagram.Length);
            return;
        }

        if (!_sessionManager.TryDecryptUdpDatagram(datagram, out BootPeerUdpReceivedPayload received, out string decryptReason))
        {
            _logger.LogDebug("Ignored unauthenticated V3 UDP datagram from {RemoteEndPoint}: {Reason}", remoteEndPoint, decryptReason);
            return;
        }

        if (!BootPeerUdpShareCodec.TryDecode(received.Payload, _poolConfig, out RecordedShareSubmission share, out string decodeReason))
        {
            _logger.LogDebug("Rejected invalid V3 UDP share payload from {Peer}: {Reason}", received.RemoteEndpoint, decodeReason);
            _stateService.UpdatePeerUdpHeartbeat(received.RemoteEndpoint, received.RemoteNodeId, "udp-invalid", success: false, DateTime.UtcNow);
            return;
        }

        share.Source = string.IsNullOrWhiteSpace(received.RemoteEndpoint)
            ? "peer-udp"
            : $"peer-udp:{received.RemoteEndpoint}";
        share.PayloadBytes = datagram.Length;

        var result = await _stateService.SubmitShareAsync(share, "peer-block");
        if (result.Accepted || string.Equals(result.RejectionReason, "Duplicate share", StringComparison.Ordinal))
        {
            _stateService.UpdatePeerUdpHeartbeat(received.RemoteEndpoint, received.RemoteNodeId, "udp-share", success: true, DateTime.UtcNow);
            return;
        }

        _logger.LogDebug(
            "Rejected V3 UDP share from {Peer}: {Reason}",
            received.RemoteEndpoint,
            result.RejectionReason);
        _stateService.UpdatePeerUdpHeartbeat(received.RemoteEndpoint, received.RemoteNodeId, "udp-invalid", success: false, DateTime.UtcNow);
    }

    public override void Dispose()
    {
        _udpClient?.Dispose();
        base.Dispose();
    }
}
