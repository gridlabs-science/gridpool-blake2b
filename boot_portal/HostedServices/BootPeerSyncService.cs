using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using boot_portal.Controllers;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot_portal.HostedServices;

public class BootPeerSyncService : BackgroundService
{
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly BootPeerSessionManager _sessionManager;
    private readonly BootPeerUdpRelayService _udpRelayService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BootPeerSyncService> _logger;

    public BootPeerSyncService(
        PoolConfig poolConfig,
        BootProtocolStateService stateService,
        BootPeerSessionManager sessionManager,
        BootPeerUdpRelayService udpRelayService,
        IHttpClientFactory httpClientFactory,
        ILogger<BootPeerSyncService> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _sessionManager = sessionManager;
        _udpRelayService = udpRelayService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_poolConfig.EnablePeerSync)
        {
            _logger.LogInformation("Peer sync is disabled.");
            return;
        }

        _stateService.SeedPeers(_poolConfig.BootstrapPeers);
        if (string.IsNullOrWhiteSpace(_poolConfig.PublicBaseUrl))
        {
            _logger.LogInformation("Peer sync is enabled without public_base_url. This node will sync outbound but will not advertise itself as a reachable peer.");
        }

        Task syncLoop = RunPeerSyncLoopAsync(stoppingToken);
        Task relayLoop = RunShareRelayLoopAsync(stoppingToken);
        await Task.WhenAll(syncLoop, relayLoop);
    }

    private async Task RunPeerSyncLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            List<string> peers = _stateService.GetPeerEndpoints();
            foreach (string peer in peers)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await PollPeerAsync(peer, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Peer poll timed out for {Peer}.", peer);
                    _stateService.MarkPeerFailure(peer, "timeout");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Peer poll failed for {Peer}.", peer);
                    _stateService.MarkPeerFailure(peer, "error");
                }
            }

            PruneStalePeers();

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _poolConfig.PeerSyncIntervalSeconds)), stoppingToken);
        }
    }

    private void PruneStalePeers()
    {
        if (_poolConfig.PeerPruneAfterSeconds <= 0)
        {
            return;
        }

        int pruned = _stateService.PruneStalePeers(
            DateTime.UtcNow,
            TimeSpan.FromSeconds(_poolConfig.PeerPruneAfterSeconds),
            Math.Max(1, _poolConfig.PeerPruneFailureCount),
            _poolConfig.BootstrapPeers);
        if (pruned > 0)
        {
            _logger.LogInformation("Pruned {Count} stale peer endpoint(s).", pruned);
        }
    }

    private async Task RunShareRelayLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var proof in _stateService.AcceptedShares.ReadAllAsync(stoppingToken))
        {
            bool hasPeerSource = BootPeerSource.TryParsePeerSource(
                proof.Source,
                out _,
                out string parsedSourceEndpoint,
                out string parsedSourceNodeId);
            string? sourceEndpoint = hasPeerSource
                ? parsedSourceEndpoint
                : null;
            await _udpRelayService.RelayShareAsync(proof, sourceEndpoint, stoppingToken);
            HashSet<string> sessionRelayedEndpoints = await _sessionManager.RelayToConnectedSessionsAsync(
                proof,
                sourceEndpoint,
                parsedSourceNodeId,
                stoppingToken);
            List<string> peers = _stateService.GetPeerEndpointsForShareRelay(sourceEndpoint);
            if (sessionRelayedEndpoints.Count > 0 && !_poolConfig.PeerRelayLatencyProbeAllTransports)
            {
                peers = peers
                    .Where(peer => !sessionRelayedEndpoints.Contains(NormalizeEndpoint(peer)))
                    .ToList();
            }

            using var semaphore = new SemaphoreSlim(_stateService.GetPeerRelayParallelism());

            var relayTasks = peers.Select(peer => RelayShareWithLimitAsync(peer, proof, semaphore, stoppingToken)).ToArray();
            await Task.WhenAll(relayTasks);
        }
    }

    private async Task RelayShareWithLimitAsync(
        string peer,
        BootShareProof proof,
        SemaphoreSlim semaphore,
        CancellationToken stoppingToken)
    {
        await semaphore.WaitAsync(stoppingToken);
        try
        {
            await RelayShareAsync(peer, proof, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Timed out relaying share {ShareId} to {Peer}.", proof.ShareId, peer);
            _stateService.MarkPeerFailure(peer, "relay-timeout");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to relay share {ShareId} to {Peer}.", proof.ShareId, peer);
            _stateService.MarkPeerFailure(peer, "relay-error");
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task PollPeerAsync(string peer, CancellationToken stoppingToken)
    {
        using var client = _httpClientFactory.CreateClient("BootPeerClient");
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{NormalizeEndpoint(peer)}/api/network/summary");
        AddPeerAnnouncementHeaders(request);
        using HttpResponseMessage response = await client.SendAsync(request, stoppingToken);
        response.EnsureSuccessStatusCode();
        BootNetworkStatusDto? remote = await response.Content.ReadFromJsonAsync<BootNetworkStatusDto>(cancellationToken: stoppingToken);
        stopwatch.Stop();

        if (remote == null)
        {
            _stateService.MarkPeerFailure(peer, "empty");
            return;
        }

        string remoteEndpoint = _stateService.ResolvePeerEndpoint(peer, remote.SelfEndpoint);
        _stateService.UpdatePeerHeartbeat(remoteEndpoint, "connected", stopwatch.Elapsed.TotalMilliseconds, DateTime.UtcNow);
        _stateService.MergeDiscoveredPeers(remote.Peers.Select(x => x.Endpoint).Append(remoteEndpoint));

        BootVersionCompatibilityDto compatibility = _stateService.EvaluatePeerCompatibility(remote);
        _stateService.UpdatePeerCompatibility(remoteEndpoint, compatibility, DateTime.UtcNow);
        if (!compatibility.CanSyncState)
        {
            _logger.LogWarning("Peer {Peer} is incompatible: {Reason}", remoteEndpoint, compatibility.Reason);
            _stateService.MarkPeerFailure(remoteEndpoint, "version-mismatch");
            _stateService.RecordExternalNetworkEvent(
                "peer-version-mismatch",
                remoteEndpoint,
                compatibility.Reason,
                remote.CurrentTipBlockHash,
                remote.CurrentTipBlockHeight);
            return;
        }

        _stateService.UpdatePeerNetworkSnapshot(
            remoteEndpoint,
            remote.CurrentStateId,
            remote.CandidateStateId,
            remote.CurrentTipBlockHash);
        await FetchPeerAddressesAsync(client, remoteEndpoint, stoppingToken);

        BootNetworkStatusDto local = _stateService.GetNetworkStatus();

        if (!string.IsNullOrWhiteSpace(remote.CurrentStateId) &&
            !string.Equals(remote.CurrentStateId, local.CurrentStateId, StringComparison.OrdinalIgnoreCase) &&
            ShouldFetchRemoteCurrentState(local, remote))
        {
            BootStateBundle? lockedBundle = await client.GetFromJsonAsync<BootStateBundle>(
                $"{remoteEndpoint}/api/network/state/{remote.CurrentStateId}",
                stoppingToken);

            if (lockedBundle != null)
            {
                bool bootstrapped = await _stateService.TryBootstrapCurrentStateAsync(
                    lockedBundle,
                    remote.CurrentTipBlockHash,
                    remote.CurrentTipBlockHeight,
                    remoteEndpoint);
                local = _stateService.GetNetworkStatus();

                if (!bootstrapped)
                {
                    await _stateService.TryAdoptCurrentStateAsync(
                        lockedBundle,
                        remote.CurrentTipBlockHash,
                        remote.CurrentTipBlockHeight,
                        remoteEndpoint);
                    local = _stateService.GetNetworkStatus();
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(remote.CurrentTipBlockHash) &&
            (!BitcoinHashes.AreEquivalent(remote.CurrentTipBlockHash, local.CurrentTipBlockHash) ||
             (remote.CurrentTipBlockHeight.HasValue && remote.CurrentTipBlockHeight != local.CurrentTipBlockHeight)))
        {
            local = await _stateService.ObserveChainTipAsync(
                remote.CurrentTipBlockHash,
                $"peer-tip:{remoteEndpoint}",
                remote.CurrentTipBlockHeight);
        }

        if (!string.Equals(remote.CurrentStateId, local.CurrentStateId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (remote.LastRotationUtc.HasValue &&
            (local.LastRotationUtc != remote.LastRotationUtc ||
             local.CurrentRoundNumber != remote.CurrentRoundNumber))
        {
            await _stateService.TrySyncCurrentRoundMetadataAsync(
                remote.CurrentStateId,
                remote.CurrentRoundNumber,
                remote.LastRotationUtc,
                remoteEndpoint);
            local = _stateService.GetNetworkStatus();
        }

        if (string.IsNullOrWhiteSpace(remote.CandidateStateId) ||
            string.Equals(remote.CandidateStateId, local.CandidateStateId, StringComparison.OrdinalIgnoreCase) ||
            remote.OnDeckTotalDifficulty <= local.OnDeckTotalDifficulty)
        {
            return;
        }

        using HttpResponseMessage bundleResponse = await client.GetAsync(
            $"{remoteEndpoint}/api/network/state/{remote.CandidateStateId}",
            stoppingToken);
        if (bundleResponse.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug(
                "Peer {Peer} candidate state {StateId} changed before it could be fetched.",
                remoteEndpoint,
                remote.CandidateStateId);
            return;
        }

        bundleResponse.EnsureSuccessStatusCode();
        BootStateBundle? bundle = await bundleResponse.Content.ReadFromJsonAsync<BootStateBundle>(cancellationToken: stoppingToken);
        if (bundle == null)
        {
            return;
        }

        await _stateService.TryImportCandidateStateAsync(bundle, remoteEndpoint);
    }

    private async Task FetchPeerAddressesAsync(HttpClient client, string remoteEndpoint, CancellationToken stoppingToken)
    {
        try
        {
            string url = $"{remoteEndpoint}/api/network/peer-addresses?limit={Math.Max(1, _poolConfig.PeerAddressGossipLimit)}";
            using HttpResponseMessage response = await client.GetAsync(url, stoppingToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            response.EnsureSuccessStatusCode();
            BootPeerAddressBookDto? addressBook = await response.Content.ReadFromJsonAsync<BootPeerAddressBookDto>(cancellationToken: stoppingToken);
            if (addressBook == null)
            {
                return;
            }

            _stateService.MergeDiscoveredPeers(addressBook.Peers.Select(peer => peer.Endpoint).Append(addressBook.SelfEndpoint));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch peer address gossip from {Peer}.", remoteEndpoint);
        }
    }

    private void AddPeerAnnouncementHeaders(HttpRequestMessage request)
    {
        string selfEndpoint = _stateService.GetSelfEndpoint();
        if (string.IsNullOrWhiteSpace(selfEndpoint))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(BootNetworkController.PeerEndpointHeader, selfEndpoint);
        request.Headers.TryAddWithoutValidation(
            BootNetworkController.PeerProtocolVersionHeader,
            _poolConfig.BootProtocolVersion.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(BootNetworkController.PeerNetworkIdHeader, _poolConfig.BootNetworkId);
        request.Headers.TryAddWithoutValidation(
            BootNetworkController.PeerStateBundleSchemaVersionHeader,
            BootProtocolVersions.StateBundleSchemaVersion.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            BootNetworkController.PeerHttpApiVersionHeader,
            BootProtocolVersions.HttpApiVersion.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            BootNetworkController.PeerTransportVersionHeader,
            BootProtocolVersions.PeerTransportVersion.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            BootNetworkController.PeerReleaseVersionHeader,
            BootProtocolVersions.Local(_poolConfig).ReleaseVersion);
    }

    private async Task RelayShareAsync(string peer, BootShareProof proof, CancellationToken stoppingToken)
    {
        using var client = _httpClientFactory.CreateClient("BootPeerClient");
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

        using var response = await client.PostAsJsonAsync(
            $"{NormalizeEndpoint(peer)}/api/peer/share",
            announcement,
            stoppingToken);

        if (response.IsSuccessStatusCode)
        {
            _stateService.UpdatePeerHeartbeat(peer, "relayed", null, DateTime.UtcNow);
            return;
        }

        string status = response.StatusCode == HttpStatusCode.TooManyRequests
            ? "relay-rate-limited"
            : $"relay-http-{(int)response.StatusCode}";
        string responsePreview = FormatResponsePreview(await response.Content.ReadAsStringAsync(stoppingToken));
        _stateService.MarkPeerFailure(peer, status);
        _stateService.RecordExternalNetworkEvent(
            "peer-relay-failed",
            peer,
            string.IsNullOrWhiteSpace(responsePreview)
                ? $"Share relay failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}."
                : $"Share relay failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {responsePreview}",
            proof.PrevBlockHash,
            null);
    }

    private static string FormatResponsePreview(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        string preview = responseBody
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return preview.Length <= 300 ? preview : $"{preview[..300]}...";
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        return endpoint.Trim().TrimEnd('/');
    }

    private static bool ShouldFetchRemoteCurrentState(BootNetworkStatusDto local, BootNetworkStatusDto remote)
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

        if (!string.Equals(remote.CurrentStateId, local.CurrentStateId, StringComparison.OrdinalIgnoreCase) &&
            local.CurrentStateProofCount == 0 &&
            remote.CurrentStateProofCount > 0)
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
}
