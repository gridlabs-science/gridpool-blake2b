using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot_portal.HostedServices;

public sealed class BitcoinRpcReconciliationService : BackgroundService
{
    private readonly PoolConfig _config;
    private readonly IBitcoinRpcClient _rpcClient;
    private readonly BitcoinNotificationHealth _health;
    private readonly BootProtocolStateService _stateService;
    private readonly ILogger<BitcoinRpcReconciliationService> _logger;
    private readonly ChainDomainProfile? _chainProfile;
    private DateTime _lastZmqConfigurationCheckUtc = DateTime.MinValue;
    private DateTime _lastPeerNetworkCheckUtc = DateTime.MinValue;
    private DateTime _lastNetworkHashrateCheckUtc = DateTime.MinValue;
    private DateTime _lastChainProfileAttestationUtc = DateTime.MinValue;
    private long _lastChainProfileAttestedHeight = -1;

    public BitcoinRpcReconciliationService(
        PoolConfig config,
        IBitcoinRpcClient rpcClient,
        BitcoinNotificationHealth health,
        BootProtocolStateService stateService,
        ILogger<BitcoinRpcReconciliationService> logger)
    {
        _config = config;
        _rpcClient = rpcClient;
        _health = health;
        _stateService = stateService;
        _logger = logger;
        _chainProfile = ChainDomainProfiles.TryResolve(config, out ChainDomainProfile? profile, out _)
            ? profile
            : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_health.IsAttachedNode)
        {
            return;
        }

