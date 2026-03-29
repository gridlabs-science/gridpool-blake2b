using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using boot_portal.Models;
using boot_portal.Utils;
using Microsoft.AspNetCore.SignalR;

namespace boot_portal.Services;

public class BootProtocolStateService
{
    private readonly object _sync = new();
    private readonly PoolConfig _poolConfig;
    private readonly BootShareVerifier _shareVerifier;
    private readonly IHubContext<PoolStatsHub> _hubContext;
    private readonly ILogger<BootProtocolStateService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly Channel<BootShareProof> _acceptedShares = Channel.CreateUnbounded<BootShareProof>();
    private readonly HashSet<string> _seenShareIds = [];
    private readonly Queue<string> _seenShareQueue = new();
    private const int MaxSeenShareIds = 20000;

    private PoolState _state = new();

    public event Func<string, Task>? WinnersListChanged;

    public BootProtocolStateService(
        PoolConfig poolConfig,
        BootShareVerifier shareVerifier,
        IHubContext<PoolStatsHub> hubContext,
        ILogger<BootProtocolStateService> logger)
    {
        _poolConfig = poolConfig;
        _shareVerifier = shareVerifier;
        _hubContext = hubContext;
        _logger = logger;
        LoadState();
    }

    public ChannelReader<BootShareProof> AcceptedShares => _acceptedShares.Reader;

    public List<PayoutInfo> GetWinnersList()
    {
        lock (_sync)
        {
            return ClonePayouts(_state.WinnersList);
        }
    }

    public List<PayoutInfo> GetCoinbaseOutputs()
    {
        lock (_sync)
        {
            return BuildCoinbaseOutputsNoLock(_state.WinnersList);
        }
    }

    public List<PayoutInfo> GetOnDeckList()
    {
        lock (_sync)
        {
            return ClonePayouts(_state.OnDeckList);
        }
    }

    public BestShareRecord GetBestShare()
    {
        lock (_sync)
        {
            return CloneBestShare(_state.BestShare);
        }
    }

    public BootNetworkStatusDto GetNetworkStatus()
    {
        lock (_sync)
        {
            return BuildNetworkStatusNoLock();
        }
    }

    public PayoutResponseDto GetPayoutResponse()
    {
        lock (_sync)
        {
            return new PayoutResponseDto
            {
                Sequence = DateTime.UtcNow.Ticks,
                Payouts = ClonePayouts(_state.WinnersList),
                CoinbaseOutputs = BuildCoinbaseOutputsNoLock(_state.WinnersList),
                Network = BuildNetworkStatusNoLock()
            };
        }
    }

