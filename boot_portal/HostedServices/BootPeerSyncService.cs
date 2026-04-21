using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot_portal.HostedServices;

public class BootPeerSyncService : BackgroundService
{
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BootPeerSyncService> _logger;

    public BootPeerSyncService(
        PoolConfig poolConfig,
        BootProtocolStateService stateService,
        IHttpClientFactory httpClientFactory,
        ILogger<BootPeerSyncService> logger)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
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
            _logger.LogWarning("Peer sync is enabled but public_base_url is not configured. This node can dial peers but cannot be advertised correctly.");
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

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _poolConfig.PeerSyncIntervalSeconds)), stoppingToken);
        }
    }

    private async Task RunShareRelayLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var proof in _stateService.AcceptedShares.ReadAllAsync(stoppingToken))
        {
            List<string> peers = _stateService.GetPeerEndpoints();
            string? sourceEndpoint = proof.Source.StartsWith("peer:", StringComparison.OrdinalIgnoreCase)
                ? proof.Source["peer:".Length..]
                : null;

            foreach (string peer in peers)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(sourceEndpoint) &&
                    string.Equals(NormalizeEndpoint(peer), NormalizeEndpoint(sourceEndpoint), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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
            }
        }
    }

    private async Task PollPeerAsync(string peer, CancellationToken stoppingToken)
    {
        using var client = _httpClientFactory.CreateClient("BootPeerClient");
        var stopwatch = Stopwatch.StartNew();
        BootNetworkStatusDto? remote = await client.GetFromJsonAsync<BootNetworkStatusDto>(
            $"{NormalizeEndpoint(peer)}/api/network/summary",
            stoppingToken);
        stopwatch.Stop();

        if (remote == null)
        {
            _stateService.MarkPeerFailure(peer, "empty");
            return;
        }

        string remoteEndpoint = NormalizeEndpoint(string.IsNullOrWhiteSpace(remote.SelfEndpoint) ? peer : remote.SelfEndpoint);
        _stateService.UpdatePeerHeartbeat(remoteEndpoint, "connected", stopwatch.Elapsed.TotalMilliseconds, DateTime.UtcNow);
        _stateService.MergeDiscoveredPeers(remote.Peers.Select(x => x.Endpoint).Append(remoteEndpoint));

        if (!_stateService.IsCompatiblePeerNetwork(remote.ProtocolVersion, remote.NetworkId))
        {
            _stateService.MarkPeerFailure(remoteEndpoint, "foreign-network");
            return;
        }

        BootNetworkStatusDto local = _stateService.GetNetworkStatus();

        if (!string.IsNullOrWhiteSpace(remote.CurrentTipBlockHash) &&
            (!BitcoinHashes.AreEquivalent(remote.CurrentTipBlockHash, local.CurrentTipBlockHash) ||
             (remote.CurrentTipBlockHeight.HasValue && remote.CurrentTipBlockHeight != local.CurrentTipBlockHeight)))
        {
            local = await _stateService.ObserveChainTipAsync(
                remote.CurrentTipBlockHash,
                $"peer-tip:{remoteEndpoint}",
                remote.CurrentTipBlockHeight);
        }

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

    private async Task RelayShareAsync(string peer, BootShareProof proof, CancellationToken stoppingToken)
    {
        using var client = _httpClientFactory.CreateClient("BootPeerClient");
        var announcement = new PeerShareAnnouncement
        {
            SenderEndpoint = _stateService.GetSelfEndpoint(),
            ProtocolVersion = _poolConfig.BootProtocolVersion,
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
        _stateService.MarkPeerFailure(peer, status);
        _stateService.RecordExternalNetworkEvent(
            "peer-relay-failed",
            peer,
            $"Share relay failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.",
            proof.PrevBlockHash,
            null);
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