        if (!_rpcClient.IsConfigured)
        {
            _health.RecordRpcFailure(
                "Attached-node mode requires bitcoin_rpc_url.",
                DateTime.UtcNow);
        }

        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, _config.BitcoinRpcPollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_rpcClient.IsConfigured)
            {
                await ReconcileAsync(stoppingToken);
            }

            try
            {
                await _health.WaitForReconciliationRequestAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        DateTime checkUtc = DateTime.UtcNow;
        try
        {
            BitcoinBlockchainInfo info = await _rpcClient.GetBlockchainInfoAsync(cancellationToken);
            string bestHash = await _rpcClient.GetBestBlockHashAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(info.Chain) && !RpcChainMatchesConfiguredNetwork(info.Chain))
            {
                _health.RecordRpcAuthorityFailure(
                    $"Attached Bitcoin RPC chain '{Sanitize(info.Chain)}' does not match configured bitcoin_network '{BitcoinScript.NormalizeNetwork(_config.BitcoinNetwork)}'.",
                    checkUtc);
                return;
            }

            if (_chainProfile != null &&
                !await AttestChainProfileAsync(_chainProfile, info.Blocks, checkUtc, cancellationToken))
            {
                return;
            }

            _health.RecordRpcSuccess(
                info.Blocks,
                info.Headers,
                bestHash,
                info.InitialBlockDownload,
                info.VerificationProgress,
                checkUtc);

            if (checkUtc - _lastPeerNetworkCheckUtc >= TimeSpan.FromSeconds(15))
            {
                await InspectPeerNetworkAsync(checkUtc, cancellationToken);
                _lastPeerNetworkCheckUtc = checkUtc;
            }

            if (checkUtc - _lastNetworkHashrateCheckUtc >= TimeSpan.FromMinutes(1))
            {
                await InspectNetworkHashrateAsync(checkUtc, cancellationToken);
                _lastNetworkHashrateCheckUtc = checkUtc;
            }

            if (checkUtc - _lastZmqConfigurationCheckUtc >= TimeSpan.FromMinutes(1))
            {
                try
                {
                    IReadOnlyList<BitcoinZmqPublisher> publishers =
                        await _rpcClient.GetZmqNotificationsAsync(cancellationToken);
                    _health.RecordAdvertisedZmqPublishers(publishers);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Could not inspect Bitcoin ZMQ publisher configuration over RPC: {Message}",
                        Sanitize(ex.Message));
                }
                _lastZmqConfigurationCheckUtc = checkUtc;
            }

            if (info.InitialBlockDownload || info.Blocks < info.Headers)
            {
                return;
            }

            int recovered = await ReconcileTipAsync(info.Blocks, bestHash, cancellationToken);
            _health.RecordReconciliation(recovered, DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _health.RecordRpcFailure(ex.Message, DateTime.UtcNow);
            _logger.LogWarning("Bitcoin RPC reconciliation failed: {Message}", Sanitize(ex.Message));
        }
    }

    private async Task<bool> AttestChainProfileAsync(
        ChainDomainProfile profile,
        long observedHeight,
        DateTime checkUtc,
        CancellationToken cancellationToken)
    {
        bool crossedActivationSinceLastAttestation =
            observedHeight >= profile.ActivationHeight &&
            _lastChainProfileAttestedHeight < profile.ActivationHeight;
        if (!crossedActivationSinceLastAttestation &&
            checkUtc - _lastChainProfileAttestationUtc < TimeSpan.FromMinutes(1))
        {
            return true;
        }

        string genesisHash = string.Empty;
        string subversion = string.Empty;
        try
        {
            genesisHash = await _rpcClient.GetBlockHashAsync(0, cancellationToken);
            BitcoinNetworkInfo network = await _rpcClient.GetNetworkInfoAsync(cancellationToken);
            subversion = network.Subversion;
            string activationBlockHash = string.Empty;
            string activationHeaderHex = string.Empty;
            string preActivationHeaderHex = string.Empty;
            if (observedHeight >= profile.ActivationHeight)
            {
                activationBlockHash = await _rpcClient.GetBlockHashAsync(profile.ActivationHeight, cancellationToken);
                activationHeaderHex = await _rpcClient.GetBlockHeaderHexAsync(activationBlockHash, cancellationToken);
                string preActivationHash = await _rpcClient.GetBlockHashAsync(profile.ActivationHeight - 1, cancellationToken);
                preActivationHeaderHex = await _rpcClient.GetBlockHeaderHexAsync(preActivationHash, cancellationToken);
            }

            BitcoinAttachedNodeProfileAttestationResult result =
                BitcoinAttachedNodeProfileAttestation.Evaluate(
                    profile,
                    new BitcoinAttachedNodeProfileEvidence(
                        genesisHash,
                        subversion,
                        observedHeight,
                        activationBlockHash,
                        activationHeaderHex,
                        preActivationHeaderHex));
            _health.RecordChainProfileAttestation(
                result.IsValid,
                genesisHash,
                subversion,
                result.Reason,
                checkUtc);
            if (!result.IsValid)
            {
                _health.RecordRpcAuthorityFailure(result.Reason, checkUtc);
                _logger.LogError("Attached-node chain-profile attestation failed: {Reason}", Sanitize(result.Reason));
                return false;
            }

            _lastChainProfileAttestationUtc = checkUtc;
            _lastChainProfileAttestedHeight = observedHeight;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            string reason = $"Attached-node chain-profile attestation failed: {Sanitize(ex.Message)}";
            _health.RecordChainProfileAttestation(false, genesisHash, subversion, reason, checkUtc);
            _health.RecordRpcAuthorityFailure(reason, checkUtc);
            _logger.LogError("{Reason}", reason);
            return false;
        }
    }

    private bool RpcChainMatchesConfiguredNetwork(string rpcChain)
    {
        string configured = BitcoinScript.NormalizeNetwork(_config.BitcoinNetwork);
        string actual = rpcChain.Trim().ToLowerInvariant();
        return configured switch
        {
            BitcoinScript.Mainnet => actual == "main",
            BitcoinScript.Testnet4 => actual is "testnet4" or "test",
            BitcoinScript.Regtest => actual == "regtest",
            _ => false
        };
    }

    private async Task InspectPeerNetworkAsync(DateTime checkUtc, CancellationToken cancellationToken)
    {
        try
        {
            BitcoinNetworkInfo network = await _rpcClient.GetNetworkInfoAsync(cancellationToken);
            IReadOnlyList<BitcoinPeerInfo> peers = await _rpcClient.GetPeerInfoAsync(cancellationToken);
            _health.RecordPeerNetworkSuccess(network, peers, checkUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _health.RecordPeerNetworkFailure(ex.Message, checkUtc);
            _logger.LogDebug("Could not inspect Bitcoin peer health over RPC: {Message}", Sanitize(ex.Message));
        }
    }

    private async Task InspectNetworkHashrateAsync(DateTime checkUtc, CancellationToken cancellationToken)
    {
        try
        {
            _health.RecordNetworkHashrate(
                await _rpcClient.GetNetworkHashrateAsync(cancellationToken),
                checkUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not inspect Bitcoin network hashrate over RPC: {Message}", Sanitize(ex.Message));
        }
    }

    internal async Task<int> ReconcileTipAsync(
        long rpcHeight,
        string rpcBestHash,
        CancellationToken cancellationToken)
    {
        BootNetworkStatusDto local = _stateService.GetNetworkStatus();
        long? localHeight = local.CurrentTipBlockHeight;
        string localHash = local.CurrentTipBlockHash ?? string.Empty;
        if (!localHeight.HasValue || string.IsNullOrWhiteSpace(localHash))
        {
            await ObserveRpcBlockAsync(rpcHeight, rpcBestHash, cancellationToken);
            return 0;
        }

        if (localHeight.Value > rpcHeight)
        {
            _health.RecordRpcTipMismatch(
                $"Bitcoin RPC active height {rpcHeight} is behind GridPool observed height {localHeight}; waiting for the replacement chain before resuming mining.",
                DateTime.UtcNow);
            return 0;
        }

        if (localHeight.Value == rpcHeight && BitcoinHashes.AreEquivalent(localHash, rpcBestHash))
        {
            return 0;
        }

        string rpcHashAtLocalHeight = localHeight.Value == rpcHeight
            ? rpcBestHash
            : await _rpcClient.GetBlockHashAsync(localHeight.Value, cancellationToken);
        long replayFromHeight = localHeight.Value + 1;
        if (!BitcoinHashes.AreEquivalent(localHash, rpcHashAtLocalHeight))
        {
            long? earliest = _stateService.GetEarliestJournaledActiveBlockHeight();
            long? ancestorHeight = null;
            string ancestorHash = string.Empty;
            if (earliest.HasValue)
            {
                for (long height = localHeight.Value - 1; height >= earliest.Value; height--)
                {
                    string? journaledHash = _stateService.GetJournaledActiveBlockHash(height);
                    if (string.IsNullOrWhiteSpace(journaledHash))
                    {
                        continue;
                    }

                    string rpcHash = await _rpcClient.GetBlockHashAsync(height, cancellationToken);
                    if (BitcoinHashes.AreEquivalent(journaledHash, rpcHash))
                    {
                        ancestorHeight = height;
                        ancestorHash = rpcHash;
                        break;
                    }
                }
            }

            if (!ancestorHeight.HasValue ||
                !await _stateService.RollbackChainToAsync(
                    ancestorHash,
                    ancestorHeight.Value,
                    "rpc-common-ancestor"))
            {
                _health.RecordRpcTipMismatch(
                    $"Bitcoin RPC active chain diverges below the retained GridPool transition journal; mining remains paused for operator recovery.",
                    DateTime.UtcNow);
                return 0;
            }

            replayFromHeight = ancestorHeight.Value + 1;
        }

        int recovered = 0;
        for (long height = replayFromHeight; height <= rpcHeight; height++)
        {
            string hash = height == rpcHeight
                ? rpcBestHash
                : await _rpcClient.GetBlockHashAsync(height, cancellationToken);
            await ObserveRpcBlockAsync(height, hash, cancellationToken);
            recovered++;
        }

        return recovered;
    }

    private async Task ObserveRpcBlockAsync(
        long height,
        string blockHash,
        CancellationToken cancellationToken)
    {
        string headerHex = await _rpcClient.GetBlockHeaderHexAsync(blockHash, cancellationToken);
        DateTime receivedUtc = DateTime.UtcNow;
        _stateService.ObserveLocalChainTipHeader(
            headerHex,
            "rpc-reconcile",
            receivedUtc,
            height);
        await _stateService.ObserveChainTipAsync(
            blockHash,
            "rpc-reconcile",
            height);
    }

    private static string Sanitize(string? message)
    {
        string value = message?.Trim() ?? string.Empty;
        return value.Length > 240 ? value[..240] : value;
    }
}