    public BootStateBundle? GetStateBundle(string stateId)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            return null;
        }

        lock (_sync)
        {
            var archived = _state.ArchivedStateBundles.FirstOrDefault(x =>
                string.Equals(x.StateId, stateId, StringComparison.OrdinalIgnoreCase));
            if (archived != null)
            {
                return CloneBundle(archived);
            }

            if (string.Equals(stateId, _state.CandidateStateId, StringComparison.OrdinalIgnoreCase))
            {
                return BuildBundleFromCurrentCandidateNoLock();
            }

            if (string.Equals(stateId, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase))
            {
                return BuildBundleFromCurrentWinnersNoLock();
            }

            return null;
        }
    }

    public List<BootPeerStatus> GetPeers()
    {
        lock (_sync)
        {
            return _state.Peers.Select(ClonePeer).ToList();
        }
    }

    public List<string> GetPeerEndpoints()
    {
        lock (_sync)
        {
            return _state.Peers
                .Select(x => x.Endpoint)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
    }

    public bool IsAdminAuthorized(string? suppliedApiKey)
    {
        if (string.IsNullOrWhiteSpace(_poolConfig.AdminApiKey))
        {
            return false;
        }

        return string.Equals(_poolConfig.AdminApiKey, suppliedApiKey, StringComparison.Ordinal);
    }

    public string GetSelfEndpoint()
    {
        return NormalizePeerEndpoint(_poolConfig.PublicBaseUrl);
    }

    public bool IsCompatiblePeerNetwork(int protocolVersion, string networkId)
    {
        return protocolVersion == _poolConfig.BootProtocolVersion &&
               string.Equals(networkId, _poolConfig.BootNetworkId, StringComparison.OrdinalIgnoreCase);
    }

    public void SeedPeers(IEnumerable<string> endpoints)
    {
        bool changed = false;
        lock (_sync)
        {
            foreach (string endpoint in endpoints)
            {
                changed |= UpsertPeerNoLock(endpoint, "configured", null, null, persistStatusOnly: false);
            }

            if (changed)
            {
                SaveStateNoLock();
            }
        }
    }

    public void MergeDiscoveredPeers(IEnumerable<string> endpoints)
    {
        bool changed = false;
        lock (_sync)
        {
            foreach (string endpoint in endpoints)
            {
                changed |= UpsertPeerNoLock(endpoint, "discovered", null, null, persistStatusOnly: false);
            }

            if (changed)
            {
                SaveStateNoLock();
            }
        }
    }

    public void UpdatePeerHeartbeat(string endpoint, string status, double? latencyMs, DateTime lastSeenUtc)
    {
        lock (_sync)
        {
            if (UpsertPeerNoLock(endpoint, status, latencyMs, lastSeenUtc, persistStatusOnly: true))
            {
                SaveStateNoLock();
            }
        }
    }

    public void MarkPeerFailure(string endpoint, string status)
    {
        lock (_sync)
        {
            if (UpsertPeerNoLock(endpoint, status, null, null, persistStatusOnly: true))
            {
                SaveStateNoLock();
            }
        }
    }

    public async Task<ShareRecordingResult> RecordShareAsync(RecordedShareSubmission share)
    {
        List<PayoutInfo> winnersSnapshot;
        string? currentTipSnapshot;
        string currentStateSnapshot;
        lock (_sync)
        {
            winnersSnapshot = ClonePayouts(_state.WinnersList);
            currentTipSnapshot = NormalizeCanonicalBlockHash(_state.CurrentTipBlockHash);
            currentStateSnapshot = _state.CurrentStateId;
        }

        BootShareValidationResult validation = _shareVerifier.ValidateShare(share, winnersSnapshot, currentTipSnapshot);
        if (!validation.IsValid)
        {
            _logger.LogInformation(
                "Rejected {Source} share from {MinerAddress}: {Reason}",
                string.IsNullOrWhiteSpace(share.Source) ? "unknown" : share.Source,
                share.MinerAddress,
                validation.RejectionReason ?? "Invalid share");
            return new ShareRecordingResult
            {
                Accepted = false,
                RejectionReason = validation.RejectionReason ?? "Invalid share",
                BestShare = GetBestShare(),
                OnDeckList = GetOnDeckList(),
                NetworkStatus = GetNetworkStatus()
            };
        }

        if (validation.Difficulty < 1)
        {
            _logger.LogInformation(
                "Rejected {Source} share from {MinerAddress}: low difficulty ({Difficulty})",
                string.IsNullOrWhiteSpace(share.Source) ? "unknown" : share.Source,
                share.MinerAddress,
                validation.Difficulty);
            return new ShareRecordingResult
            {
                Accepted = false,
                RejectionReason = "Low difficulty",
                ComputedDifficulty = validation.Difficulty,
                BestShare = GetBestShare(),
                OnDeckList = GetOnDeckList(),
                NetworkStatus = GetNetworkStatus()
            };
        }

        ShareRecordingResult result;
        bool shouldRelay = false;
        lock (_sync)
        {
            if (!string.Equals(currentStateSnapshot, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase) ||
                !BitcoinHashes.AreEquivalent(currentTipSnapshot, NormalizeCanonicalBlockHash(_state.CurrentTipBlockHash)))
            {
                return new ShareRecordingResult
                {
                    Accepted = false,
                    RejectionReason = "Round changed during validation",
                    ComputedDifficulty = validation.Difficulty,
                    BestShare = CloneBestShare(_state.BestShare),
                    OnDeckList = ClonePayouts(_state.OnDeckList),
                    NetworkStatus = BuildNetworkStatusNoLock()
                };
            }

            BootShareProof proof = CreateProofNoLock(validation, share.Source);
            if (!RememberShareIdNoLock(proof.ShareId))
            {
                return new ShareRecordingResult
                {
                    Accepted = false,
                    RejectionReason = "Duplicate share",
                    ComputedDifficulty = validation.Difficulty,
                    IsBlock = validation.IsBlock,
                    BlockHash = validation.BlockHash,
                    BestShare = CloneBestShare(_state.BestShare),
                    OnDeckList = ClonePayouts(_state.OnDeckList),
                    NetworkStatus = BuildNetworkStatusNoLock()
                };
            }

            int insertIndex = 0;

            while (insertIndex < _state.OnDeckProofs.Count &&
                   _state.OnDeckProofs[insertIndex].Difficulty >= proof.Difficulty)
            {
                insertIndex++;
            }

            bool affectedOnDeck = insertIndex < _poolConfig.SharedWinnerSlotCount;
            if (affectedOnDeck)
            {
                _state.OnDeckProofs.Insert(insertIndex, proof);
            }

            _state.OnDeckProofs = _state.OnDeckProofs
                .OrderByDescending(x => x.Difficulty)
                .ThenBy(x => x.ShareId, StringComparer.Ordinal)
                .ToList();

            while (_state.OnDeckProofs.Count > _poolConfig.SharedWinnerSlotCount)
            {
                _state.OnDeckProofs.RemoveAt(_state.OnDeckProofs.Count - 1);
            }

            RebuildOnDeckListNoLock();

            bool newRecord = false;
            if (proof.Difficulty > _state.BestShare.Difficulty)
            {
                _state.BestShare = new BestShareRecord
                {
                    Difficulty = proof.Difficulty,
                    MinerAddress = proof.Username,
                    Timestamp = proof.Timestamp
                };
                newRecord = true;
            }

            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            SaveStateNoLock();

            result = new ShareRecordingResult
            {
                Accepted = true,
                AffectedOnDeck = affectedOnDeck,
                NewRecord = newRecord,
                ComputedDifficulty = validation.Difficulty,
                IsBlock = validation.IsBlock,
                BlockHash = validation.BlockHash,
                BestShare = CloneBestShare(_state.BestShare),
                OnDeckList = ClonePayouts(_state.OnDeckList),
                NetworkStatus = BuildNetworkStatusNoLock(),
                AcceptedProof = CloneProof(proof)
            };

            shouldRelay = affectedOnDeck;
        }

        if (result.NewRecord)
        {
            await _hubContext.Clients.All.SendAsync("UpdateRecord", result.BestShare);
        }

        if (result.AffectedOnDeck)
        {
            await _hubContext.Clients.All.SendAsync("UpdateOnDeck", result.OnDeckList);
        }

        await _hubContext.Clients.All.SendAsync("UpdateNetworkState", result.NetworkStatus);
        if (shouldRelay && result.AcceptedProof != null)
        {
            await _acceptedShares.Writer.WriteAsync(result.AcceptedProof);
        }
        return result;
    }

    public async Task<ShareRecordingResult> SubmitShareAsync(RecordedShareSubmission share, string blockSource)
    {
        ShareRecordingResult result = await RecordShareAsync(share);
        bool shouldRotate =
            result.IsBlock &&
            !string.IsNullOrWhiteSpace(result.BlockHash) &&
            (result.Accepted || string.Equals(result.RejectionReason, "Duplicate share", StringComparison.Ordinal));

        if (!shouldRotate)
        {
            return result;
        }

        RoundRotationResult rotation = await RotateToNextRoundAsync(result.BlockHash, blockSource, manual: false);
        result.Rotation = rotation;
        result.NetworkStatus = rotation.NetworkStatus;
        result.OnDeckList = rotation.OnDeckList;
        return result;
    }

    public async Task<RoundRotationResult> RotateToNextRoundAsync(string blockHash, string source, bool manual)
    {
        RoundRotationResult result;
        lock (_sync)
        {
            string? previousTipBlockHash = NormalizeCanonicalBlockHash(_state.CurrentTipBlockHash);
            string? submittedBlockHash = NormalizeCanonicalBlockHash(blockHash);
            string? effectiveBlockHash = manual
                ? submittedBlockHash ?? previousTipBlockHash
                : submittedBlockHash;

            if (!manual &&
                !string.IsNullOrWhiteSpace(effectiveBlockHash) &&
                BitcoinHashes.AreEquivalent(effectiveBlockHash, previousTipBlockHash))
            {
                return new RoundRotationResult
                {
                    Rotated = false,
                    Reason = "Block already applied",
                    BlockHash = previousTipBlockHash,
                    WinnersList = ClonePayouts(_state.WinnersList),
                    OnDeckList = ClonePayouts(_state.OnDeckList),
                    NetworkStatus = BuildNetworkStatusNoLock()
                };
            }

            BootStateBundle lockedBundle = BuildBundleFromCurrentCandidateNoLock();
            lockedBundle.StateId = ComputeStateIdNoLock(_state.OnDeckProofs, effectiveBlockHash);
            lockedBundle.Kind = manual ? "manual-rotation" : source;
            lockedBundle.LockedByBlockHash = effectiveBlockHash;
            lockedBundle.ParentBlockHash = previousTipBlockHash;
            lockedBundle.CreatedAtUtc = DateTime.UtcNow;
            lockedBundle.Commitment = BuildCommitmentNoLock();

            _state.CurrentTipBlockHash = effectiveBlockHash;
            _state.LastRotationUtc = DateTime.UtcNow;
            _state.CurrentStateId = lockedBundle.StateId;
            _state.WinnersList = ClonePayouts(lockedBundle.WinnersList);
            _state.OnDeckProofs = [];
            _state.OnDeckList = [];
            UpsertArchivedBundleNoLock(lockedBundle);

            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            SaveStateNoLock();

            result = new RoundRotationResult
            {
                Rotated = true,
                Reason = manual ? "Manual reset completed" : $"Round rotated from {source}",
                BlockHash = effectiveBlockHash,
                WinnersList = ClonePayouts(_state.WinnersList),
                OnDeckList = ClonePayouts(_state.OnDeckList),
                NetworkStatus = BuildNetworkStatusNoLock(),
                LockedStateBundle = CloneBundle(lockedBundle)
            };
        }

        await _hubContext.Clients.All.SendAsync("UpdateWinners", result.WinnersList);
        await _hubContext.Clients.All.SendAsync("UpdateOnDeck", result.OnDeckList);
        await _hubContext.Clients.All.SendAsync("UpdateNetworkState", result.NetworkStatus);
        await NotifyWinnersListChangedAsync(manual ? "manual-reset" : source);
        return result;
    }

    public async Task<BootNetworkStatusDto> ObserveChainTipAsync(string blockHash, string source)
    {
        BootNetworkStatusDto status;
        List<PayoutInfo> onDeckSnapshot;
        lock (_sync)
        {
            string? normalizedBlockHash = NormalizeCanonicalBlockHash(blockHash);
            if (string.IsNullOrWhiteSpace(normalizedBlockHash))
            {
                _logger.LogWarning("Ignored invalid chain tip hash from {Source}: {BlockHash}", source, blockHash);
                return BuildNetworkStatusNoLock();
            }

            if (BitcoinHashes.AreEquivalent(normalizedBlockHash, _state.CurrentTipBlockHash))
            {
                return BuildNetworkStatusNoLock();
            }

            _state.CurrentTipBlockHash = normalizedBlockHash;
            _state.OnDeckProofs = [];
            _state.OnDeckList = [];
            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            SaveStateNoLock();

            status = BuildNetworkStatusNoLock();
            onDeckSnapshot = ClonePayouts(_state.OnDeckList);
        }

        _logger.LogInformation("Observed new chain tip from {Source}: {BlockHash}", source, blockHash);
        await _hubContext.Clients.All.SendAsync("UpdateOnDeck", onDeckSnapshot);
        await _hubContext.Clients.All.SendAsync("UpdateNetworkState", status);
        return status;
    }

    public async Task<bool> TryImportCandidateStateAsync(BootStateBundle bundle, string sourceEndpoint)
    {
        if (!IsCompatiblePeerNetwork(bundle.ProtocolVersion, bundle.NetworkId))
        {
            return false;
        }

        if (bundle.WinnersList.Count > _poolConfig.SharedWinnerSlotCount || bundle.ShareProofs.Count > _poolConfig.SharedWinnerSlotCount)
        {
            return false;
        }

        List<PayoutInfo> winnersSnapshot;
        string? currentTipSnapshot;
        string currentStateSnapshot;
        lock (_sync)
        {
            winnersSnapshot = ClonePayouts(_state.WinnersList);
            currentTipSnapshot = _state.CurrentTipBlockHash;
            currentStateSnapshot = _state.CurrentStateId;
        }

        if (!string.IsNullOrWhiteSpace(currentTipSnapshot) &&
            !BitcoinHashes.AreEquivalent(bundle.ParentBlockHash, currentTipSnapshot))
        {
            return false;
        }

        List<BootShareProof> validatedProofs;
        List<PayoutInfo> expectedPayouts;
        double totalDifficulty;

        try
        {
            validatedProofs = ValidateImportedProofs(bundle.ShareProofs, winnersSnapshot, currentTipSnapshot, $"peer-state:{sourceEndpoint}");
            expectedPayouts = BuildPayoutsFromProofs(validatedProofs);
            totalDifficulty = validatedProofs.Sum(x => x.Difficulty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected candidate state bundle from {SourceEndpoint}.", sourceEndpoint);
            return false;
        }

        string expectedStateId = ComputeStateIdNoLock(validatedProofs, currentTipSnapshot);
        if (!string.Equals(expectedStateId, bundle.StateId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!WinnersMatch(expectedPayouts, bundle.WinnersList))
        {
            return false;
        }

        bool imported = false;
        BootNetworkStatusDto networkStatus;
        List<PayoutInfo> onDeckSnapshot;

        lock (_sync)
        {
            if (!string.Equals(currentStateSnapshot, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase) ||
                !BitcoinHashes.AreEquivalent(currentTipSnapshot, _state.CurrentTipBlockHash))
            {
                return false;
            }

            double localTotalDifficulty = _state.OnDeckProofs.Sum(x => x.Difficulty);
            if (totalDifficulty <= localTotalDifficulty &&
                !string.Equals(bundle.StateId, _state.CandidateStateId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _state.OnDeckProofs = validatedProofs.Select(CloneProof).ToList();
            _state.OnDeckList = ClonePayouts(expectedPayouts);
            _state.CandidateStateId = bundle.StateId;
            foreach (var proof in validatedProofs)
            {
                RememberShareIdNoLock(proof.ShareId);
            }
            SaveStateNoLock();

            imported = true;
            networkStatus = BuildNetworkStatusNoLock();
            onDeckSnapshot = ClonePayouts(_state.OnDeckList);
        }

        if (imported)
        {
            _logger.LogInformation("Imported stronger candidate state {StateId} from {SourceEndpoint}.", bundle.StateId, sourceEndpoint);
            await _hubContext.Clients.All.SendAsync("UpdateOnDeck", onDeckSnapshot);
            await _hubContext.Clients.All.SendAsync("UpdateNetworkState", networkStatus);
        }

        return imported;
    }

    public async Task<bool> TryAdoptCurrentStateAsync(BootStateBundle bundle, string sourceEndpoint)
    {
        if (!IsCompatiblePeerNetwork(bundle.ProtocolVersion, bundle.NetworkId))
        {
            return false;
        }

        if (bundle.WinnersList.Count > _poolConfig.SharedWinnerSlotCount || bundle.ShareProofs.Count > _poolConfig.SharedWinnerSlotCount)
        {
            return false;
        }

        List<PayoutInfo> currentWinnersSnapshot;
        string? currentTipSnapshot;
        string currentStateSnapshot;
        lock (_sync)
        {
            currentWinnersSnapshot = ClonePayouts(_state.WinnersList);
            currentTipSnapshot = _state.CurrentTipBlockHash;
            currentStateSnapshot = _state.CurrentStateId;
        }

        if (string.IsNullOrWhiteSpace(currentTipSnapshot) ||
            !BitcoinHashes.AreEquivalent(bundle.LockedByBlockHash, currentTipSnapshot))
        {
            return false;
        }

        List<BootShareProof> validatedProofs;
        List<PayoutInfo> expectedPayouts;
        try
        {
            validatedProofs = ValidateImportedProofs(bundle.ShareProofs, currentWinnersSnapshot, bundle.ParentBlockHash, $"peer-locked:{sourceEndpoint}");
            expectedPayouts = BuildPayoutsFromProofs(validatedProofs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected locked state bundle from {SourceEndpoint}.", sourceEndpoint);
            return false;
        }

        string expectedStateId = ComputeStateIdNoLock(validatedProofs, bundle.LockedByBlockHash);
        if (!string.Equals(expectedStateId, bundle.StateId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!WinnersMatch(expectedPayouts, bundle.WinnersList))
        {
            return false;
        }

        BootNetworkStatusDto networkStatus;
        List<PayoutInfo> winnersSnapshot;
        List<PayoutInfo> onDeckSnapshot;
        bool adopted = false;

        lock (_sync)
        {
            if (!string.Equals(currentStateSnapshot, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase) ||
                !BitcoinHashes.AreEquivalent(currentTipSnapshot, _state.CurrentTipBlockHash))
            {
                return false;
            }

            if (string.Equals(bundle.StateId, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _state.CurrentStateId = bundle.StateId;
            _state.LastRotationUtc = bundle.CreatedAtUtc == default ? DateTime.UtcNow : bundle.CreatedAtUtc;
            _state.WinnersList = ClonePayouts(expectedPayouts);
            _state.OnDeckProofs = [];
            _state.OnDeckList = [];
            foreach (var proof in validatedProofs)
            {
                RememberShareIdNoLock(proof.ShareId);
            }

            BootStateBundle lockedBundle = CloneBundle(bundle);
            lockedBundle.ShareProofs = validatedProofs.Select(CloneProof).ToList();
            lockedBundle.WinnersList = ClonePayouts(expectedPayouts);
            lockedBundle.StateId = expectedStateId;
            lockedBundle.TotalDifficulty = validatedProofs.Sum(x => x.Difficulty);
            lockedBundle.LockedByBlockHash = BitcoinHashes.NormalizeHex(bundle.LockedByBlockHash);
            lockedBundle.ParentBlockHash = BitcoinHashes.NormalizeHex(bundle.ParentBlockHash);
            lockedBundle.Commitment = BuildCommitmentNoLock();
            UpsertArchivedBundleNoLock(lockedBundle);

            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            SaveStateNoLock();

            winnersSnapshot = ClonePayouts(_state.WinnersList);
            onDeckSnapshot = ClonePayouts(_state.OnDeckList);
            networkStatus = BuildNetworkStatusNoLock();
            adopted = true;
        }

        if (adopted)
        {
            _logger.LogInformation("Adopted locked state {StateId} from {SourceEndpoint}.", bundle.StateId, sourceEndpoint);
            await _hubContext.Clients.All.SendAsync("UpdateWinners", winnersSnapshot);
            await _hubContext.Clients.All.SendAsync("UpdateOnDeck", onDeckSnapshot);
            await _hubContext.Clients.All.SendAsync("UpdateNetworkState", networkStatus);
            await NotifyWinnersListChangedAsync($"adopted-state:{sourceEndpoint}");
        }

        return adopted;
    }

    private void LoadState()
    {
        lock (_sync)
        {
            if (File.Exists(BootPortalPaths.PoolStateFilePath))
            {
                try
                {
                    string json = File.ReadAllText(BootPortalPaths.PoolStateFilePath);
                    var loaded = JsonSerializer.Deserialize<PoolState>(json);
                    if (loaded != null)
                    {
                        _state = loaded;
                        _state.Metadata.NetworkId = string.IsNullOrWhiteSpace(_state.Metadata.NetworkId)
                            ? _poolConfig.BootNetworkId
                            : _state.Metadata.NetworkId;
                        _state.Metadata.ProtocolVersion = _poolConfig.BootProtocolVersion;
                        string? loadedTip = NormalizeCanonicalBlockHash(_state.CurrentTipBlockHash);
                        if (!string.IsNullOrWhiteSpace(_state.CurrentTipBlockHash) && string.IsNullOrWhiteSpace(loadedTip))
                        {
                            _logger.LogWarning(
                                "Discarding non-canonical persisted chain tip marker: {Tip}",
                                _state.CurrentTipBlockHash);
                        }

                        _state.CurrentTipBlockHash = loadedTip;

                        if (_state.OnDeckProofs.Count == 0 && _state.OnDeckList.Count > 0)
                        {
                            _state.OnDeckProofs = _state.OnDeckList.Select(CreatePlaceholderProofNoLock).ToList();
                        }

                        if (_state.WinnersList.Count == 0)
                        {
                            InitializeDefaultsNoLock();
                        }

                        _state.CurrentStateId = string.IsNullOrWhiteSpace(_state.CurrentStateId)
                            ? ComputeStateIdFromPayoutsNoLock(_state.WinnersList, _state.CurrentTipBlockHash)
                            : _state.CurrentStateId;
                        _state.CandidateStateId = ComputeCandidateStateIdNoLock();
                        SeedSeenSharesNoLock();
                        _logger.LogInformation("Loaded Boot protocol state from disk.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load Boot protocol state from disk.");
                }
            }

            InitializeDefaultsNoLock();
            SeedSeenSharesNoLock();
            SaveStateNoLock();
        }
    }

    private void InitializeDefaultsNoLock()
    {
        _state = new PoolState
        {
            Metadata = new BootProtocolMetadata
            {
                NetworkId = _poolConfig.BootNetworkId,
                ProtocolVersion = _poolConfig.BootProtocolVersion
            },
            BestShare = new BestShareRecord(),
            CurrentTipBlockHash = null,
            LastRotationUtc = null
        };

        _state.WinnersList.Add(new PayoutInfo
        {
            Value = Program.BLOCK_REWARD / 2,
            Address = _poolConfig.PoolPayoutScript,
            Username = _poolConfig.PoolPayoutScript
        });
        _state.CurrentStateId = ComputeStateIdFromPayoutsNoLock(_state.WinnersList, null);
        _state.CandidateStateId = ComputeCandidateStateIdNoLock();
    }

    private void SaveStateNoLock()
    {
        _state.Metadata.NetworkId = _poolConfig.BootNetworkId;
        _state.Metadata.ProtocolVersion = _poolConfig.BootProtocolVersion;
        BootPortalPaths.EnsureParentDirectory(BootPortalPaths.PoolStateFilePath);
        File.WriteAllText(BootPortalPaths.PoolStateFilePath, JsonSerializer.Serialize(_state, _jsonOptions));
    }

    private void RebuildOnDeckListNoLock()
    {
        _state.OnDeckList = [];
        if (_state.OnDeckProofs.Count == 0)
        {
            return;
        }

        ulong reward = Program.BLOCK_REWARD / ((ulong)_state.OnDeckProofs.Count + 1);
        foreach (var proof in _state.OnDeckProofs)
        {
            _state.OnDeckList.Add(new PayoutInfo
            {
                Value = reward,
                Address = proof.MinerAddress,
                Username = string.IsNullOrWhiteSpace(proof.Username) ? proof.MinerAddress : proof.Username,
                Difficulty = proof.Difficulty,
                DiffString = proof.DiffString
            });
        }
    }

    private BootNetworkStatusDto BuildNetworkStatusNoLock()
    {
        return new BootNetworkStatusDto
        {
            SelfEndpoint = NormalizePeerEndpoint(_poolConfig.PublicBaseUrl),
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            NetworkId = _poolConfig.BootNetworkId,
            SharedWinnerSlotCount = _poolConfig.SharedWinnerSlotCount,
            TotalPayoutSlotCount = _poolConfig.TotalPayoutSlotCount,
            CurrentStateId = _state.CurrentStateId,
            CandidateStateId = _state.CandidateStateId,
            CurrentTipBlockHash = _state.CurrentTipBlockHash,
            LastRotationUtc = _state.LastRotationUtc,
            WinnersCount = _state.WinnersList.Count,
            OnDeckCount = _state.OnDeckList.Count,
            OnDeckTotalDifficulty = _state.OnDeckProofs.Sum(x => x.Difficulty),
            PeerCount = _state.Peers.Count,
            Peers = _state.Peers.Select(ClonePeer).ToList(),
            Commitment = BuildCommitmentNoLock()
        };
    }

    private BootCommitmentInfo BuildCommitmentNoLock()
    {
        string previewState = string.IsNullOrWhiteSpace(_state.CandidateStateId)
            ? "pending"
            : _state.CandidateStateId[..Math.Min(16, _state.CandidateStateId.Length)];

        return new BootCommitmentInfo
        {
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            NetworkId = _poolConfig.BootNetworkId,
            NextStateId = _state.CandidateStateId,
            OnChainSupported = false,
            TagPreview = $"BOOT|v{_poolConfig.BootProtocolVersion}|{_poolConfig.BootNetworkId}|{previewState}",
            SupportNote = "Per-round on-chain commitments require miner-side template support. The server computes state IDs now, but DATUM/Hydrapool must expose a dynamic coinbase hook before this can be embedded on-chain."
        };
    }

    private BootStateBundle BuildBundleFromCurrentCandidateNoLock()
    {
        return new BootStateBundle
        {
            StateId = _state.CandidateStateId,
            Kind = "candidate",
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            NetworkId = _poolConfig.BootNetworkId,
            LockedByBlockHash = null,
            ParentBlockHash = _state.CurrentTipBlockHash,
            CreatedAtUtc = DateTime.UtcNow,
            TotalDifficulty = _state.OnDeckProofs.Sum(x => x.Difficulty),
            WinnersList = ClonePayouts(_state.OnDeckList),
            ShareProofs = _state.OnDeckProofs.Select(CloneProof).ToList(),
            Commitment = BuildCommitmentNoLock()
        };
    }

    private BootStateBundle BuildBundleFromCurrentWinnersNoLock()
    {
        return new BootStateBundle
        {
            StateId = _state.CurrentStateId,
            Kind = "current",
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            NetworkId = _poolConfig.BootNetworkId,
            LockedByBlockHash = _state.CurrentTipBlockHash,
            ParentBlockHash = null,
            CreatedAtUtc = _state.LastRotationUtc ?? DateTime.UtcNow,
            TotalDifficulty = _state.WinnersList.Sum(x => x.Difficulty),
            WinnersList = ClonePayouts(_state.WinnersList),
            ShareProofs = [],
            Commitment = BuildCommitmentNoLock()
        };
    }

    private BootShareProof CreateProofNoLock(BootShareValidationResult validation, string source, DateTime? timestamp = null)
    {
        return new BootShareProof
        {
            ShareId = validation.ShareId,
            MinerAddress = validation.MinerAddress,
            Username = validation.Username,
            ScriptPubKeyHex = validation.ScriptPubKeyHex,
            HeaderHex = validation.HeaderHex,
            CoinbaseHex = validation.CoinbaseHex,
            MerklePath = validation.MerklePath.ToList(),
            PrevBlockHash = validation.PrevBlockHash,
            Difficulty = validation.Difficulty,
            DiffString = ClientHandler.FormatDifficulty(validation.Difficulty),
            Source = source,
            Timestamp = timestamp ?? DateTime.UtcNow
        };
    }

    private BootShareProof CreatePlaceholderProofNoLock(PayoutInfo payout)
    {
        string minerAddress = string.IsNullOrWhiteSpace(payout.Address) ? _poolConfig.PoolPayoutScript : payout.Address;
        return new BootShareProof
        {
            ShareId = ComputeShareId(minerAddress, payout.DiffString, payout.Address),
            MinerAddress = minerAddress,
            Username = string.IsNullOrWhiteSpace(payout.Username) ? minerAddress : payout.Username,
            ScriptPubKeyHex = BitcoinScript.TryAddressToScriptPubKey(minerAddress, out var script)
                ? Convert.ToHexString(script).ToLowerInvariant()
                : string.Empty,
            Difficulty = payout.Difficulty,
            DiffString = string.IsNullOrWhiteSpace(payout.DiffString)
                ? ClientHandler.FormatDifficulty(payout.Difficulty)
                : payout.DiffString,
            Timestamp = DateTime.UtcNow,
            Source = "legacy-state"
        };
    }

    private string ComputeCandidateStateIdNoLock()
    {
        return ComputeStateIdNoLock(_state.OnDeckProofs, NormalizeCanonicalBlockHash(_state.CurrentTipBlockHash));
    }

    private List<BootShareProof> ValidateImportedProofs(
        IEnumerable<BootShareProof> shareProofs,
        IReadOnlyList<PayoutInfo> expectedWinners,
        string? expectedPrevBlockHash,
        string source)
    {
        var proofs = shareProofs
            .Select(CloneProof)
            .OrderByDescending(x => x.Difficulty)
            .ThenBy(x => x.ShareId, StringComparer.Ordinal)
            .ToList();

        var validatedProofs = new List<BootShareProof>(proofs.Count);
        foreach (var proof in proofs)
        {
            BootShareValidationResult validation = _shareVerifier.ValidateShare(proof, expectedWinners, expectedPrevBlockHash);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.RejectionReason ?? "Imported share proof is invalid.");
            }

            validatedProofs.Add(CreateProofNoLock(validation, source, proof.Timestamp));
        }

        return validatedProofs
            .OrderByDescending(x => x.Difficulty)
            .ThenBy(x => x.ShareId, StringComparer.Ordinal)
            .ToList();
    }

    private List<PayoutInfo> BuildPayoutsFromProofs(IEnumerable<BootShareProof> proofs)
    {
        var list = proofs.ToList();
        if (list.Count == 0)
        {
            return [];
        }

        ulong reward = Program.BLOCK_REWARD / ((ulong)list.Count + 1);
        return list.Select(proof => new PayoutInfo
        {
            Value = reward,
            Address = proof.MinerAddress,
            Username = string.IsNullOrWhiteSpace(proof.Username) ? proof.MinerAddress : proof.Username,
            Difficulty = proof.Difficulty,
            DiffString = string.IsNullOrWhiteSpace(proof.DiffString)
                ? ClientHandler.FormatDifficulty(proof.Difficulty)
                : proof.DiffString
        }).ToList();
    }

    private List<PayoutInfo> BuildCoinbaseOutputsNoLock(IEnumerable<PayoutInfo> payouts)
    {
        var compressed = new List<PayoutInfo>();
        var indexByScript = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var payout in payouts)
        {
            string normalizedAddress = BitcoinScript.NormalizeAddress(payout.Address);
            string scriptPubKeyHex = BitcoinScript.AddressToScriptPubKeyHex(normalizedAddress);
            if (indexByScript.TryGetValue(scriptPubKeyHex, out int existingIndex))
            {
                compressed[existingIndex].Value += payout.Value;
                continue;
            }

            indexByScript[scriptPubKeyHex] = compressed.Count;
            compressed.Add(new PayoutInfo
            {
                Value = payout.Value,
                Address = normalizedAddress,
                Username = string.IsNullOrWhiteSpace(payout.Username) ? normalizedAddress : payout.Username,
                Difficulty = payout.Difficulty,
                DiffString = payout.DiffString
            });
        }

        return ClonePayouts(compressed);
    }

    private bool WinnersMatch(List<PayoutInfo> expected, List<PayoutInfo> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i].Value != actual[i].Value)
            {
                return false;
            }

            string expectedScript = BitcoinScript.AddressToScriptPubKeyHex(expected[i].Address);
            string actualScript = BitcoinScript.AddressToScriptPubKeyHex(actual[i].Address);
            if (!string.Equals(expectedScript, actualScript, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Math.Abs(expected[i].Difficulty - actual[i].Difficulty) > 0.0000001)
            {
                return false;
            }
        }

        return true;
    }

    private string ComputeStateIdFromPayoutsNoLock(IEnumerable<PayoutInfo> payouts, string? blockHash)
    {
        var pseudoProofs = payouts.Select(x => new BootShareProof
        {
            MinerAddress = x.Address,
            ScriptPubKeyHex = BitcoinScript.TryAddressToScriptPubKey(x.Address, out var script)
                ? Convert.ToHexString(script).ToLowerInvariant()
                : string.Empty,
            Difficulty = x.Difficulty
        });

        return ComputeStateIdNoLock(pseudoProofs, blockHash);
    }

    private string ComputeStateIdNoLock(IEnumerable<BootShareProof> shares, string? blockHash)
    {
        var builder = new StringBuilder();
        builder.Append("boot-protocol-state").Append('\n');
        builder.Append(_poolConfig.BootProtocolVersion).Append('\n');
        builder.Append(_poolConfig.BootNetworkId).Append('\n');
        builder.Append(NormalizeCanonicalBlockHash(blockHash) ?? string.Empty).Append('\n');

        int index = 0;
        foreach (var share in shares
                     .OrderByDescending(x => x.Difficulty)
                     .ThenBy(x => x.ShareId, StringComparer.Ordinal))
        {
            builder.Append(index++).Append('|');
            builder.Append(share.ScriptPubKeyHex).Append('|');
            builder.Append(share.Difficulty.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            builder.Append(share.ShareId).Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeCanonicalBlockHash(string? blockHash)
    {
        string normalized = BitcoinHashes.NormalizeHex(blockHash);
        if (normalized.Length != 64)
        {
            return null;
        }

        for (int index = 0; index < normalized.Length; index++)
        {
            if (!Uri.IsHexDigit(normalized[index]))
            {
                return null;
            }
        }

        return normalized;
    }

    private async Task NotifyWinnersListChangedAsync(string reason)
    {
        Func<string, Task>? handlers = WinnersListChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (Func<string, Task> handler in handlers.GetInvocationList().Cast<Func<string, Task>>())
        {
            try
            {
                await handler(reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A WinnersListChanged handler failed for reason {Reason}.", reason);
            }
        }
    }

    private static string ComputeShareId(string headerHex, string coinbaseHex, string minerAddress)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{headerHex}|{coinbaseHex}|{minerAddress}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void UpsertArchivedBundleNoLock(BootStateBundle bundle)
    {
        _state.ArchivedStateBundles.RemoveAll(existing =>
            string.Equals(existing.StateId, bundle.StateId, StringComparison.OrdinalIgnoreCase));
        _state.ArchivedStateBundles.Insert(0, CloneBundle(bundle));

        while (_state.ArchivedStateBundles.Count > _poolConfig.MaxStateBundleHistory)
        {
            _state.ArchivedStateBundles.RemoveAt(_state.ArchivedStateBundles.Count - 1);
        }
    }

    private bool UpsertPeerNoLock(string endpoint, string status, double? latencyMs, DateTime? lastSeenUtc, bool persistStatusOnly)
    {
        string normalized = NormalizePeerEndpoint(endpoint);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(GetSelfEndpoint()) &&
            string.Equals(normalized, GetSelfEndpoint(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existing = _state.Peers.FirstOrDefault(x => string.Equals(x.Endpoint, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            if (_state.Peers.Count >= _poolConfig.MaxPeers)
            {
                return false;
            }

            _state.Peers.Add(new BootPeerStatus
            {
                Endpoint = normalized,
                Status = status,
                LatencyMs = latencyMs,
                LastSeenUtc = lastSeenUtc
            });
            return true;
        }

        bool changed = false;
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(existing.Status, status, StringComparison.Ordinal))
        {
            existing.Status = status;
            changed = true;
        }

        if (latencyMs.HasValue && existing.LatencyMs != latencyMs)
        {
            existing.LatencyMs = latencyMs;
            changed = true;
        }

        if (lastSeenUtc.HasValue && existing.LastSeenUtc != lastSeenUtc)
        {
            existing.LastSeenUtc = lastSeenUtc;
            changed = true;
        }

        return changed && !persistStatusOnly ? true : changed;
    }

    private static string NormalizePeerEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return string.Empty;
        }

        return endpoint.Trim().TrimEnd('/');
    }

    private void SeedSeenSharesNoLock()
    {
        foreach (var proof in _state.OnDeckProofs)
        {
            RememberShareIdNoLock(proof.ShareId);
        }

        foreach (var bundle in _state.ArchivedStateBundles)
        {
            foreach (var proof in bundle.ShareProofs)
            {
                RememberShareIdNoLock(proof.ShareId);
            }
        }
    }

    private bool RememberShareIdNoLock(string shareId)
    {
        if (!_seenShareIds.Add(shareId))
        {
            return false;
        }

        _seenShareQueue.Enqueue(shareId);
        while (_seenShareQueue.Count > MaxSeenShareIds)
        {
            string expired = _seenShareQueue.Dequeue();
            _seenShareIds.Remove(expired);
        }

        return true;
    }

    private static List<PayoutInfo> ClonePayouts(IEnumerable<PayoutInfo> payouts)
    {
        return payouts.Select(x => new PayoutInfo
        {
            Value = x.Value,
            Address = x.Address,
            Username = x.Username,
            Difficulty = x.Difficulty,
            DiffString = x.DiffString
        }).ToList();
    }

    private static BestShareRecord CloneBestShare(BestShareRecord? bestShare)
    {
        if (bestShare == null)
        {
            return new BestShareRecord();
        }

        return new BestShareRecord
        {
            Difficulty = bestShare.Difficulty,
            MinerAddress = bestShare.MinerAddress,
            Timestamp = bestShare.Timestamp
        };
    }

    private static BootShareProof CloneProof(BootShareProof proof)
    {
        return new BootShareProof
        {
            ShareId = proof.ShareId,
            MinerAddress = proof.MinerAddress,
            Username = proof.Username,
            ScriptPubKeyHex = proof.ScriptPubKeyHex,
            HeaderHex = proof.HeaderHex,
            CoinbaseHex = proof.CoinbaseHex,
            MerklePath = proof.MerklePath.ToList(),
            PrevBlockHash = proof.PrevBlockHash,
            Difficulty = proof.Difficulty,
            DiffString = proof.DiffString,
            Source = proof.Source,
            Timestamp = proof.Timestamp
        };
    }

    private static BootPeerStatus ClonePeer(BootPeerStatus peer)
    {
        return new BootPeerStatus
        {
            Endpoint = peer.Endpoint,
            Status = peer.Status,
            LatencyMs = peer.LatencyMs,
            LastSeenUtc = peer.LastSeenUtc
        };
    }

    private static BootStateBundle CloneBundle(BootStateBundle bundle)
    {
        return new BootStateBundle
        {
            StateId = bundle.StateId,
            Kind = bundle.Kind,
            ProtocolVersion = bundle.ProtocolVersion,
            NetworkId = bundle.NetworkId,
            LockedByBlockHash = bundle.LockedByBlockHash,
            ParentBlockHash = bundle.ParentBlockHash,
            CreatedAtUtc = bundle.CreatedAtUtc,
            TotalDifficulty = bundle.TotalDifficulty,
            WinnersList = ClonePayouts(bundle.WinnersList),
            ShareProofs = bundle.ShareProofs.Select(CloneProof).ToList(),
            Commitment = new BootCommitmentInfo
            {
                ProtocolVersion = bundle.Commitment.ProtocolVersion,
                NetworkId = bundle.Commitment.NetworkId,
                NextStateId = bundle.Commitment.NextStateId,
                OnChainSupported = bundle.Commitment.OnChainSupported,
                TagPreview = bundle.Commitment.TagPreview,
                SupportNote = bundle.Commitment.SupportNote
            }
        };
    }
}
