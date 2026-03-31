using System.Diagnostics;
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
                catch (OperationCanceledException)
                {
                    throw;
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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to relay share {ShareId} to {Peer}.", proof.ShareId, peer);
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
        bool sameTip = BitcoinHashes.AreEquivalent(remote.CurrentTipBlockHash, local.CurrentTipBlockHash);

        if (!string.IsNullOrWhiteSpace(remote.CurrentStateId) &&
            !string.Equals(remote.CurrentStateId, local.CurrentStateId, StringComparison.OrdinalIgnoreCase))
        {
            BootStateBundle? lockedBundle = await client.GetFromJsonAsync<BootStateBundle>(
                $"{remoteEndpoint}/api/network/state/{remote.CurrentStateId}",
                stoppingToken);

            if (lockedBundle != null)
            {
                bool bootstrapped = await _stateService.TryBootstrapCurrentStateAsync(
                    lockedBundle,
                    remote.CurrentTipBlockHash,
                    remoteEndpoint);
                local = _stateService.GetNetworkStatus();
                sameTip = BitcoinHashes.AreEquivalent(remote.CurrentTipBlockHash, local.CurrentTipBlockHash);

                if (!bootstrapped)
                {
                    await _stateService.TryAdoptCurrentStateAsync(lockedBundle, remote.CurrentTipBlockHash, remoteEndpoint);
                    local = _stateService.GetNetworkStatus();
                    sameTip = BitcoinHashes.AreEquivalent(remote.CurrentTipBlockHash, local.CurrentTipBlockHash);
                }
            }
        }

        if (!sameTip)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(remote.CandidateStateId) ||
            string.Equals(remote.CandidateStateId, local.CandidateStateId, StringComparison.OrdinalIgnoreCase) ||
            remote.OnDeckTotalDifficulty <= local.OnDeckTotalDifficulty)
        {
            return;
        }

        BootStateBundle? bundle = await client.GetFromJsonAsync<BootStateBundle>(
            $"{remoteEndpoint}/api/network/state/{remote.CandidateStateId}",
            stoppingToken);

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
        }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        return endpoint.Trim().TrimEnd('/');
    }
}
