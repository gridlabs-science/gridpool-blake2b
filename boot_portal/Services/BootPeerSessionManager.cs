using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
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
    public IPEndPoint EndPoint { get; init; } = new(IPAddress.None, 0);
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
    private const string BundleEncodingGzipBase64 = "gzip+base64+json";
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
        await RunSessionConnectLoopAsync(stoppingToken);
    }

    private async Task RunSessionConnectLoopAsync(CancellationToken stoppingToken)
    {
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

            await BroadcastNetworkStatusAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _poolConfig.PeerSessionConnectIntervalSeconds)), stoppingToken);
        }
    }

    public async Task RelayChainTipToConnectedSessionsAsync(
        BootChainTipAnnouncement announcement,
        CancellationToken cancellationToken)
    {
        if (_sessions.IsEmpty)
        {
            return;
        }

        var outbound = CloneChainTipAnnouncement(announcement);
        outbound.SenderEndpoint = _stateService.GetSelfEndpoint();
        outbound.SenderNodeId = _identity.NodeId;
        List<PeerSession> sessions = _sessions.Values.Where(session => session.IsOpen).ToList();
        await Parallel.ForEachAsync(
            sessions,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, _stateService.GetPeerRelayParallelism())
            },
            async (session, token) =>
            {
                DateTime sendStartedUtc = DateTime.UtcNow;
                bool sent = await TrySendPayloadAsync(
                    session,
                    new BootPeerSessionPayload
                    {
                        Type = "chain-tip",
                        SentUtc = sendStartedUtc,
                        ChainTip = outbound
                    },
                    token);
                RecordChainTipSendTelemetry(outbound, session.RemoteEndpoint, session.RemoteNodeId, "websocket", sendStartedUtc, sent);
            });
    }

    private async Task BroadcastNetworkStatusAsync(CancellationToken cancellationToken)
    {
        if (_sessions.IsEmpty)
        {
            return;
        }

        List<PeerSession> sessions = _sessions.Values.Where(session => session.IsOpen).ToList();
        foreach (PeerSession session in sessions)
        {
            await SendNetworkStatusAsync(session, cancellationToken);
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

        BootNodeVersionInfo localVersion = _stateService.GetLocalVersionInfo();
        var announcement = new PeerShareAnnouncement
        {
            SenderEndpoint = _stateService.GetSelfEndpoint(),
            ProtocolVersion = localVersion.ProtocolVersion,
            ConsensusVersion = localVersion.ConsensusVersion,
            StateBundleSchemaVersion = localVersion.StateBundleSchemaVersion,
            HttpApiVersion = localVersion.HttpApiVersion,
            PeerTransportVersion = localVersion.PeerTransportVersion,
            UdpRelayVersion = localVersion.UdpRelayVersion,
            ReleaseVersion = localVersion.ReleaseVersion,
            NetworkId = _poolConfig.BootNetworkId,
            ChainDomainFingerprint = localVersion.ChainDomainFingerprint,
            ProofClass = proof.ProofClass,
            RelayStage = proof.RelayStage,
            RelayTtl = proof.RelayTtl,
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
                session.RemoteUdpEndPoint == null)
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
                EndPoint = session.RemoteUdpEndPoint,
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
            BootNodeVersionInfo localVersion = _stateService.GetLocalVersionInfo();
            BootPeerSessionHello localHello = _identity.CreateHello(_poolConfig, localVersion, _stateService.GetSelfEndpoint());
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

            if (!_identity.ValidateHello(remoteHello, _poolConfig, localVersion, out string rejectionReason))
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
                localVersion,
                BootProtocolVersions.FromPeerHello(remoteHello),
                _poolConfig.BootNetworkId,
                remoteHello.NetworkId,
                requireStateBundleSchema: true);
            byte[] sharedSecret = _identity.ComputeSharedSecret(remoteHello);
            var crypto = BootPeerSessionCrypto.Create(
                sharedSecret,
                localIsInitiator ? localHello.Nonce : remoteHello.Nonce,
                localIsInitiator ? remoteHello.Nonce : localHello.Nonce,
                localIsInitiator,
                localHello.ChainDomainFingerprint);

            IPEndPoint? remoteUdpEndPoint = compatibility.UdpRelayCompatible
                ? await ResolveUdpEndPointAsync(remoteEndpoint, remoteHello.UdpHost, remoteHello.UdpPort, cancellationToken)
                : null;
            session = new PeerSession(
                GetSessionKey(remoteEndpoint, remoteHello.NodeId),
                remoteEndpoint,
                remoteHello.NodeId,
                compatibility.UdpRelayCompatible ? remoteHello.UdpHost : string.Empty,
                compatibility.UdpRelayCompatible ? remoteHello.UdpPort : 0,
                remoteUdpEndPoint,
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

            await SendNetworkStatusAsync(session, cancellationToken);
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
            DateTime transportReceivedUtc = DateTime.UtcNow;

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
                await HandlePayloadAsync(session, payload, frameBytes, transportReceivedUtc, cancellationToken);
            }
        }
    }

    private async Task HandlePayloadAsync(
        PeerSession session,
        BootPeerSessionPayload payload,
        int frameBytes,
        DateTime transportReceivedUtc,
        CancellationToken cancellationToken)
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
            case "network-status":
                await HandleNetworkStatusPayloadAsync(session, payload.NetworkStatus, cancellationToken);
                break;
            case "chain-tip":
                await HandleChainTipPayloadAsync(session, payload.ChainTip, frameBytes, transportReceivedUtc);
                break;
            case "state-request":
                await HandleStateRequestPayloadAsync(session, payload, cancellationToken);
                break;
            case "state-response":
                await HandleStateResponsePayloadAsync(session, payload, cancellationToken);
                break;
            case "state-not-found":
                _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-state-not-found");
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

    private Task<bool> SendNetworkStatusAsync(PeerSession session, CancellationToken cancellationToken)
    {
        return TrySendPayloadAsync(
            session,
            new BootPeerSessionPayload
            {
                Type = "network-status",
                NetworkStatus = _stateService.GetNetworkStatus()
            },
            cancellationToken);
    }

    private async Task HandleNetworkStatusPayloadAsync(
        PeerSession session,
        BootNetworkStatusDto? remote,
        CancellationToken cancellationToken)
    {
        if (remote == null)
        {
            return;
        }

        string remoteEndpoint = string.IsNullOrWhiteSpace(session.RemoteEndpoint)
            ? remote.SelfEndpoint
            : session.RemoteEndpoint;
        if (!string.IsNullOrWhiteSpace(remoteEndpoint))
        {
            _stateService.MergeDiscoveredPeers([remoteEndpoint]);
        }

        BootVersionCompatibilityDto compatibility = _stateService.EvaluatePeerCompatibility(remote);
        _stateService.UpdatePeerCompatibility(remoteEndpoint, compatibility, DateTime.UtcNow);
        _stateService.UpdatePeerNetworkSnapshot(
            string.IsNullOrWhiteSpace(remoteEndpoint) ? session.RemoteNodeId : remoteEndpoint,
            remote.CurrentStateId,
            remote.CandidateStateId,
            remote.CurrentTipBlockHash);
        if (!compatibility.CanSyncState)
        {
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-version-mismatch");
            session.Abort();
            return;
        }

        BootNetworkStatusDto local = _stateService.GetNetworkStatus();
        if (!string.IsNullOrWhiteSpace(remote.CurrentStateId) &&
            !string.Equals(remote.CurrentStateId, local.CurrentStateId, StringComparison.OrdinalIgnoreCase) &&
            ShouldRequestRemoteCurrentState(local, remote))
        {
            await RequestStateBundleAsync(session, remote.CurrentStateId, "current", cancellationToken);
            local = _stateService.GetNetworkStatus();
        }

        if (!string.IsNullOrWhiteSpace(remote.CandidateStateId) &&
            !string.Equals(remote.CandidateStateId, local.CandidateStateId, StringComparison.OrdinalIgnoreCase) &&
            remote.OnDeckTotalDifficulty > local.OnDeckTotalDifficulty)
        {
            await RequestStateBundleAsync(session, remote.CandidateStateId, "candidate", cancellationToken);
        }
    }

    private async Task HandleChainTipPayloadAsync(
        PeerSession session,
        BootChainTipAnnouncement? announcement,
        int frameBytes,
        DateTime transportReceivedUtc)
    {
        if (announcement == null)
        {
            return;
        }

        BootVersionCompatibilityDto compatibility = BootProtocolVersions.Evaluate(
            _stateService.GetLocalVersionInfo(),
            new BootNodeVersionInfo
            {
                ConsensusVersion = announcement.ConsensusVersion,
                ProtocolVersion = announcement.ProtocolVersion,
                PeerTransportVersion = announcement.PeerTransportVersion,
                ChainDomainFingerprint = announcement.ChainDomainFingerprint
            },
            _poolConfig.BootNetworkId,
            announcement.NetworkId,
            requireStateBundleSchema: false);
        if (!compatibility.NetworkCompatible ||
            !compatibility.ChainDomainCompatible ||
            !compatibility.ConsensusCompatible ||
            !compatibility.PeerTransportCompatible)
        {
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-tip-rejected");
            session.Abort();
            return;
        }

        string remoteEndpoint = string.IsNullOrWhiteSpace(session.RemoteEndpoint)
            ? announcement.SenderEndpoint
            : session.RemoteEndpoint;
        string remoteNodeId = string.IsNullOrWhiteSpace(session.RemoteNodeId)
            ? announcement.SenderNodeId
            : session.RemoteNodeId;
        _stateService.UpdatePeerSessionHeartbeat(remoteEndpoint, remoteNodeId, "session-chain-tip", DateTime.UtcNow);
        await _stateService.ObservePeerChainTipAsync(
            announcement,
            remoteEndpoint,
            remoteNodeId,
            "v2-session",
            frameBytes,
            transportReceivedUtc);
    }

    private Task<bool> RequestStateBundleAsync(
        PeerSession session,
        string stateId,
        string stateKind,
        CancellationToken cancellationToken)
    {
        return TrySendPayloadAsync(
            session,
            new BootPeerSessionPayload
            {
                Type = "state-request",
                StateId = stateId,
                StateKind = stateKind
            },
            cancellationToken);
    }

    private async Task HandleStateRequestPayloadAsync(
        PeerSession session,
        BootPeerSessionPayload payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.StateId))
        {
            return;
        }

        BootStateBundle? bundle = _stateService.GetStateBundle(payload.StateId);
        if (bundle == null)
        {
            await TrySendPayloadAsync(
                session,
                new BootPeerSessionPayload
                {
                    Type = "state-not-found",
                    MessageId = payload.MessageId,
                    StateId = payload.StateId,
                    StateKind = payload.StateKind
                },
                cancellationToken);
            return;
        }

        string json = JsonSerializer.Serialize(bundle, JsonOptions);
        string compressed = CompressToBase64(json);
        await TrySendPayloadAsync(
            session,
            new BootPeerSessionPayload
            {
                Type = "state-response",
                MessageId = payload.MessageId,
                StateId = bundle.StateId,
                StateKind = bundle.Kind,
                BundleEncoding = BundleEncodingGzipBase64,
                BundleData = compressed,
                BundleUncompressedBytes = Encoding.UTF8.GetByteCount(json)
            },
            cancellationToken);
        _stateService.UpdatePeerSessionHeartbeat(session.RemoteEndpoint, session.RemoteNodeId, "session-state-served", DateTime.UtcNow);
    }

    private async Task HandleStateResponsePayloadAsync(
        PeerSession session,
        BootPeerSessionPayload payload,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(payload.BundleEncoding, BundleEncodingGzipBase64, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(payload.BundleData))
        {
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-state-invalid");
            return;
        }

        BootStateBundle? bundle;
        try
        {
            string json = DecompressFromBase64(payload.BundleData);
            bundle = JsonSerializer.Deserialize<BootStateBundle>(json, JsonOptions);
        }
        catch (Exception)
        {
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-state-invalid");
            return;
        }

        if (bundle == null)
        {
            return;
        }

        string source = string.IsNullOrWhiteSpace(session.RemoteEndpoint)
            ? $"peer-session-node:{session.RemoteNodeId}"
            : $"peer-session:{session.RemoteEndpoint}";
        bool imported = string.Equals(bundle.Kind, "candidate", StringComparison.OrdinalIgnoreCase)
            ? await _stateService.TryImportCandidateStateAsync(bundle, source)
            : await ImportCurrentStateFromSessionAsync(bundle, source);

        _stateService.UpdatePeerSessionHeartbeat(
            session.RemoteEndpoint,
            session.RemoteNodeId,
            imported ? "session-state-imported" : "session-state-rejected",
            DateTime.UtcNow);
    }

    private async Task<bool> ImportCurrentStateFromSessionAsync(BootStateBundle bundle, string source)
    {
        BootNetworkStatusDto local = _stateService.GetNetworkStatus();
        bool bootstrapped = await _stateService.TryBootstrapCurrentStateAsync(
            bundle,
            bundle.LockedByBlockHash,
            bundle.LockedByBlockHeight,
            source);
        if (bootstrapped)
        {
            return true;
        }

        if (string.Equals(bundle.StateId, local.CurrentStateId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return await _stateService.TryAdoptCurrentStateAsync(
            bundle,
            bundle.LockedByBlockHash,
            bundle.LockedByBlockHeight,
            source);
    }

    private static string CompressToBase64(string json)
    {
        byte[] input = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(input, 0, input.Length);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static string DecompressFromBase64(string compressedBase64)
    {
        byte[] compressed = Convert.FromBase64String(compressedBase64);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static bool ShouldRequestRemoteCurrentState(BootNetworkStatusDto local, BootNetworkStatusDto remote)
    {
        if (local.WinnersCount == 0 || (local.WinnersCount == 1 && local.OnDeckCount == 0))
        {
            return true;
        }

        if (remote.CurrentRoundNumber > local.CurrentRoundNumber)
        {
            return true;
        }

        if (remote.CurrentRoundNumber < local.CurrentRoundNumber)
        {
            return false;
        }

        if (local.CurrentStateProofCount == 0 && remote.CurrentStateProofCount > 0)
        {
            return true;
        }

        const double epsilon = 0.0000001;
        if (remote.CurrentStateTotalDifficulty > local.CurrentStateTotalDifficulty + epsilon)
        {
            return true;
        }

        if (remote.CurrentStateTotalDifficulty + epsilon < local.CurrentStateTotalDifficulty)
        {
            return false;
        }

        return string.CompareOrdinal(remote.CurrentStateId ?? string.Empty, local.CurrentStateId ?? string.Empty) > 0;
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
            TransportReceivedUtc = DateTime.UtcNow,
            ProofClass = string.IsNullOrWhiteSpace(announcement.Share.ProofClass) ? announcement.ProofClass : announcement.Share.ProofClass,
            RelayStage = string.IsNullOrWhiteSpace(announcement.Share.RelayStage) ? announcement.RelayStage : announcement.Share.RelayStage,
            RelayTtl = announcement.Share.RelayTtl != 0 ? announcement.Share.RelayTtl : announcement.RelayTtl,
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
            TimeSpan timeout = TimeSpan.FromSeconds(Math.Max(1, _poolConfig.PeerSessionSendTimeoutSeconds));
            bool lockAcquired = await WaitForSendLockAsync(session.SendLock, timeout, cancellationToken);
            if (!lockAcquired)
            {
                throw new TimeoutException("Peer session send-lock wait timed out.");
            }
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                string payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                BootPeerSessionEncryptedFrame frame = session.Crypto.Encrypt(payloadJson, session.NextSendSequence++);
                string frameJson = JsonSerializer.Serialize(frame, JsonOptions);
                await SendStringAsync(session.Socket, frameJson, timeoutCts.Token);
                return true;
            }
            finally
            {
                session.SendLock.Release();
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timed out waiting for V2 peer-session send lock for {Peer}; closing the session so reliable HTTP fallback can proceed.", session.RemoteEndpoint);
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-send-timeout");
            RemoveSession(session);
            session.Abort();
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out sending V2 peer-session payload to {Peer}; closing the session so reliable HTTP fallback can proceed.", session.RemoteEndpoint);
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-send-timeout");
            RemoveSession(session);
            session.Abort();
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to send V2 peer-session payload to {Peer}.", session.RemoteEndpoint);
            _stateService.MarkPeerSessionFailure(session.RemoteEndpoint, session.RemoteNodeId, "session-error");
            RemoveSession(session);
            return false;
        }
    }

    public static Task<bool> WaitForSendLockAsync(
        SemaphoreSlim sendLock,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        sendLock.WaitAsync(timeout, cancellationToken);

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

    private async Task<IPEndPoint?> ResolveUdpEndPointAsync(
        string endpoint,
        string advertisedUdpHost,
        int advertisedUdpPort,
        CancellationToken cancellationToken)
    {
        int port = advertisedUdpPort is > 0 and <= 65535
            ? advertisedUdpPort
            : _poolConfig.PeerUdpPort;
        string host = advertisedUdpHost.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            if (!Uri.TryCreate(NormalizeEndpoint(endpoint), UriKind.Absolute, out Uri? uri) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                return null;
            }

            host = uri.Host;
        }

        if (IPAddress.TryParse(host, out IPAddress? literalAddress) && literalAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            return new IPEndPoint(literalAddress, port);
        }

        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            IPAddress? address = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork);
            return address == null ? null : new IPEndPoint(address, port);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogDebug(ex, "Unable to resolve UDP relay endpoint {Host}:{Port} for {Peer}.", host, port, endpoint);
            return null;
        }
    }

    private void RecordChainTipSendTelemetry(
        BootChainTipAnnouncement announcement,
        string remoteEndpoint,
        string remoteNodeId,
        string transport,
        DateTime sendStartedUtc,
        bool success)
    {
        _stateService.RecordExternalNetworkEvent(
            success ? "chain-tip-send-complete" : "chain-tip-send-failed",
            "local-chain-tip-relay",
            success ? "Chain-tip relay send completed." : "Chain-tip relay send failed.",
            announcement.BlockHash,
            announcement.BlockHeight,
            DateTime.UtcNow,
            transport,
            remoteEndpoint,
            remoteNodeId,
            announcement.RelayQueuedUtc == default ? null : announcement.RelayQueuedUtc,
            (DateTime.UtcNow - sendStartedUtc).TotalMilliseconds,
            0);
    }

    private static BootChainTipAnnouncement CloneChainTipAnnouncement(BootChainTipAnnouncement source) => new()
    {
        SenderEndpoint = source.SenderEndpoint,
        SenderNodeId = source.SenderNodeId,
        Source = source.Source,
        HeaderHex = source.HeaderHex,
        BlockHash = source.BlockHash,
        BlockHeight = source.BlockHeight,
        ObservedUtc = source.ObservedUtc,
        RelayQueuedUtc = source.RelayQueuedUtc,
        ProtocolVersion = source.ProtocolVersion,
        ConsensusVersion = source.ConsensusVersion,
        PeerTransportVersion = source.PeerTransportVersion,
        NetworkId = source.NetworkId,
        ChainDomainFingerprint = source.ChainDomainFingerprint
    };

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
            string remoteUdpHost,
            int remoteUdpPort,
            IPEndPoint? remoteUdpEndPoint,
            WebSocket socket,
            BootPeerSessionCrypto crypto)
        {
            Key = key;
            RemoteEndpoint = remoteEndpoint;
            RemoteNodeId = remoteNodeId;
            RemoteUdpHost = remoteUdpHost?.Trim() ?? string.Empty;
            RemoteUdpPort = remoteUdpPort is > 0 and <= 65535 ? remoteUdpPort : 0;
            RemoteUdpEndPoint = remoteUdpEndPoint;
            Socket = socket;
            Crypto = crypto;
        }

        public string Key { get; }
        public string RemoteEndpoint { get; }
        public string RemoteNodeId { get; }
        public string RemoteUdpHost { get; }
        public int RemoteUdpPort { get; }
        public IPEndPoint? RemoteUdpEndPoint { get; }
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
        private readonly byte[] _sendKey;
        private readonly byte[] _receiveKey;
        private readonly byte[] _sendNoncePrefix;
        private readonly byte[] _receiveNoncePrefix;
        private readonly byte[] _udpSendKey;
        private readonly byte[] _udpReceiveKey;
        private readonly byte[] _udpSendNoncePrefix;
        private readonly byte[] _udpReceiveNoncePrefix;
        private readonly byte[] _associatedData;
        private readonly byte[] _udpAssociatedData;

        private BootPeerSessionCrypto(
            byte[] sendKey,
            byte[] receiveKey,
            byte[] sendNoncePrefix,
            byte[] receiveNoncePrefix,
            byte[] udpSendKey,
            byte[] udpReceiveKey,
            byte[] udpSendNoncePrefix,
            byte[] udpReceiveNoncePrefix,
            byte[] associatedData,
            byte[] udpAssociatedData)
        {
            _sendKey = sendKey;
            _receiveKey = receiveKey;
            _sendNoncePrefix = sendNoncePrefix;
            _receiveNoncePrefix = receiveNoncePrefix;
            _udpSendKey = udpSendKey;
            _udpReceiveKey = udpReceiveKey;
            _udpSendNoncePrefix = udpSendNoncePrefix;
            _udpReceiveNoncePrefix = udpReceiveNoncePrefix;
            _associatedData = associatedData;
            _udpAssociatedData = udpAssociatedData;
        }

        public static BootPeerSessionCrypto Create(
            byte[] sharedSecret,
            string initiatorNonceBase64,
            string responderNonceBase64,
            bool localIsInitiator,
            string chainDomainFingerprint)
        {
            byte[] initiatorNonce = Convert.FromBase64String(initiatorNonceBase64);
            byte[] responderNonce = Convert.FromBase64String(responderNonceBase64);
            string domain = chainDomainFingerprint ?? string.Empty;
            byte[] initiatorToResponderKey = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "key:initiator-to-responder");
            byte[] responderToInitiatorKey = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "key:responder-to-initiator");
            byte[] initiatorToResponderPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "nonce:initiator-to-responder")[..4];
            byte[] responderToInitiatorPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "nonce:responder-to-initiator")[..4];
            byte[] udpInitiatorToResponderKey = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "udp-key:initiator-to-responder");
            byte[] udpResponderToInitiatorKey = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "udp-key:responder-to-initiator");
            byte[] udpInitiatorToResponderPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "udp-nonce:initiator-to-responder")[..4];
            byte[] udpResponderToInitiatorPrefix = Derive(sharedSecret, initiatorNonce, responderNonce, domain, "udp-nonce:responder-to-initiator")[..4];
            byte[] associatedData = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(domain)
                ? "GridPool peer session v2 encrypted frame"
                : $"GridPool peer session v3 encrypted frame\nchain_domain_fingerprint={domain}");
            byte[] udpAssociatedData = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(domain)
                ? "GridPool peer udp fast relay v3"
                : $"GridPool peer udp fast relay v6\nchain_domain_fingerprint={domain}");

            return localIsInitiator
                ? new BootPeerSessionCrypto(
                    initiatorToResponderKey,
                    responderToInitiatorKey,
                    initiatorToResponderPrefix,
                    responderToInitiatorPrefix,
                    udpInitiatorToResponderKey,
                    udpResponderToInitiatorKey,
                    udpInitiatorToResponderPrefix,
                    udpResponderToInitiatorPrefix,
                    associatedData,
                    udpAssociatedData)
                : new BootPeerSessionCrypto(
                    responderToInitiatorKey,
                    initiatorToResponderKey,
                    responderToInitiatorPrefix,
                    initiatorToResponderPrefix,
                    udpResponderToInitiatorKey,
                    udpInitiatorToResponderKey,
                    udpResponderToInitiatorPrefix,
                    udpInitiatorToResponderPrefix,
                    associatedData,
                    udpAssociatedData);
        }

        public BootPeerSessionEncryptedFrame Encrypt(string payloadJson, ulong sequence)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(payloadJson);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            using var aes = new AesGcm(_sendKey, tag.Length);
            aes.Encrypt(BuildNonce(_sendNoncePrefix, sequence), plaintext, ciphertext, tag, _associatedData);
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
            aes.Decrypt(BuildNonce(_receiveNoncePrefix, frame.Sequence), ciphertext, tag, plaintext, _associatedData);
            return Encoding.UTF8.GetString(plaintext);
        }

        public byte[] EncryptUdp(ReadOnlySpan<byte> payload, ulong sequence, ReadOnlySpan<byte> senderNodeKey)
        {
            byte[] ciphertext = new byte[payload.Length];
            byte[] tag = new byte[16];
            byte[] header = BuildUdpHeader(senderNodeKey, sequence);
            using var aes = new AesGcm(_udpSendKey, tag.Length);
            aes.Encrypt(BuildNonce(_udpSendNoncePrefix, sequence), payload, ciphertext, tag, header.Concat(_udpAssociatedData).ToArray());

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
                aes.Decrypt(BuildNonce(_udpReceiveNoncePrefix, sequence), ciphertext, tag, plaintext, header.ToArray().Concat(_udpAssociatedData).ToArray());
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

        private static byte[] Derive(
            byte[] sharedSecret,
            byte[] initiatorNonce,
            byte[] responderNonce,
            string chainDomainFingerprint,
            string label)
        {
            string prefix = string.IsNullOrEmpty(chainDomainFingerprint)
                ? "GridPool peer session v2\n"
                : $"GridPool peer session v3\nchain_domain_fingerprint={chainDomainFingerprint}\n";
            byte[] labelBytes = Encoding.UTF8.GetBytes(prefix + label);
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
