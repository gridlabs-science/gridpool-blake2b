using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public sealed class BootPeerUdpDatagramTarget
{
    public string RemoteEndpoint { get; init; } = string.Empty;
    public string RemoteNodeId { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public byte[] Datagram { get; init; } = [];
}

public sealed class BootPeerUdpReceivedPayload
{
    public string RemoteEndpoint { get; init; } = string.Empty;
    public string RemoteNodeId { get; init; } = string.Empty;
    public byte[] Payload { get; init; } = [];
}

public sealed class BootPeerSessionManager : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sessionRegistrationLock = new();
    private readonly ConcurrentDictionary<string, PeerSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _dialTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly BootPeerIdentity _identity;
    private readonly ILogger<BootPeerSessionManager> _logger;

    public BootPeerSessionManager(
        PoolConfig poolConfig,
        BootProtocolStateService stateService,
        BootPeerIdentity identity,
        ILogger<BootPeerSessionManager> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _identity = identity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_poolConfig.EnablePeerSync || !_poolConfig.EnablePeerPersistentSessions)
        {
            _logger.LogInformation("V2 peer persistent sessions are disabled.");
            return;
        }

        _logger.LogInformation("V2 peer persistent sessions enabled. Node id: {NodeId}", ShortNodeId(_identity.NodeId));

        while (!stoppingToken.IsCancellationRequested)
        {
            CleanupDialTasks();

            foreach (string peer in _stateService.GetPeerEndpointsForPersistentSessions())
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                string endpoint = NormalizeEndpoint(peer);
                if (string.IsNullOrWhiteSpace(endpoint) ||
                    HasOpenSession(endpoint) ||
                    _dialTasks.ContainsKey(endpoint))
                {
                    continue;
                }

                _dialTasks[endpoint] = Task.Run(() => ConnectOutboundSessionAsync(endpoint, stoppingToken), CancellationToken.None);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _poolConfig.PeerSessionConnectIntervalSeconds)), stoppingToken);
        }
    }

    public async Task AcceptInboundSessionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        await RunSessionAsync(socket, dialedEndpoint: string.Empty, localIsInitiator: false, cancellationToken);
    }

    public async Task<HashSet<string>> RelayToConnectedSessionsAsync(
        BootShareProof proof,
        string? sourceEndpoint,
        string? sourceNodeId,
        CancellationToken cancellationToken)
    {
        var relayedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_poolConfig.EnablePeerPersistentSessions || _sessions.IsEmpty)
        {
            return relayedEndpoints;
        }

        string normalizedSource = NormalizeEndpoint(sourceEndpoint ?? string.Empty);
        string normalizedSourceNodeId = NormalizeNodeId(sourceNodeId);
        List<PeerSession> sessions = _sessions.Values
            .Where(session =>
                session.IsOpen &&
                (string.IsNullOrWhiteSpace(normalizedSource) ||
                 !string.Equals(session.RemoteEndpoint, normalizedSource, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(normalizedSourceNodeId) ||
                 !string.Equals(NormalizeNodeId(session.RemoteNodeId), normalizedSourceNodeId, StringComparison.Ordinal)))
            .ToList();
        if (sessions.Count == 0)
        {
            return relayedEndpoints;
        }

        var announcement = new PeerShareAnnouncement
        {
            SenderEndpoint = _stateService.GetSelfEndpoint(),
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            ConsensusVersion = _poolConfig.BootProtocolVersion,
            StateBundleSchemaVersion = BootProtocolVersions.StateBundleSchemaVersion,
            HttpApiVersion = BootProtocolVersions.HttpApiVersion,
            PeerTransportVersion = BootProtocolVersions.PeerTransportVersion,
            UdpRelayVersion = BootProtocolVersions.UdpRelayVersion,
            ReleaseVersion = BootProtocolVersions.Local(_poolConfig).ReleaseVersion,
            NetworkId = _poolConfig.BootNetworkId,
            Share = proof
        };

        await Parallel.ForEachAsync(
            sessions,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, _stateService.GetPeerRelayParallelism())
            },
            async (session, token) =>
            {
                bool sent = await TrySendPayloadAsync(
                    session,
                    new BootPeerSessionPayload
                    {
                        Type = "share",
                        Share = announcement
                    },
                    token);

                if (sent)
                {
                    _stateService.UpdatePeerSessionHeartbeat(
                        session.RemoteEndpoint,
                        session.RemoteNodeId,
                        "session-relayed",
                        DateTime.UtcNow);

                    if (!string.IsNullOrWhiteSpace(session.RemoteEndpoint))
                    {
                        lock (relayedEndpoints)
                        {
                            relayedEndpoints.Add(session.RemoteEndpoint);
                        }
                    }
                }
            });

        return relayedEndpoints;
    }

    public List<BootPeerUdpDatagramTarget> BuildUdpDatagramTargets(
        ReadOnlySpan<byte> payload,
        string? sourceEndpoint,
        int maxDatagramBytes)
    {
        if (!_poolConfig.EnablePeerPersistentSessions || _sessions.IsEmpty)
        {
            return [];
        }

        string normalizedSource = NormalizeEndpoint(sourceEndpoint ?? string.Empty);
        byte[] senderNodeKey = GetNodeKey(_identity.NodeId);
        var targets = new List<BootPeerUdpDatagramTarget>();
        foreach (PeerSession session in _sessions.Values)
        {
            if (!session.IsOpen ||
                string.IsNullOrWhiteSpace(session.RemoteEndpoint) ||
                string.Equals(session.RemoteEndpoint, normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                !TryBuildUdpHost(session.RemoteEndpoint, out string host, out int port))
            {
                continue;
            }

            ulong sequence = session.NextUdpSendSequence++;
            byte[] datagram = session.Crypto.EncryptUdp(payload, sequence, senderNodeKey);
            if (datagram.Length > maxDatagramBytes)
            {
                continue;
            }

            targets.Add(new BootPeerUdpDatagramTarget
            {
                RemoteEndpoint = session.RemoteEndpoint,
                RemoteNodeId = session.RemoteNodeId,
                Host = host,
                Port = port,
                Datagram = datagram
            });
        }

        return targets;
    }

    public bool TryDecryptUdpDatagram(
        ReadOnlySpan<byte> datagram,
        out BootPeerUdpReceivedPayload received,
        out string reason)
    {
        received = new BootPeerUdpReceivedPayload();
        reason = string.Empty;
        if (!_poolConfig.EnablePeerPersistentSessions)
        {
            reason = "sessions-disabled";
            return false;
        }

        if (!BootPeerSessionCrypto.TryReadUdpHeader(datagram, out byte[] senderNodeKey, out ulong sequence, out reason))
        {
            return false;
        }

        PeerSession? session = _sessions.Values.FirstOrDefault(candidate =>
            candidate.IsOpen &&
            GetNodeKey(candidate.RemoteNodeId).SequenceEqual(senderNodeKey));
        if (session == null)
        {
            reason = "unknown-session";
            return false;
        }

        if (!session.Crypto.TryDecryptUdp(datagram, sequence, out byte[] payload, out reason))
        {
            return false;
        }

        if (!session.AcceptUdpSequence(sequence, Math.Max(128, _poolConfig.PeerUdpReplayWindow)))
        {
            reason = "replay";
            return false;
        }

        received = new BootPeerUdpReceivedPayload
        {
            RemoteEndpoint = session.RemoteEndpoint,
            RemoteNodeId = session.RemoteNodeId,
            Payload = payload
        };
        return true;
    }

    private async Task ConnectOutboundSessionAsync(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(15, _poolConfig.PeerSessionIdleTimeoutSeconds / 2));
            await socket.ConnectAsync(BuildSessionUri(endpoint), cancellationToken);
            await RunSessionAsync(socket, endpoint, localIsInitiator: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "V2 peer session dial failed for {Peer}.", endpoint);
            _stateService.MarkPeerSessionFailure(endpoint, "session-error");
        }
        finally
        {
            _dialTasks.TryRemove(endpoint, out _);
        }
    }

    private async Task RunSessionAsync(
        WebSocket socket,
        string dialedEndpoint,
        bool localIsInitiator,
        CancellationToken cancellationToken)
    {
        PeerSession? session = null;
        string remoteEndpoint = NormalizeEndpoint(dialedEndpoint);
        try
        {
            BootPeerSessionHello localHello = _identity.CreateHello(_poolConfig, _stateService.GetSelfEndpoint());
            BootPeerSessionHello? remoteHello;
            if (localIsInitiator)
            {
                await SendPlainJsonAsync(socket, localHello, cancellationToken);
                remoteHello = await ReceivePlainJsonAsync<BootPeerSessionHello>(socket, cancellationToken);
            }
            else
            {
                remoteHello = await ReceivePlainJsonAsync<BootPeerSessionHello>(socket, cancellationToken);
                await SendPlainJsonAsync(socket, localHello, cancellationToken);
            }

            if (!_identity.ValidateHello(remoteHello, _poolConfig, out string rejectionReason))
            {
                _logger.LogDebug("Rejected V2 peer session hello from {Peer}: {Reason}", remoteEndpoint, rejectionReason);
                if (!string.IsNullOrWhiteSpace(remoteEndpoint))
                {
                    _stateService.MarkPeerSessionFailure(remoteEndpoint, remoteHello?.NodeId ?? string.Empty, "session-handshake-failed");
                }

                await CloseSocketAsync(socket, WebSocketCloseStatus.PolicyViolation, rejectionReason, cancellationToken);
                return;
            }

            remoteEndpoint = ResolveRemoteEndpoint(dialedEndpoint, remoteHello!.Endpoint);
            BootVersionCompatibilityDto compatibility = BootProtocolVersions.Evaluate(
                BootProtocolVersions.Local(_poolConfig),
                BootProtocolVersions.FromPeerHello(remoteHello),
                _poolConfig.BootNetworkId,
                remoteHello.NetworkId,
                requireStateBundleSchema: true);
            byte[] sharedSecret = _identity.ComputeSharedSecret(remoteHello);
            var crypto = BootPeerSessionCrypto.Create(
                sharedSecret,
                localIsInitiator ? localHello.Nonce : remoteHello.Nonce,
                localIsInitiator ? remoteHello.Nonce : localHello.Nonce,
                localIsInitiator);

            session = new PeerSession(
                GetSessionKey(remoteEndpoint, remoteHello.NodeId),
                remoteEndpoint,
                remoteHello.NodeId,
                socket,
                crypto);

            if (!TryRegisterSession(session))
            {
                _logger.LogDebug("Skipped duplicate V2 peer session for {Peer}.", remoteEndpoint);
                return;
            }

            _stateService.UpdatePeerSessionHeartbeat(remoteEndpoint, remoteHello.NodeId, "session-connected", DateTime.UtcNow);
            _stateService.UpdatePeerCompatibility(remoteEndpoint, compatibility, DateTime.UtcNow);

            _logger.LogInformation(
                "V2 peer session connected: {Endpoint} node={NodeId} initiator={Initiator}",
                string.IsNullOrWhiteSpace(remoteEndpoint) ? "(outbound-only)" : remoteEndpoint,
                ShortNodeId(remoteHello.NodeId),
                localIsInitiator);

            await TrySendPayloadAsync(
                session,
                new BootPeerSessionPayload
                {
                    Type = "address-book",
                    AddressBook = _stateService.GetPeerAddressBook(_poolConfig.PeerAddressGossipLimit)
                },
                cancellationToken);

            await SendPingAsync(session, cancellationToken);

            await ReceiveEncryptedLoopAsync(session, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "V2 peer session failed for {Peer}.", remoteEndpoint);
            if (!string.IsNullOrWhiteSpace(remoteEndpoint))
            {
                _stateService.MarkPeerSessionFailure(remoteEndpoint, session?.RemoteNodeId ?? string.Empty, "session-error");
            }
        }
        finally
        {
            if (session != null)
            {
                _stateService.UpdatePeerSessionClosed(
                    session.RemoteEndpoint,
                    session.RemoteNodeId,
                    "session-closed",
                    DateTime.UtcNow);
                RemoveSession(session);
            }

            await CloseSocketAsync(socket, WebSocketCloseStatus.NormalClosure, "session-ended", CancellationToken.None);
        }
    }

    private async Task ReceiveEncryptedLoopAsync(PeerSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && session.IsOpen)
        {
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idleCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, _poolConfig.PeerSessionIdleTimeoutSeconds)));

            string? frameJson;
            try
            {
                frameJson = await ReceiveStringAsync(session.Socket, idleCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await SendPingAsync(session, cancellationToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(frameJson))
            {
                return;
            }

            int frameBytes = Encoding.UTF8.GetByteCount(frameJson);
            BootPeerSessionEncryptedFrame? frame = JsonSerializer.Deserialize<BootPeerSessionEncryptedFrame>(frameJson, JsonOptions);
            if (frame == null || !string.Equals(frame.Type, "encrypted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid encrypted peer session frame.");
            }

            if (frame.Sequence != session.NextReceiveSequence)
            {
                throw new InvalidOperationException("Out-of-order peer session frame.");
            }

            string payloadJson = session.Crypto.Decrypt(frame);
            session.NextReceiveSequence++;

            BootPeerSessionPayload? payload = JsonSerializer.Deserialize<BootPeerSessionPayload>(payloadJson, JsonOptions);
            if (payload != null)
            {
                await HandlePayloadAsync(session, payload, frameBytes, cancellationToken);
            }
        }
    }

    private async Task HandlePayloadAsync(PeerSession session, BootPeerSessionPayload payload, int frameBytes, CancellationToken cancellationToken)
    {
        string type = payload.Type.Trim().ToLowerInvariant();
        switch (type)
        {
            case "share":
                await HandleSharePayloadAsync(session, payload.Share, frameBytes, cancellationToken);
                break;
            case "address-book":
                if (payload.AddressBook != null)
                {
                    _stateService.MergeDiscoveredPeers(payload.AddressBook.Peers.Select(peer => peer.Endpoint).Append(payload.AddressBook.SelfEndpoint));
                    _stateService.UpdatePeerSessionHeartbeat(session.RemoteEndpoint, session.RemoteNodeId, "session-gossip", DateTime.UtcNow);
                }
                break;
            case "ping":
                await TrySendPayloadAsync(
                    session,
                    new BootPeerSessionPayload
                    {
                        Type = "pong",
                        MessageId = payload.MessageId,
                        SentUtc = payload.SentUtc,
                        Text = payload.Text
                    },
                    cancellationToken);
                break;
            case "pong":
                double? latencyMs = CalculateRoundTripLatencyMs(payload.SentUtc);
                _stateService.UpdatePeerSessionHeartbeat(
                    session.RemoteEndpoint,
                    session.RemoteNodeId,
                    "session-connected",
                    DateTime.UtcNow,
                    latencyMs);
                break;
        }
    }

    private Task<bool> SendPingAsync(PeerSession session, CancellationToken cancellationToken)
    {
        DateTime sentUtc = DateTime.UtcNow;
        return TrySendPayloadAsync(
            session,
            new BootPeerSessionPayload
            {
                Type = "ping",
                SentUtc = sentUtc,
                Text = sentUtc.ToString("O")
            },
            cancellationToken);
    }

    private static double? CalculateRoundTripLatencyMs(DateTime sentUtc)
    {
        if (sentUtc == default)
        {
            return null;
        }

        DateTime normalizedSentUtc = sentUtc.Kind == DateTimeKind.Utc
            ? sentUtc
            : DateTime.SpecifyKind(sentUtc, DateTimeKind.Utc);
        double latencyMs = (DateTime.UtcNow - normalizedSentUtc).TotalMilliseconds;
        return latencyMs is >= 0 and <= 300000 ? latencyMs : null;
    }

    private async Task HandleSharePayloadAsync(
        PeerSession session,
        PeerShareAnnouncement? announcement,
        int frameBytes,
        CancellationToken cancellationToken)
    {
        if (announcement?.Share == null)
        {
            return;
        }

        BootVersionCompatibilityDto compatibility = _stateService.EvaluatePeerShareCompatibility(announcement);
        if (!compatibility.NetworkCompatible || !compatibility.ConsensusCompatible || !compatibility.HttpApiCompatible)
        {
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-rejected");
            _stateService.RecordExternalNetworkEvent(
                "peer-version-mismatch",
                string.IsNullOrWhiteSpace(session.RemoteEndpoint) ? session.RemoteNodeId : session.RemoteEndpoint,
                $"Rejected session share relay: {compatibility.Reason}.");
            return;
        }

        BootRequestValidationFailure? requestValidation = BootRequestGuards.ValidateSharePayload(
            _poolConfig,
            announcement.Share.MinerAddress,
            announcement.Share.HeaderHex,
            announcement.Share.CoinbaseHex,
            announcement.Share.MerklePath);
        if (requestValidation.HasValue)
        {
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-rejected");
            return;
        }

        string senderEndpoint = string.IsNullOrWhiteSpace(session.RemoteEndpoint)
            ? announcement.SenderEndpoint
            : session.RemoteEndpoint;

        if (!string.IsNullOrWhiteSpace(senderEndpoint))
        {
            _stateService.MergeDiscoveredPeers([senderEndpoint]);
        }
        _stateService.UpdatePeerSessionHeartbeat(senderEndpoint, session.RemoteNodeId, "session-share", DateTime.UtcNow);

        var result = await _stateService.SubmitShareAsync(new RecordedShareSubmission
        {
            MinerAddress = announcement.Share.MinerAddress,
            Username = string.IsNullOrWhiteSpace(announcement.Share.Username) ? string.Empty : announcement.Share.Username,
            HeaderHex = announcement.Share.HeaderHex,
            CoinbaseHex = announcement.Share.CoinbaseHex,
            MerklePath = announcement.Share.MerklePath,
            PrevBlockHash = announcement.Share.PrevBlockHash,
            Difficulty = announcement.Share.Difficulty,
            PayloadBytes = Math.Max(0, frameBytes),
            Source = string.IsNullOrWhiteSpace(senderEndpoint)
                ? $"peer-session-node:{session.RemoteNodeId}"
                : $"peer-session:{senderEndpoint}"
        }, "peer-block");

        if (!result.Accepted && !string.Equals(result.RejectionReason, "Duplicate share", StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Rejected V2 peer-session share from {SenderEndpoint}: {Reason}",
                senderEndpoint,
                result.RejectionReason);
        }
    }

    private async Task<bool> TrySendPayloadAsync(PeerSession session, BootPeerSessionPayload payload, CancellationToken cancellationToken)
    {
        if (!session.IsOpen)
        {
            return false;
        }

        try
        {
            await session.SendLock.WaitAsync(cancellationToken);
            try
            {
                string payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                BootPeerSessionEncryptedFrame frame = session.Crypto.Encrypt(payloadJson, session.NextSendSequence++);
                string frameJson = JsonSerializer.Serialize(frame, JsonOptions);
                await SendStringAsync(session.Socket, frameJson, cancellationToken);
                return true;
            }
            finally
            {
                session.SendLock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to send V2 peer-session payload to {Peer}.", session.RemoteEndpoint);
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-error");
            RemoveSession(session);
            return false;
        }
    }

    private bool TryRegisterSession(PeerSession session)
    {
        lock (_sessionRegistrationLock)
        {
            if (_sessions.TryGetValue(session.Key, out PeerSession? existing))
            {
                if (existing.IsOpen)
                {
                    session.Abort();
                    return false;
                }

                existing.Abort();
            }

            _sessions[session.Key] = session;
            return true;
        }
    }

    private void RemoveSession(PeerSession session)
    {
        if (_sessions.TryGetValue(session.Key, out PeerSession? current) && ReferenceEquals(current, session))
        {
            _sessions.TryRemove(session.Key, out _);
        }
    }

    private bool HasOpenSession(string endpoint)
    {
        string normalized = NormalizeEndpoint(endpoint);
        return _sessions.Values.Any(session =>
            session.IsOpen &&
            string.Equals(session.RemoteEndpoint, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void CleanupDialTasks()
    {
        foreach ((string endpoint, Task task) in _dialTasks.ToArray())
        {
            if (!task.IsCompleted)
            {
                continue;
            }

            _dialTasks.TryRemove(endpoint, out _);
            if (task.IsFaulted && task.Exception != null)
            {
                _logger.LogDebug(task.Exception.GetBaseException(), "V2 peer session task failed for {Peer}.", endpoint);
            }
        }
    }

    private string ResolveRemoteEndpoint(string dialedEndpoint, string? advertisedEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(advertisedEndpoint))
        {
            return _stateService.ResolvePeerEndpoint(dialedEndpoint, advertisedEndpoint);
        }

        return NormalizeEndpoint(dialedEndpoint);
    }

    private static string GetSessionKey(string remoteEndpoint, string nodeId)
    {
        return string.IsNullOrWhiteSpace(remoteEndpoint)
            ? $"node:{ShortNodeId(nodeId)}"
            : NormalizeEndpoint(remoteEndpoint);
    }

    private static Uri BuildSessionUri(string endpoint)
    {
        var endpointUri = new Uri(NormalizeEndpoint(endpoint), UriKind.Absolute);
        var builder = new UriBuilder(endpointUri)
        {
            Scheme = string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/api/peer/session",
            Query = string.Empty
        };
        return builder.Uri;
    }

    private bool TryBuildUdpHost(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = _poolConfig.PeerUdpPort;
        if (!Uri.TryCreate(NormalizeEndpoint(endpoint), UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        host = uri.Host;
        return true;
    }

    private async Task<T?> ReceivePlainJsonAsync<T>(WebSocket socket, CancellationToken cancellationToken)
    {
        string? json = await ReceiveStringAsync(socket, cancellationToken);
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static async Task SendPlainJsonAsync<T>(WebSocket socket, T value, CancellationToken cancellationToken)
    {
        await SendStringAsync(socket, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
    }

    private async Task<string?> ReceiveStringAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException("Peer session sent a non-text frame.");
            }

            stream.Write(buffer, 0, result.Count);
            if (stream.Length > Math.Max(4096, _poolConfig.PeerSessionMaxFrameBytes))
            {
                throw new InvalidOperationException("Peer session frame exceeds configured size limit.");
            }
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task SendStringAsync(WebSocket socket, string text, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private static async Task CloseSocketAsync(
        WebSocket socket,
        WebSocketCloseStatus closeStatus,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(closeStatus, description, cancellationToken);
            }
            catch (WebSocketException)
            {
                socket.Abort();
            }
        }
    }

    private static string NormalizeEndpoint(string? endpoint)
    {
        return string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.Trim().TrimEnd('/');
    }

    private static string NormalizeNodeId(string? nodeId)
    {
        return string.IsNullOrWhiteSpace(nodeId) ? string.Empty : nodeId.Trim();
    }

    private static string ShortNodeId(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return "unknown";
        }

        return nodeId.Length <= 12 ? nodeId : nodeId[..12];
    }

    private static byte[] GetNodeKey(string? nodeId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(nodeId ?? string.Empty));
        return hash[..16];
    }

    private sealed class PeerSession
    {
        public PeerSession(
            string key,
            string remoteEndpoint,
            string remoteNodeId,
            WebSocket socket,
            BootPeerSessionCrypto crypto)
        {
            Key = key;
            RemoteEndpoint = remoteEndpoint;
            RemoteNodeId = remoteNodeId;
            Socket = socket;
            Crypto = crypto;
        }

        public string Key { get; }
        public string RemoteEndpoint { get; }
        public string RemoteNodeId { get; }
        public WebSocket Socket { get; }
        public BootPeerSessionCrypto Crypto { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public ulong NextSendSequence { get; set; }
        public ulong NextReceiveSequence { get; set; }
        public ulong NextUdpSendSequence { get; set; }
        public bool IsOpen => Socket.State == WebSocketState.Open;
        private readonly object _udpReplayLock = new();
        private readonly HashSet<ulong> _udpReceivedSequences = [];
        private readonly Queue<ulong> _udpReceivedSequenceQueue = [];

        public void Abort()
        {
            try
            {
                Socket.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public bool AcceptUdpSequence(ulong sequence, int replayWindow)
        {
            lock (_udpReplayLock)
            {
                if (!_udpReceivedSequences.Add(sequence))
                {
                    return false;
                }

                _udpReceivedSequenceQueue.Enqueue(sequence);
                while (_udpReceivedSequenceQueue.Count > replayWindow &&
                       _udpReceivedSequenceQueue.TryDequeue(out ulong expired))
                {
                    _udpReceivedSequences.Remove(expired);
                }

                return true;
            }
        }
    }

    private sealed class BootPeerSessionCrypto
    {
        private static readonly byte[] UdpMagic = "GP3S"u8.ToArray();
        private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("GridPool peer session v2 encrypted frame");
        private static readonly byte[] UdpAssociatedData = Encoding.UTF8.GetBytes("GridPool peer udp fast relay v3");
        private readonly byte[] _sendKey;
        private readonly byte[] _receiveKey;
        private readonly byte[] _sendNoncePrefix;
        private readonly byte[] _receiveNoncePrefix;
        private readonly byte[] _udpSendKey;
        private readonly byte[] _udpReceiveKey;
        private readonly byte[] _udpSendNoncePrefix;
        private readonly byte[] _udpReceiveNoncePrefix;

        private BootPeerSessionCrypto(
            byte[] sendKey,
            byte[] receiveKey,
            byte[] sendNoncePrefix,
            byte[] receiveNoncePrefix,
            byte[] udpSendKey,
            byte[] udpReceiveKey,
            byte[] udpSendNoncePrefix,
            byte[] udpReceiveNoncePrefix)
        {
            _sendKey = sendKey;
            _receiveKey = receiveKey;
            _sendNoncePrefix = sendNoncePrefix;
            _receiveNoncePrefix = receiveNoncePrefix;
            _udpSendKey = udpSendKey;
            _udpReceiveKey = udpReceiveKey;
            _udpSendNoncePrefix = udpSendNoncePrefix;
            _udpReceiveNoncePrefix = udpReceiveNoncePrefix;
        }

        public static BootPeerSessionCrypto Create(
            byte[] sharedSecret,
            string initiatorNonceBase64,
            string responderNonceBase64,
            bool localIsInitiator)
        {
            byte[] initiatorNonce = Convert.FromBase64String(initiatorNonceBase64);
            byte[] responderNonce = Convert.FromBase64String(responderNonceBase64);
            byte[] initiatorToResponderKey = Derive(sharedSecret, initiatorNonce, responderNonce, "key:initiator-to-responder");
            byte[] responderToInitiatorKey = Derive(sharedSecret, initiatorNonce, responderNonce, "key:responder-to-initiator");
            byte[] initiatorToResponderPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, "nonce:initiator-to-responder")[..4];
            byte[] responderToInitiatorPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, "nonce:responder-to-initiator")[..4];
            byte[] udpInitiatorToResponderKey = Derive(sharedSecret, initiatorNonce, responderNonce, "udp-key:initiator-to-responder");
            byte[] udpResponderToInitiatorKey = Derive(sharedSecret, initiatorNonce, responderNonce, "udp-key:responder-to-initiator");
            byte[] udpInitiatorToResponderPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, "udp-nonce:initiator-to-responder")[..4];
            byte[] udpResponderToInitiatorPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, "udp-nonce:responder-to-initiator")[..4];

            return localIsInitiator
                ? new BootPeerSessionCrypto(
                    initiatorToResponderKey,
                    responderToInitiatorKey,
                    initiatorToResponderPrefix,
                    responderToInitiatorPrefix,
                    udpInitiatorToResponderKey,
                    udpResponderToInitiatorKey,
                    udpInitiatorToResponderPrefix,
                    udpResponderToInitiatorPrefix)
                : new BootPeerSessionCrypto(
                    responderToInitiatorKey,
                    initiatorToResponderKey,
                    responderToInitiatorPrefix,
                    initiatorToResponderPrefix,
                    udpResponderToInitiatorKey,
                    udpInitiatorToResponderKey,
                    udpResponderToInitiatorPrefix,
                    udpInitiatorToResponderPrefix);
        }

        public BootPeerSessionEncryptedFrame Encrypt(string payloadJson, ulong sequence)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(payloadJson);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            using var aes = new AesGcm(_sendKey, tag.Length);
            aes.Encrypt(BuildNonce(_sendNoncePrefix, sequence), plaintext, ciphertext, tag, AssociatedData);
            return new BootPeerSessionEncryptedFrame
            {
                Sequence = sequence,
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };
        }

        public string Decrypt(BootPeerSessionEncryptedFrame frame)
        {
            byte[] ciphertext = Convert.FromBase64String(frame.Ciphertext);
            byte[] tag = Convert.FromBase64String(frame.Tag);
            byte[] plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_receiveKey, tag.Length);
            aes.Decrypt(BuildNonce(_receiveNoncePrefix, frame.Sequence), ciphertext, tag, plaintext, AssociatedData);
            return Encoding.UTF8.GetString(plaintext);
        }

        public byte[] EncryptUdp(ReadOnlySpan<byte> payload, ulong sequence, ReadOnlySpan<byte> senderNodeKey)
        {
            byte[] ciphertext = new byte[payload.Length];
            byte[] tag = new byte[16];
            byte[] header = BuildUdpHeader(senderNodeKey, sequence);
            using var aes = new AesGcm(_udpSendKey, tag.Length);
            aes.Encrypt(BuildNonce(_udpSendNoncePrefix, sequence), payload, ciphertext, tag, header.Concat(UdpAssociatedData).ToArray());

            byte[] datagram = new byte[header.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(header, 0, datagram, 0, header.Length);
            Buffer.BlockCopy(ciphertext, 0, datagram, header.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, datagram, header.Length + ciphertext.Length, tag.Length);
            return datagram;
        }

        public bool TryDecryptUdp(ReadOnlySpan<byte> datagram, ulong sequence, out byte[] payload, out string reason)
        {
            payload = [];
            reason = string.Empty;
            if (datagram.Length < UdpHeaderLength + 16)
            {
                reason = "datagram-too-short";
                return false;
            }

            ReadOnlySpan<byte> header = datagram[..UdpHeaderLength];
            ReadOnlySpan<byte> ciphertext = datagram.Slice(UdpHeaderLength, datagram.Length - UdpHeaderLength - 16);
            ReadOnlySpan<byte> tag = datagram[^16..];
            byte[] plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(_udpReceiveKey, tag.Length);
                aes.Decrypt(BuildNonce(_udpReceiveNoncePrefix, sequence), ciphertext, tag, plaintext, header.ToArray().Concat(UdpAssociatedData).ToArray());
                payload = plaintext;
                return true;
            }
            catch (CryptographicException)
            {
                reason = "auth-failed";
                return false;
            }
        }

        public static bool TryReadUdpHeader(ReadOnlySpan<byte> datagram, out byte[] senderNodeKey, out ulong sequence, out string reason)
        {
            senderNodeKey = [];
            sequence = 0;
            reason = string.Empty;
            if (datagram.Length < UdpHeaderLength + 16)
            {
                reason = "datagram-too-short";
                return false;
            }

            if (!datagram[..4].SequenceEqual(UdpMagic))
            {
                reason = "bad-magic";
                return false;
            }

            if (datagram[4] != 1)
            {
                reason = "unsupported-version";
                return false;
            }

            senderNodeKey = datagram.Slice(5, 16).ToArray();
            sequence = BinaryPrimitives.ReadUInt64BigEndian(datagram.Slice(21, 8));
            return true;
        }

        private static byte[] Derive(byte[] sharedSecret, byte[] initiatorNonce, byte[] responderNonce, string label)
        {
            byte[] labelBytes = Encoding.UTF8.GetBytes("GridPool peer session v2\n" + label);
            byte[] material = new byte[labelBytes.Length + sharedSecret.Length + initiatorNonce.Length + responderNonce.Length];
            Buffer.BlockCopy(labelBytes, 0, material, 0, labelBytes.Length);
            Buffer.BlockCopy(sharedSecret, 0, material, labelBytes.Length, sharedSecret.Length);
            Buffer.BlockCopy(initiatorNonce, 0, material, labelBytes.Length + sharedSecret.Length, initiatorNonce.Length);
            Buffer.BlockCopy(responderNonce, 0, material, labelBytes.Length + sharedSecret.Length + initiatorNonce.Length, responderNonce.Length);
            return SHA256.HashData(material);
        }

        private static byte[] BuildNonce(byte[] prefix, ulong sequence)
        {
            var nonce = new byte[12];
            Buffer.BlockCopy(prefix, 0, nonce, 0, Math.Min(4, prefix.Length));
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), sequence);
            return nonce;
        }

        private const int UdpHeaderLength = 29;

        private static byte[] BuildUdpHeader(ReadOnlySpan<byte> senderNodeKey, ulong sequence)
        {
            if (senderNodeKey.Length != 16)
            {
                throw new ArgumentException("UDP sender node key must be 16 bytes.");
            }

            var header = new byte[UdpHeaderLength];
            Buffer.BlockCopy(UdpMagic, 0, header, 0, UdpMagic.Length);
            header[4] = 1;
            senderNodeKey.CopyTo(header.AsSpan(5, 16));
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(21, 8), sequence);
            return header;
        }
    }
}
