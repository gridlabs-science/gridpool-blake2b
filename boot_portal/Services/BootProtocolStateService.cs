using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using boot_portal.Models;
using boot_portal.Utils;
using Microsoft.AspNetCore.SignalR;

namespace boot_portal.Services;

public class BootProtocolStateService
{
    public const string GenesisFoundationAddress = "bc1qce93hy5rhg02s6aeu7mfdvxg76x66pqqtrvzs3";

    private readonly DateTime _serviceStartedUtc = DateTime.UtcNow;
    private readonly object _sync = new();
    private readonly PoolConfig _poolConfig;
    private readonly BootShareVerifier _shareVerifier;
    private readonly IHubContext<PoolStatsHub> _hubContext;
    private readonly ILogger<BootProtocolStateService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly JsonSerializerOptions _compactJsonOptions = new() { WriteIndented = false };
    private readonly Channel<BootShareProof> _acceptedShares = Channel.CreateUnbounded<BootShareProof>();
    private readonly HashSet<string> _seenShareIds = [];
    private readonly Queue<string> _seenShareQueue = new();
    private readonly List<BootShareDiagnosticTelemetry> _recentShareDiagnostics = [];
    private readonly Dictionary<string, BootDatumSessionTelemetry> _activeDatumSessions = new(StringComparer.Ordinal);
    private readonly List<BootStateBundle> _recentCandidateBundles = [];
    private Task? _deferredSaveTask;
    private bool _deferredSavePending;
    private Task? _deferredHistorySaveTask;
    private bool _deferredHistorySavePending;
    private static readonly TimeSpan DeferredSaveInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DeferredHistorySaveInterval = TimeSpan.FromSeconds(60);
    private const double SlowStateLockWaitWarningMs = 250;
    private const double SlowStateSaveWarningMs = 250;
    private const int MaxSeenShareIds = 20000;
    private const int MaxAcceptedParentBlockHashes = 100000;
    private const int MaxRecentRejectedShareDiagnostics = 1000;
    private const int MaxRecentCoinbaserDiagnostics = 1000;
    private const int MaxRecentDatumShareResponses = 2000;
    private const int MaxRecentDatumSessions = 5000;
    private const int MaxRecentNetworkEvents = 5000;
    private const int MaxRecentCandidateBundles = 512;

    private PoolState _state = new();

    public event Func<string, Task>? WinnersListChanged;
    public event Func<string, Task>? WorkTemplatesInvalidated;

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
        var waitStopwatch = Stopwatch.StartNew();
        lock (_sync)
        {
            double waitMs = waitStopwatch.Elapsed.TotalMilliseconds;
            if (waitMs >= SlowStateLockWaitWarningMs)
            {
                _logger.LogWarning(
                    "Slow state lock wait in GetWinnersList: {WaitMs:F1} ms (round={Round}, candidate={CandidateStateId}, onDeck={OnDeckCount}).",
                    waitMs,
                    _state.CurrentRoundNumber,
                    _state.CandidateStateId,
                    _state.OnDeckList.Count);
            }

            return ClonePayouts(_state.WinnersList);
        }
    }

    public List<PayoutInfo> GetCoinbaseOutputs()
    {
        var waitStopwatch = Stopwatch.StartNew();
        lock (_sync)
        {
            double waitMs = waitStopwatch.Elapsed.TotalMilliseconds;
            if (waitMs >= SlowStateLockWaitWarningMs)
            {
                _logger.LogWarning(
                    "Slow state lock wait in GetCoinbaseOutputs: {WaitMs:F1} ms (round={Round}, candidate={CandidateStateId}, onDeck={OnDeckCount}).",
                    waitMs,
                    _state.CurrentRoundNumber,
                    _state.CandidateStateId,
                    _state.OnDeckList.Count);
            }

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

            var recentCandidate = _recentCandidateBundles.FirstOrDefault(x =>
                string.Equals(x.StateId, stateId, StringComparison.OrdinalIgnoreCase));
            if (recentCandidate != null)
            {
                return CloneBundle(recentCandidate);
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

    public List<BootRoundHistoryEntry> GetRoundHistory(int limit = 24)
    {
        lock (_sync)
        {
            return BuildRoundHistoryNoLock(limit);
        }
    }

    public BootRoundHistoryEntry? GetRoundHistoryEntry(string stateId)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            return null;
        }

        lock (_sync)
        {
            List<BootStateBundle> completedRounds = _state.ArchivedStateBundles
                .Where(x => x.WinnersList.Count > 0 || x.ProofWinnersList.Count > 0)
                .ToList();
            HashSet<string> canonicalStateIds = BuildCanonicalStateIdSetNoLock();
            int index = completedRounds.FindIndex(x =>
                string.Equals(x.StateId, stateId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return null;
            }

            BootStateBundle? priorBundle = index + 1 < completedRounds.Count
                ? completedRounds[index + 1]
                : null;
            return BuildRoundHistoryEntryNoLock(
                completedRounds[index],
                canonicalStateIds.Contains(completedRounds[index].StateId),
                priorBundle);
        }
    }

    public List<BootPeerStatus> GetPeers()
    {
        lock (_sync)
        {
            return _state.Peers.Select(ClonePeer).ToList();
        }
    }

    public BootHashrateSeriesDto GetHashrateSeries(string? windowKey = null)
    {
        lock (_sync)
        {
            return BuildHashrateSeriesNoLock(windowKey);
        }
    }

    public BootShareDiagnosticsSeriesDto GetShareDiagnostics(
        string? windowKey = "12h",
        string? source = null,
        bool? accepted = false,
        int limit = 500,
        string? minerAddress = null,
        string? category = null)
    {
        lock (_sync)
        {
            return BuildShareDiagnosticsSeriesNoLock(windowKey, source, accepted, limit, minerAddress, category);
        }
    }

    public BootCoinbaserDiagnosticsSeriesDto GetCoinbaserDiagnostics(
        string? windowKey = "12h",
        int limit = 500,
        string? remoteEndpoint = null,
        bool? temporarySlotZero = null)
    {
        lock (_sync)
        {
            return BuildCoinbaserDiagnosticsSeriesNoLock(windowKey, limit, remoteEndpoint, temporarySlotZero);
        }
    }

    public BootDatumShareResponseSeriesDto GetDatumShareResponses(
        string? windowKey = "12h",
        int limit = 500,
        string? remoteEndpoint = null,
        bool? accepted = null,
        string? reason = null)
    {
        lock (_sync)
        {
            return BuildDatumShareResponseSeriesNoLock(windowKey, limit, remoteEndpoint, accepted, reason);
        }
    }

    public BootDatumSessionSeriesDto GetDatumSessions(
        string? windowKey = "12h",
        int limit = 500,
        string? remoteEndpoint = null,
        bool? active = null,
        string? protocol = null)
    {
        lock (_sync)
        {
            return BuildDatumSessionSeriesNoLock(windowKey, limit, remoteEndpoint, active, protocol);
        }
    }

    public BootNetworkEventSeriesDto GetNetworkEvents(
        string? windowKey = "12h",
        int limit = 500,
        string? eventType = null,
        string? source = null)
    {
        lock (_sync)
        {
            return BuildNetworkEventSeriesNoLock(windowKey, limit, eventType, source);
        }
    }

    public void RecordDatumSessionOpened(string sessionId, string remoteEndpoint, DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            var session = FindOrCreateDatumSessionNoLock(sessionId, remoteEndpoint, effectiveTimestampUtc);
            session.RemoteEndpoint = string.IsNullOrWhiteSpace(remoteEndpoint) ? session.RemoteEndpoint : remoteEndpoint;
            session.StartedUtc = effectiveTimestampUtc;
            session.LastActivityUtc = effectiveTimestampUtc;
            session.LastActivityType = "opened";
            TrimDatumSessionsNoLock(effectiveTimestampUtc);
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordDatumSessionProtocol(string sessionId, string protocol, DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(protocol))
        {
            return;
        }

        lock (_sync)
        {
            if (!TryGetDatumSessionNoLock(sessionId, out var session))
            {
                return;
            }

            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            session.Protocol = protocol;
            session.LastActivityUtc = effectiveTimestampUtc;
            session.LastActivityType = "protocol";
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordDatumSessionHello(
        string sessionId,
        string? clientIdentityKey,
        string? clientEncryptIdentityKey,
        DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!TryGetDatumSessionNoLock(sessionId, out var session))
            {
                return;
            }

            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            session.HandshakeCompleted = true;
            session.HelloCount += 1;
            session.ClientIdentityKey = clientIdentityKey ?? string.Empty;
            session.ClientEncryptIdentityKey = clientEncryptIdentityKey ?? string.Empty;
            session.HelloReceivedUtc ??= effectiveTimestampUtc;
            session.HandshakeMs ??= Math.Max(0, (effectiveTimestampUtc - session.StartedUtc).TotalMilliseconds);
            session.LastActivityUtc = effectiveTimestampUtc;
            session.LastActivityType = "hello";
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordDatumSessionPayoutLock(string sessionId, string? payoutAddress, DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(payoutAddress))
        {
            return;
        }

        lock (_sync)
        {
            if (!TryGetDatumSessionNoLock(sessionId, out var session))
            {
                return;
            }

            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            session.LockedPayoutAddress = BitcoinScript.NormalizeAddress(payoutAddress);
            session.PayoutLockedUtc ??= effectiveTimestampUtc;
            session.LastActivityUtc = effectiveTimestampUtc;
            session.LastActivityType = "payout-lock";
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordDatumSessionCoinbaserFetch(string sessionId, DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!TryGetDatumSessionNoLock(sessionId, out var session))
            {
                return;
            }

            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            session.CoinbaserFetchCount += 1;
            session.LastCoinbaserFetchUtc = effectiveTimestampUtc;
            session.LastActivityUtc = effectiveTimestampUtc;
            session.LastActivityType = "coinbaser-fetch";
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordDatumSessionRefreshRequest(string sessionId, DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!TryGetDatumSessionNoLock(sessionId, out var session))
            {
                return;
            }

            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            session.RefreshRequestCount += 1;
            session.LastRefreshRequestUtc = effectiveTimestampUtc;
            session.LastActivityUtc = effectiveTimestampUtc;
            session.LastActivityType = "refresh-request";
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordDatumSessionShareOutcome(
        string sessionId,
        bool accepted,
        bool affectedOnDeck,
        DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!TryGetDatumSessionNoLock(sessionId, out var session))
            {
                return;
            }

            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            session.ShareResponseCount += 1;
            if (accepted)
            {
                session.AcceptedShareCount += 1;
            }
            else
            {
                session.RejectedShareCount += 1;
            }

            if (affectedOnDeck)
            {
                session.AffectedOnDeckCount += 1;
            }

            session.LastShareResponseUtc = effectiveTimestampUtc;
            session.LastActivityUtc = effectiveTimestampUtc;
            session.LastActivityType = accepted ? "share-accepted" : "share-rejected";
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void CompleteDatumSession(
        string sessionId,
        string closeDisposition,
        string? closeReason = null,
        bool serverInitiated = false,
        string? serverCloseEventType = null,
        DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!TryGetDatumSessionNoLock(sessionId, out var session))
            {
                return;
            }

            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            session.ServerInitiatedClose = serverInitiated;
            session.ServerCloseEventType = serverCloseEventType;
            session.CloseDisposition = string.IsNullOrWhiteSpace(closeDisposition) ? "closed" : closeDisposition;
            session.CloseReason = closeReason;
            session.ClosedUtc = effectiveTimestampUtc;
            session.DurationMs = Math.Max(0, (effectiveTimestampUtc - session.StartedUtc).TotalMilliseconds);
            session.IdleBeforeCloseMs = session.LastActivityUtc.HasValue
                ? Math.Max(0, (effectiveTimestampUtc - session.LastActivityUtc.Value).TotalMilliseconds)
                : null;
            if (!session.LastActivityUtc.HasValue)
            {
                session.LastActivityUtc = effectiveTimestampUtc;
                session.LastActivityType = "closed-without-activity";
            }

            _activeDatumSessions.Remove(sessionId);
            TrimDatumSessionsNoLock(effectiveTimestampUtc);
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordCoinbaserFetch(
        string sessionId,
        string source,
        string remoteEndpoint,
        string? clientIdentityPreview,
        long requestSequence,
        ulong rewardValue,
        ulong teamPayoutTotal,
        ulong slotZeroValue,
        string slotZeroAddress,
        bool usingTemporarySlotZero,
        int winnersCount,
        int coinbaseOutputCount,
        int responsePayloadBytes,
        double durationMs,
        double parseDurationMs,
        double stateReadDurationMs,
        double buildDurationMs,
        double serializeDurationMs,
        double sendDurationMs,
        DateTime? timestampUtc = null)
    {
        lock (_sync)
        {
            DateTime effectiveTimestampUtc = timestampUtc ?? DateTime.UtcNow;
            _state.RecentCoinbaserDiagnostics.Add(new BootCoinbaserFetchTelemetry
            {
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source,
                RemoteEndpoint = remoteEndpoint,
                ClientIdentityPreview = clientIdentityPreview ?? string.Empty,
                RequestSequence = requestSequence,
                RewardValue = rewardValue,
                TeamPayoutTotal = teamPayoutTotal,
                SlotZeroValue = slotZeroValue,
                SlotZeroAddress = slotZeroAddress,
                UsingTemporarySlotZero = usingTemporarySlotZero,
                WinnersCount = winnersCount,
                CoinbaseOutputCount = coinbaseOutputCount,
                ResponsePayloadBytes = responsePayloadBytes,
                DurationMs = durationMs,
                ParseDurationMs = parseDurationMs,
                StateReadDurationMs = stateReadDurationMs,
                BuildDurationMs = buildDurationMs,
                SerializeDurationMs = serializeDurationMs,
                SendDurationMs = sendDurationMs,
                CurrentRoundNumber = _state.CurrentRoundNumber,
                CurrentStateId = _state.CurrentStateId,
                CandidateStateId = _state.CandidateStateId,
                CurrentTipBlockHash = _state.CurrentTipBlockHash,
                CurrentTipBlockHeight = _state.CurrentTipBlockHeight,
                TimestampUtc = effectiveTimestampUtc
            });

            TrimCoinbaserDiagnosticsNoLock(effectiveTimestampUtc);
            RequestDeferredHistorySaveNoLock();
        }
    }

    public void RecordDatumShareResponse(BootDatumShareResponseTelemetry telemetry)
    {
        lock (_sync)
        {
            DateTime effectiveTimestampUtc = telemetry.TimestampUtc == default
                ? DateTime.UtcNow
                : telemetry.TimestampUtc;
            telemetry.TimestampUtc = effectiveTimestampUtc;
            telemetry.CurrentRoundNumber = _state.CurrentRoundNumber;
            telemetry.CurrentStateId = _state.CurrentStateId;
            telemetry.CandidateStateId = _state.CandidateStateId;
            telemetry.CurrentTipBlockHash = _state.CurrentTipBlockHash;
            telemetry.CurrentTipBlockHeight = _state.CurrentTipBlockHeight;

            _state.RecentDatumShareResponses.Add(CloneDatumShareResponse(telemetry));
            TrimDatumShareResponsesNoLock(effectiveTimestampUtc);
            RequestDeferredHistorySaveNoLock();
        }

        double slowThresholdMs = GetDatumShareResponseSlowMs();
        if (telemetry.TotalDurationMs >= slowThresholdMs)
        {
            _logger.LogWarning(
                "Slow DATUM share response to {RemoteEndpoint}: {TotalMs:F1} ms (accepted={Accepted}, reason={Reason}, job={JobId}, coinbase={CoinbaseId}, nonceOnly={NonceOnly}, cached={Cached}, difficulty={Difficulty}).",
                telemetry.RemoteEndpoint,
                telemetry.TotalDurationMs,
                telemetry.Accepted,
                telemetry.RejectionReason ?? "none",
                telemetry.JobId,
                telemetry.CoinbaseId,
                telemetry.NonceOnlySubmit,
                telemetry.UsedCachedJob,
                telemetry.Difficulty);
        }
    }

    public void RecordExternalNetworkEvent(
        string eventType,
        string source,
        string? message,
        string? blockHash = null,
        long? blockHeight = null,
        DateTime? timestampUtc = null)
    {
        lock (_sync)
        {
            RecordNetworkEventNoLock(eventType, source, message, blockHash, blockHeight, timestampUtc);
            RequestDeferredHistorySaveNoLock();
        }
    }

    public string? GetKnownDatumPayoutAddress(string? clientIdentity)
    {
        if (string.IsNullOrWhiteSpace(clientIdentity))
        {
            return null;
        }

        lock (_sync)
        {
            return _state.KnownDatumPayoutAddresses.TryGetValue(clientIdentity, out string? address)
                ? address
                : null;
        }
    }

    public void RememberDatumPayoutAddress(string? clientIdentity, string? payoutAddress)
    {
        if (string.IsNullOrWhiteSpace(clientIdentity) || string.IsNullOrWhiteSpace(payoutAddress))
        {
            return;
        }

        string normalizedAddress = BitcoinScript.NormalizeAddress(payoutAddress);
        lock (_sync)
        {
            if (_state.KnownDatumPayoutAddresses.TryGetValue(clientIdentity, out string? existing) &&
                string.Equals(BitcoinScript.NormalizeAddress(existing), normalizedAddress, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _state.KnownDatumPayoutAddresses[clientIdentity] = normalizedAddress;
            RequestDeferredSaveNoLock();
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
        if (!_poolConfig.EnableAdminApi || string.IsNullOrWhiteSpace(_poolConfig.AdminApiKey))
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
                RequestDeferredSaveNoLock();
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
                RequestDeferredSaveNoLock();
            }
        }
    }

    public void UpdatePeerHeartbeat(string endpoint, string status, double? latencyMs, DateTime lastSeenUtc)
    {
        lock (_sync)
        {
            if (UpsertPeerNoLock(endpoint, status, latencyMs, lastSeenUtc, persistStatusOnly: true))
            {
                RequestDeferredSaveNoLock();
            }
        }
    }

    public void MarkPeerFailure(string endpoint, string status)
    {
        lock (_sync)
        {
            if (UpsertPeerNoLock(endpoint, status, null, null, persistStatusOnly: true))
            {
                RequestDeferredSaveNoLock();
            }
        }
    }

    public async Task<ShareRecordingResult> RecordShareAsync(RecordedShareSubmission share)
    {
        List<PayoutInfo> winnersSnapshot;
        string currentStateSnapshot;
        List<string> acceptedParentBlockHashesSnapshot;
        lock (_sync)
        {
            winnersSnapshot = ClonePayouts(_state.WinnersList);
            currentStateSnapshot = _state.CurrentStateId;
            acceptedParentBlockHashesSnapshot = GetAcceptedParentBlockHashesNoLock();
        }

        BootShareValidationResult validation = _shareVerifier.ValidateShare(share, winnersSnapshot, acceptedParentBlockHashesSnapshot);
        if (!validation.IsValid && IsWrongParentRejection(validation.RejectionReason))
        {
            BootShareValidationResult freshParentValidation = _shareVerifier.ValidateShare(
                share,
                winnersSnapshot,
                expectedPrevBlockHashes: []);
            if (IsTrustedFreshParentSource(share.Source))
            {
                if (!freshParentValidation.IsValid)
                {
                    RecordFreshParentRetryEvent(
                        "fresh-parent-retry-failed",
                        share.Source,
                        share.PrevBlockHash,
                        $"Fresh-parent retry failed validation after ignoring parent mismatch: {freshParentValidation.RejectionReason ?? "Unknown reason"}.");
                    validation = freshParentValidation;
                }
                else if (freshParentValidation.Difficulty < 1)
                {
                    RecordFreshParentRetryEvent(
                        "fresh-parent-retry-low-difficulty",
                        share.Source,
                        freshParentValidation.PrevBlockHash,
                        $"Fresh-parent retry validated but computed difficulty {freshParentValidation.Difficulty.ToString("F2", CultureInfo.InvariantCulture)} was below the floor.");
                    validation = freshParentValidation;
                }
                else if (TryLearnFreshParentFromTrustedShare(share.Source, freshParentValidation, currentStateSnapshot))
                {
                    validation = freshParentValidation;
                }
                else
                {
                    RecordFreshParentRetryEvent(
                        "fresh-parent-learn-failed",
                        share.Source,
                        freshParentValidation.PrevBlockHash,
                        "Fresh-parent retry validated, but the parent could not be learned before the state changed.");
                }
            }
        }

        if (!validation.IsValid)
        {
            BootNetworkStatusDto networkStatus;
            DateTime nowUtc = DateTime.UtcNow;
            lock (_sync)
            {
                RecordShareDiagnosticNoLock(
                    share.Source,
                    share.MinerAddress,
                    string.IsNullOrWhiteSpace(share.Username) ? share.MinerAddress : share.Username,
                    accepted: false,
                    affectedOnDeck: false,
                    validation.RejectionReason,
                    share.Difficulty,
                    nowUtc);
                RequestDeferredHistorySaveNoLock();
                networkStatus = BuildNetworkStatusNoLock();
            }

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
                NetworkStatus = networkStatus
            };
        }

        if (validation.Difficulty < 1)
        {
            BootNetworkStatusDto networkStatus;
            DateTime nowUtc = DateTime.UtcNow;
            lock (_sync)
            {
                RecordShareDiagnosticNoLock(
                    share.Source,
                    validation.MinerAddress,
                    string.IsNullOrWhiteSpace(validation.Username) ? validation.MinerAddress : validation.Username,
                    accepted: false,
                    affectedOnDeck: false,
                    "Low difficulty",
                    validation.Difficulty,
                    nowUtc);
                RequestDeferredHistorySaveNoLock();
                networkStatus = BuildNetworkStatusNoLock();
            }

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
                NetworkStatus = networkStatus
            };
        }

        ShareRecordingResult result;
        bool shouldRelay = false;
        bool shouldNotifyNetwork = false;
        lock (_sync)
        {
            if (!string.Equals(currentStateSnapshot, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase))
            {
                RecordShareDiagnosticNoLock(
                    share.Source,
                    validation.MinerAddress,
                    string.IsNullOrWhiteSpace(validation.Username) ? validation.MinerAddress : validation.Username,
                    accepted: false,
                    affectedOnDeck: false,
                    "Round changed during validation",
                    validation.Difficulty,
                    DateTime.UtcNow);
                RequestDeferredHistorySaveNoLock();
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

            if (!IsAcceptedParentBlockHashNoLock(validation.PrevBlockHash))
            {
                RecordShareDiagnosticNoLock(
                    share.Source,
                    validation.MinerAddress,
                    string.IsNullOrWhiteSpace(validation.Username) ? validation.MinerAddress : validation.Username,
                    accepted: false,
                    affectedOnDeck: false,
                    "Accepted parent set changed during validation",
                    validation.Difficulty,
                    DateTime.UtcNow);
                RequestDeferredHistorySaveNoLock();
                return new ShareRecordingResult
                {
                    Accepted = false,
                    RejectionReason = "Accepted parent set changed during validation",
                    ComputedDifficulty = validation.Difficulty,
                    BestShare = CloneBestShare(_state.BestShare),
                    OnDeckList = ClonePayouts(_state.OnDeckList),
                    NetworkStatus = BuildNetworkStatusNoLock()
                };
            }

            BootShareProof proof = CreateProofNoLock(validation, share.Source);
            if (!RememberShareIdNoLock(proof.ShareId))
            {
                RecordShareDiagnosticNoLock(
                    share.Source,
                    proof.MinerAddress,
                    string.IsNullOrWhiteSpace(proof.Username) ? proof.MinerAddress : proof.Username,
                    accepted: false,
                    affectedOnDeck: false,
                    "Duplicate share",
                    validation.Difficulty,
                    proof.Timestamp);
                RequestDeferredHistorySaveNoLock();
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

            RecordAcceptedShareTelemetryNoLock(proof);
            RecordShareDiagnosticNoLock(
                share.Source,
                proof.MinerAddress,
                string.IsNullOrWhiteSpace(proof.Username) ? proof.MinerAddress : proof.Username,
                accepted: true,
                affectedOnDeck: affectedOnDeck,
                rejectionReason: null,
                difficulty: validation.Difficulty,
                timestampUtc: proof.Timestamp);
            bool capturedHashrateSample = MaybeCaptureHashrateSampleNoLock(proof.Timestamp, force: false);
            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            CacheCurrentCandidateBundleNoLock();
            RequestDeferredSaveNoLock();
            RequestDeferredHistorySaveNoLock();

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
            shouldNotifyNetwork = newRecord || affectedOnDeck || capturedHashrateSample;
        }

        if (result.NewRecord)
        {
            QueueRealtimeSend(_hubContext.Clients.All.SendAsync("UpdateRecord", result.BestShare), "UpdateRecord");
        }

        if (result.AffectedOnDeck)
        {
            QueueRealtimeSend(_hubContext.Clients.All.SendAsync("UpdateOnDeck", result.OnDeckList), "UpdateOnDeck");
        }

        if (shouldNotifyNetwork)
        {
            QueueRealtimeSend(_hubContext.Clients.All.SendAsync("UpdateNetworkState", result.NetworkStatus), "UpdateNetworkState");
        }
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

    public async Task<RoundRotationResult> RotateToNextRoundAsync(string blockHash, string source, bool manual, long? blockHeight = null)
    {
        RoundRotationResult result;
        bool winnersChanged = false;
        lock (_sync)
        {
            List<PayoutInfo> previousWinnersSnapshot = ClonePayouts(_state.WinnersList);
            string previousStateId = _state.CurrentStateId;
            int previousCurrentRoundNumber = _state.CurrentRoundNumber;
            string? previousTipBlockHash = NormalizeCanonicalBlockHash(_state.CurrentTipBlockHash);
            long? previousTipBlockHeight = _state.CurrentTipBlockHeight;
            string? submittedBlockHash = NormalizeCanonicalBlockHash(blockHash);
            string? effectiveBlockHash = manual
                ? submittedBlockHash ?? previousTipBlockHash
                : submittedBlockHash;
            long? effectiveBlockHeight = manual
                ? blockHeight ?? previousTipBlockHeight
                : blockHeight;

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

            if (manual && _state.OnDeckProofs.Count == 0)
            {
                _state.CurrentTipBlockHash = effectiveBlockHash;
                _state.CurrentTipBlockHeight = effectiveBlockHeight;
                ResetAcceptedParentBlockHashesNoLock(effectiveBlockHash);
                _state.LastRotationUtc = DateTime.UtcNow;
                _state.OnDeckProofs = [];
                _state.OnDeckList = [];
            }
            else
            {
                BootStateBundle lockedBundle = BuildBundleFromCurrentCandidateNoLock();
                lockedBundle.StateId = ComputeStateIdNoLock(_state.OnDeckProofs, effectiveBlockHash);
                lockedBundle.PreviousStateId = previousStateId;
                lockedBundle.Kind = manual ? "manual-rotation" : source;
                lockedBundle.CurrentRoundNumber = previousCurrentRoundNumber + 1;
                lockedBundle.LockedByBlockHash = effectiveBlockHash;
                lockedBundle.LockedByBlockHeight = effectiveBlockHeight;
                lockedBundle.ParentBlockHash = previousTipBlockHash;
                lockedBundle.ParentBlockHeight = previousTipBlockHeight;
                lockedBundle.CreatedAtUtc = DateTime.UtcNow;
                lockedBundle.ValidParentBlockHashes = GetAcceptedParentBlockHashesNoLock();
                lockedBundle.ProofWinnersList = previousWinnersSnapshot;
                lockedBundle.Commitment = BuildCommitmentNoLock();

                _state.CurrentTipBlockHash = effectiveBlockHash;
                _state.CurrentTipBlockHeight = effectiveBlockHeight;
                PreserveAcceptedParentContinuityAfterRotationNoLock(previousTipBlockHash, effectiveBlockHash);
                _state.LastRotationUtc = DateTime.UtcNow;
                _state.CurrentStateId = lockedBundle.StateId;
                _state.CurrentRoundNumber = lockedBundle.CurrentRoundNumber;
                _state.WinnersList = ClonePayouts(lockedBundle.WinnersList);
                _state.OnDeckProofs = [];
                _state.OnDeckList = [];
                UpsertArchivedBundleNoLock(lockedBundle);
                winnersChanged = true;
            }

            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            CacheCurrentCandidateBundleNoLock();
            RecordNetworkEventNoLock(
                manual ? "manual-reset" : "round-rotation",
                source,
                manual && !winnersChanged
                    ? "Manual reset cleared the active On Deck state and preserved the current Winners List."
                    : manual ? "Manual reset completed." : $"Round rotated from {source}.",
                effectiveBlockHash,
                effectiveBlockHeight);
            RequestDeferredSaveNoLock();
            RequestDeferredHistorySaveNoLock();

            result = new RoundRotationResult
            {
                Rotated = !manual || winnersChanged,
                Reason = manual && !winnersChanged
                    ? "Manual reset cleared On Deck state and preserved the current Winners List"
                    : manual ? "Manual reset completed" : $"Round rotated from {source}",
                BlockHash = effectiveBlockHash,
                WinnersList = ClonePayouts(_state.WinnersList),
                OnDeckList = ClonePayouts(_state.OnDeckList),
                NetworkStatus = BuildNetworkStatusNoLock(),
                LockedStateBundle = winnersChanged ? GetStateBundle(_state.CurrentStateId) : null
            };
        }

        await _hubContext.Clients.All.SendAsync("UpdateOnDeck", result.OnDeckList);
        await _hubContext.Clients.All.SendAsync("UpdateNetworkState", result.NetworkStatus);
        await _hubContext.Clients.All.SendAsync("UpdateRoundHistory", GetRoundHistory());
        if (winnersChanged)
        {
            await _hubContext.Clients.All.SendAsync("UpdateWinners", result.WinnersList);
            await NotifyWinnersListChangedAsync(manual ? "manual-reset" : source);
        }
        return result;
    }

    public async Task<BootNetworkStatusDto> ResetHistoryToGenesisAsync()
    {
        BootNetworkStatusDto networkStatus;
        List<PayoutInfo> winnersSnapshot;
        List<PayoutInfo> onDeckSnapshot;

        lock (_sync)
        {
            List<BootPeerStatus> peers = _state.Peers.Select(ClonePeer).ToList();
            Dictionary<string, string> knownDatumPayouts = new(_state.KnownDatumPayoutAddresses, StringComparer.Ordinal);
            BestShareRecord bestShare = CloneBestShare(_state.BestShare);
            string? currentTipBlockHash = _state.CurrentTipBlockHash;
            long? currentTipBlockHeight = _state.CurrentTipBlockHeight;

            InitializeDefaultsNoLock();
            _state.Peers = peers;
            _state.KnownDatumPayoutAddresses = knownDatumPayouts;
            _state.BestShare = bestShare;
            _state.CurrentTipBlockHash = currentTipBlockHash;
            _state.CurrentTipBlockHeight = currentTipBlockHeight;
            _state.LastRotationUtc = DateTime.UtcNow;
            _state.LastTestingTriggerBlockHash = null;
            _state.LastTestingTriggerBlockHeight = null;
            _state.RecentAcceptedShares = [];
            _state.RecentRejectedShareDiagnostics = [];
            _state.RecentCoinbaserDiagnostics = [];
            _state.RecentDatumShareResponses = [];
            _state.RecentDatumSessions = [];
            _state.RecentNetworkEvents = [];
            _activeDatumSessions.Clear();
            _recentShareDiagnostics.Clear();
            _state.HashrateSamples = [];
            ResetAcceptedParentBlockHashesNoLock(currentTipBlockHash);
            RecordNetworkEventNoLock(
                "genesis-reset",
                "admin",
                "Reset local history back to the genesis Winners List.",
                currentTipBlockHash,
                currentTipBlockHeight);
            SaveStateNoLock();

            winnersSnapshot = ClonePayouts(_state.WinnersList);
            onDeckSnapshot = ClonePayouts(_state.OnDeckList);
            networkStatus = BuildNetworkStatusNoLock();
        }

        await _hubContext.Clients.All.SendAsync("UpdateWinners", winnersSnapshot);
        await _hubContext.Clients.All.SendAsync("UpdateOnDeck", onDeckSnapshot);
        await _hubContext.Clients.All.SendAsync("UpdateNetworkState", networkStatus);
        await _hubContext.Clients.All.SendAsync("UpdateRoundHistory", GetRoundHistory());
        await NotifyWinnersListChangedAsync("genesis-reset");
        await NotifyWorkTemplatesInvalidatedAsync("genesis-reset");
        return networkStatus;
    }

    public async Task<BootNetworkStatusDto> ObserveChainTipAsync(string blockHash, string source, long? blockHeight = null)
    {
        BootNetworkStatusDto status;
        string? normalizedBlockHash;
        long? effectiveBlockHeight;
        bool shouldRotateTestRound = false;
        bool metadataChanged = false;
        lock (_sync)
        {
            normalizedBlockHash = NormalizeCanonicalBlockHash(blockHash);
            if (string.IsNullOrWhiteSpace(normalizedBlockHash))
            {
                _logger.LogWarning("Ignored invalid chain tip hash from {Source}: {BlockHash}", source, blockHash);
                return BuildNetworkStatusNoLock();
            }

            effectiveBlockHeight = blockHeight;
            if (!effectiveBlockHeight.HasValue &&
                !string.IsNullOrWhiteSpace(_state.CurrentTipBlockHash) &&
                !BitcoinHashes.AreEquivalent(normalizedBlockHash, _state.CurrentTipBlockHash) &&
                _state.CurrentTipBlockHeight.HasValue)
            {
                // Most notification sources deliver blocks sequentially even if they omit height.
                // Infer the next height immediately for UI/history purposes, then allow exact data
                // from a later peer/backlog update to overwrite it if needed.
                effectiveBlockHeight = _state.CurrentTipBlockHeight.Value + 1;
            }

            if (BitcoinHashes.AreEquivalent(normalizedBlockHash, _state.CurrentTipBlockHash))
            {
                metadataChanged = UpdateKnownBlockHeightNoLock(normalizedBlockHash, effectiveBlockHeight);
                if (metadataChanged)
                {
                    RequestDeferredSaveNoLock();
                }
                return BuildNetworkStatusNoLock();
            }

            shouldRotateTestRound = ShouldTriggerTestingRoundResetNoLock(normalizedBlockHash);
            if (shouldRotateTestRound)
            {
                _state.LastTestingTriggerBlockHash = normalizedBlockHash;
                _state.LastTestingTriggerBlockHeight = effectiveBlockHeight;
                UpdateKnownBlockHeightNoLock(normalizedBlockHash, effectiveBlockHeight);
                RecordNetworkEventNoLock(
                    "chain-tip",
                    source,
                    "Observed chain tip that qualified for deterministic test rotation.",
                    normalizedBlockHash,
                    effectiveBlockHeight);
                RequestDeferredSaveNoLock();
                RequestDeferredHistorySaveNoLock();
                status = BuildNetworkStatusNoLock();
            }
            else
            {
                _state.CurrentTipBlockHash = normalizedBlockHash;
                _state.CurrentTipBlockHeight = effectiveBlockHeight;
                RememberAcceptedParentBlockHashNoLock(normalizedBlockHash);
                UpdateKnownBlockHeightNoLock(normalizedBlockHash, effectiveBlockHeight);
                _state.CandidateStateId = ComputeCandidateStateIdNoLock();
                CacheCurrentCandidateBundleNoLock();
                RecordNetworkEventNoLock("chain-tip", source, null, normalizedBlockHash, effectiveBlockHeight);
                RequestDeferredSaveNoLock();
                RequestDeferredHistorySaveNoLock();

                status = BuildNetworkStatusNoLock();
            }
        }

        if (shouldRotateTestRound && !string.IsNullOrWhiteSpace(normalizedBlockHash))
        {
            _logger.LogWarning(
                "Deterministic test round trigger fired from {Source}: {BlockHash}",
                source,
                normalizedBlockHash);
            RoundRotationResult rotation = await RotateToNextRoundAsync(
                normalizedBlockHash,
                $"test-trigger:{source}",
                manual: false,
                blockHeight: effectiveBlockHeight);
            return rotation.NetworkStatus;
        }

        _logger.LogInformation("Observed new chain tip from {Source}: {BlockHash}", source, blockHash);
        await _hubContext.Clients.All.SendAsync("UpdateNetworkState", status);
        await NotifyWorkTemplatesInvalidatedAsync($"chain-tip:{source}");
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
        List<string> acceptedParentBlockHashesSnapshot;
        lock (_sync)
        {
            winnersSnapshot = ClonePayouts(_state.WinnersList);
            currentTipSnapshot = _state.CurrentTipBlockHash;
            currentStateSnapshot = _state.CurrentStateId;
            acceptedParentBlockHashesSnapshot = GetAcceptedParentBlockHashesNoLock();
        }

        List<string> remoteAcceptedParentBlockHashes = NormalizeAcceptedParentBlockHashes(
            bundle.ValidParentBlockHashes
                .Append(bundle.ParentBlockHash ?? string.Empty)
                .Concat(bundle.ShareProofs.Select(proof => proof.PrevBlockHash)));
        List<string> validationParentBlockHashes = MergeAcceptedParentBlockHashes(
            acceptedParentBlockHashesSnapshot,
            remoteAcceptedParentBlockHashes);

        bool parentSetsOverlap =
            acceptedParentBlockHashesSnapshot.Count == 0 ||
            remoteAcceptedParentBlockHashes.Count == 0 ||
            acceptedParentBlockHashesSnapshot.Any(local =>
                remoteAcceptedParentBlockHashes.Any(remote => BitcoinHashes.AreEquivalent(local, remote)));

        bool currentTipIsCompatible =
            string.IsNullOrWhiteSpace(currentTipSnapshot) ||
            validationParentBlockHashes.Any(hash => BitcoinHashes.AreEquivalent(hash, currentTipSnapshot)) ||
            BitcoinHashes.AreEquivalent(bundle.ParentBlockHash, currentTipSnapshot);

        if (!parentSetsOverlap || !currentTipIsCompatible)
        {
            return false;
        }

        List<BootShareProof> validatedProofs;
        List<PayoutInfo> expectedPayouts;
        double totalDifficulty;

        try
        {
            IReadOnlyList<PayoutInfo> proofWinners = bundle.ProofWinnersList.Count > 0
                ? bundle.ProofWinnersList
                : winnersSnapshot;
            validatedProofs = ValidateImportedProofs(bundle.ShareProofs, proofWinners, validationParentBlockHashes, $"peer-state:{sourceEndpoint}");
            expectedPayouts = BuildPayoutsFromProofs(validatedProofs);
            totalDifficulty = validatedProofs.Sum(x => x.Difficulty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected candidate state bundle from {SourceEndpoint}.", sourceEndpoint);
            return false;
        }

        string expectedStateId = ComputeCandidateStateId(currentStateSnapshot, validatedProofs);
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
            if (!string.Equals(currentStateSnapshot, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            List<string> currentAcceptedParentBlockHashes = GetAcceptedParentBlockHashesNoLock();
            List<string> mergedAcceptedParentBlockHashes = MergeAcceptedParentBlockHashes(
                currentAcceptedParentBlockHashes,
                remoteAcceptedParentBlockHashes);
            if (!validatedProofs.All(proof =>
                    mergedAcceptedParentBlockHashes.Any(hash => BitcoinHashes.AreEquivalent(hash, proof.PrevBlockHash))))
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
            SetAcceptedParentBlockHashesNoLock(mergedAcceptedParentBlockHashes, _state.CurrentTipBlockHash);
            _state.CandidateStateId = bundle.StateId;
            CacheCandidateBundleNoLock(bundle);
            foreach (var proof in validatedProofs)
            {
                RememberShareIdNoLock(proof.ShareId);
            }
            RequestDeferredSaveNoLock();

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

    public async Task<bool> TryAdoptCurrentStateAsync(BootStateBundle bundle, string? observedTipBlockHash, long? observedTipBlockHeight, string sourceEndpoint)
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

        string? lockedTipSnapshot = NormalizeCanonicalBlockHash(bundle.LockedByBlockHash) ??
            NormalizeCanonicalBlockHash(observedTipBlockHash);
        string? observedTipSnapshot = NormalizeCanonicalBlockHash(observedTipBlockHash) ?? lockedTipSnapshot;
        long? lockedTipHeightSnapshot = bundle.LockedByBlockHeight ?? observedTipBlockHeight;
        bool localStateIsEmpty =
            currentWinnersSnapshot.Count == 0 ||
            (currentWinnersSnapshot.Count == 1 &&
             currentWinnersSnapshot[0].Difficulty <= 0 &&
             currentWinnersSnapshot[0].Value == Program.BLOCK_REWARD / 2);

        if (string.IsNullOrWhiteSpace(currentTipSnapshot) ||
            string.IsNullOrWhiteSpace(lockedTipSnapshot))
        {
            return false;
        }

        if (!localStateIsEmpty &&
            !string.IsNullOrWhiteSpace(observedTipSnapshot) &&
            !BitcoinHashes.AreEquivalent(currentTipSnapshot, observedTipSnapshot))
        {
            return false;
        }

        List<BootShareProof> validatedProofs;
        List<PayoutInfo> expectedPayouts;
        try
        {
            IReadOnlyList<PayoutInfo> proofWinners = bundle.ProofWinnersList.Count > 0
                ? bundle.ProofWinnersList
                : currentWinnersSnapshot;
            List<string> lockedStateParentBlockHashes = NormalizeAcceptedParentBlockHashes(
                bundle.ValidParentBlockHashes
                    .Append(bundle.ParentBlockHash ?? string.Empty)
                    .Concat(bundle.ShareProofs.Select(proof => proof.PrevBlockHash)));
            validatedProofs = ValidateImportedProofs(
                bundle.ShareProofs,
                proofWinners,
                lockedStateParentBlockHashes,
                $"peer-locked:{sourceEndpoint}");
            expectedPayouts = BuildPayoutsFromProofs(validatedProofs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected locked state bundle from {SourceEndpoint}.", sourceEndpoint);
            return false;
        }

        string expectedStateId = ComputeStateIdNoLock(validatedProofs, lockedTipSnapshot);
        string legacyExpectedStateId = ComputeStateIdNoLock(validatedProofs, null);
        bool stateIdMatches =
            string.Equals(expectedStateId, bundle.StateId, StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(bundle.LockedByBlockHash) &&
             string.Equals(legacyExpectedStateId, bundle.StateId, StringComparison.OrdinalIgnoreCase));
        if (!stateIdMatches)
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

            double remoteLockedTotalDifficulty = validatedProofs.Sum(x => x.Difficulty);
            double localLockedTotalDifficulty = _state.WinnersList.Sum(x => x.Difficulty);
            const double difficultyEpsilon = 0.0000001;
            bool remoteLooksStronger =
                localStateIsEmpty ||
                bundle.CurrentRoundNumber > _state.CurrentRoundNumber ||
                (bundle.CurrentRoundNumber == _state.CurrentRoundNumber &&
                 remoteLockedTotalDifficulty > localLockedTotalDifficulty + difficultyEpsilon) ||
                (bundle.CurrentRoundNumber == _state.CurrentRoundNumber &&
                 Math.Abs(remoteLockedTotalDifficulty - localLockedTotalDifficulty) <= difficultyEpsilon &&
                 string.CompareOrdinal(bundle.StateId ?? string.Empty, _state.CurrentStateId ?? string.Empty) > 0);
            if (!remoteLooksStronger)
            {
                return false;
            }

            _state.CurrentStateId = bundle.StateId;
            _state.CurrentRoundNumber = Math.Max(0, bundle.CurrentRoundNumber);
            _state.LastRotationUtc = bundle.CreatedAtUtc == default ? DateTime.UtcNow : bundle.CreatedAtUtc;
            _state.WinnersList = ClonePayouts(expectedPayouts);
            _state.CurrentTipBlockHash = observedTipSnapshot ?? currentTipSnapshot;
            _state.CurrentTipBlockHeight = observedTipBlockHeight ?? bundle.LockedByBlockHeight ?? _state.CurrentTipBlockHeight;
            TrimAcceptedParentBlockHashesToRoundNoLock(lockedTipSnapshot, _state.CurrentTipBlockHash);
            _state.OnDeckProofs = [];
            _state.OnDeckList = [];
            foreach (var proof in validatedProofs)
            {
                RememberShareIdNoLock(proof.ShareId);
            }

            BootStateBundle lockedBundle = CloneBundle(bundle);
            lockedBundle.ShareProofs = validatedProofs.Select(CloneProof).ToList();
            lockedBundle.WinnersList = ClonePayouts(expectedPayouts);
            lockedBundle.ProofWinnersList = ClonePayouts(bundle.ProofWinnersList.Count > 0
                ? bundle.ProofWinnersList
                : currentWinnersSnapshot);
            lockedBundle.PreviousStateId = bundle.PreviousStateId;
            lockedBundle.CurrentRoundNumber = Math.Max(0, bundle.CurrentRoundNumber);
            lockedBundle.StateId = string.IsNullOrWhiteSpace(bundle.LockedByBlockHash) ? legacyExpectedStateId : expectedStateId;
            lockedBundle.TotalDifficulty = remoteLockedTotalDifficulty;
            lockedBundle.LockedByBlockHash = lockedTipSnapshot;
            lockedBundle.LockedByBlockHeight = lockedTipHeightSnapshot;
            lockedBundle.ParentBlockHash = BitcoinHashes.NormalizeHex(bundle.ParentBlockHash);
            lockedBundle.ParentBlockHeight = bundle.ParentBlockHeight;
            lockedBundle.ValidParentBlockHashes = GetAcceptedParentBlockHashesNoLock();
            lockedBundle.Commitment = BuildCommitmentNoLock();
            UpsertArchivedBundleNoLock(lockedBundle);

            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            CacheCurrentCandidateBundleNoLock();
            RecordNetworkEventNoLock(
                "state-adopted",
                sourceEndpoint,
                "Adopted a stronger locked current state from a peer.",
                _state.CurrentTipBlockHash,
                _state.CurrentTipBlockHeight);
            RequestDeferredSaveNoLock();
            RequestDeferredHistorySaveNoLock();

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
            await _hubContext.Clients.All.SendAsync("UpdateRoundHistory", GetRoundHistory());
            await NotifyWinnersListChangedAsync($"adopted-state:{sourceEndpoint}");
        }

        return adopted;
    }

    public async Task<bool> TryBootstrapCurrentStateAsync(BootStateBundle bundle, string? observedTipBlockHash, long? observedTipBlockHeight, string sourceEndpoint)
    {
        if (!IsCompatiblePeerNetwork(bundle.ProtocolVersion, bundle.NetworkId))
        {
            _logger.LogDebug("Rejected bootstrap state from {SourceEndpoint}: incompatible network.", sourceEndpoint);
            return false;
        }

        if (bundle.WinnersList.Count > _poolConfig.SharedWinnerSlotCount)
        {
            _logger.LogDebug(
                "Rejected bootstrap state from {SourceEndpoint}: winners count {Count} exceeds configured shared slots {MaxCount}.",
                sourceEndpoint,
                bundle.WinnersList.Count,
                _poolConfig.SharedWinnerSlotCount);
            return false;
        }

        string? lockedTip = NormalizeCanonicalBlockHash(bundle.LockedByBlockHash);
        string? observedTip = NormalizeCanonicalBlockHash(observedTipBlockHash) ?? lockedTip;
        long? lockedTipHeight = bundle.LockedByBlockHeight ?? observedTipBlockHeight;
        List<string> bundleParentBlockHashes = bundle.ValidParentBlockHashes
            .Select(NormalizeCanonicalBlockHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(observedTip) || bundle.WinnersList.Count == 0)
        {
            _logger.LogDebug(
                "Rejected bootstrap state from {SourceEndpoint}: missing observed tip or winners list.",
                sourceEndpoint);
            return false;
        }

        BootNetworkStatusDto networkStatus;
        List<PayoutInfo> winnersSnapshot;
        List<PayoutInfo> onDeckSnapshot;
        bool adopted = false;

        lock (_sync)
        {
            string? localTip = NormalizeCanonicalBlockHash(_state.CurrentTipBlockHash);
            bool localStateIsEmpty = IsPlaceholderOrEmptyCurrentStateNoLock();
            bool hasEstablishedState =
                !localStateIsEmpty &&
                (_state.ArchivedStateBundles.Any(existing => existing.WinnersList.Count > 0 || existing.ShareProofs.Count > 0) ||
                 _state.WinnersList.Count > 1 ||
                 (_state.WinnersList.Count == 1 && _state.WinnersList[0].Difficulty > 0));

            if (hasEstablishedState)
            {
                _logger.LogDebug(
                    "Rejected bootstrap state from {SourceEndpoint}: local node already has an established state {StateId}.",
                    sourceEndpoint,
                    _state.CurrentStateId);
                return false;
            }

            if (!localStateIsEmpty &&
                !string.IsNullOrWhiteSpace(localTip) &&
                !BitcoinHashes.AreEquivalent(localTip, observedTip))
            {
                _logger.LogDebug(
                    "Rejected bootstrap state from {SourceEndpoint}: local tip {LocalTip} does not match observed remote tip {RemoteTip}.",
                    sourceEndpoint,
                    localTip,
                    observedTip);
                return false;
            }

            _state.CurrentTipBlockHash = observedTip;
            _state.CurrentTipBlockHeight = observedTipBlockHeight ?? bundle.LockedByBlockHeight;
            SetAcceptedParentBlockHashesNoLock(bundleParentBlockHashes, observedTip);
            _state.CurrentStateId = string.IsNullOrWhiteSpace(bundle.StateId)
                ? ComputeStateIdFromPayoutsNoLock(bundle.WinnersList, lockedTip)
                : bundle.StateId;
            _state.CurrentRoundNumber = Math.Max(0, bundle.CurrentRoundNumber);
            _state.LastRotationUtc = bundle.CreatedAtUtc == default ? DateTime.UtcNow : bundle.CreatedAtUtc;
            _state.WinnersList = ClonePayouts(bundle.WinnersList);
            _state.OnDeckProofs = [];
            _state.OnDeckList = [];

            BootStateBundle lockedBundle = CloneBundle(bundle);
            lockedBundle.PreviousStateId = bundle.PreviousStateId;
            lockedBundle.CurrentRoundNumber = Math.Max(0, bundle.CurrentRoundNumber);
            lockedBundle.LockedByBlockHash = lockedTip;
            lockedBundle.LockedByBlockHeight = lockedTipHeight;
            lockedBundle.ValidParentBlockHashes = GetAcceptedParentBlockHashesNoLock();
            lockedBundle.WinnersList = ClonePayouts(bundle.WinnersList);
            lockedBundle.ProofWinnersList = ClonePayouts(bundle.ProofWinnersList);
            lockedBundle.Commitment = BuildCommitmentNoLock();
            UpsertArchivedBundleNoLock(lockedBundle);

            _state.CandidateStateId = ComputeCandidateStateIdNoLock();
            CacheCurrentCandidateBundleNoLock();
            RecordNetworkEventNoLock(
                "state-bootstrapped",
                sourceEndpoint,
                "Bootstrapped current locked state from a peer.",
                _state.CurrentTipBlockHash,
                _state.CurrentTipBlockHeight);
            RequestDeferredSaveNoLock();
            RequestDeferredHistorySaveNoLock();

            winnersSnapshot = ClonePayouts(_state.WinnersList);
            onDeckSnapshot = ClonePayouts(_state.OnDeckList);
            networkStatus = BuildNetworkStatusNoLock();
            adopted = true;
        }

        if (adopted)
        {
            _logger.LogWarning(
                "Bootstrapped current state {StateId} from {SourceEndpoint} without local chain context. This state should be cross-checked by subsequent peer sync.",
                bundle.StateId,
                sourceEndpoint);
            await _hubContext.Clients.All.SendAsync("UpdateWinners", winnersSnapshot);
            await _hubContext.Clients.All.SendAsync("UpdateOnDeck", onDeckSnapshot);
            await _hubContext.Clients.All.SendAsync("UpdateNetworkState", networkStatus);
            await _hubContext.Clients.All.SendAsync("UpdateRoundHistory", GetRoundHistory());
            await NotifyWinnersListChangedAsync($"bootstrap-state:{sourceEndpoint}");
        }

        return adopted;
    }

    public async Task<bool> TrySyncCurrentRoundMetadataAsync(
        string stateId,
        int remoteCurrentRoundNumber,
        DateTime? remoteLastRotationUtc,
        string sourceEndpoint)
    {
        if (string.IsNullOrWhiteSpace(stateId) || !remoteLastRotationUtc.HasValue || remoteLastRotationUtc.Value == default)
        {
            return false;
        }

        BootNetworkStatusDto? networkStatus = null;
        bool changed = false;

        lock (_sync)
        {
            if (!string.Equals(_state.CurrentStateId, stateId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!_state.LastRotationUtc.HasValue ||
                _state.LastRotationUtc.Value == default ||
                remoteLastRotationUtc.Value > _state.LastRotationUtc.Value)
            {
                _state.LastRotationUtc = remoteLastRotationUtc.Value;
                changed = true;
            }

            if (remoteCurrentRoundNumber > _state.CurrentRoundNumber)
            {
                _state.CurrentRoundNumber = remoteCurrentRoundNumber;
                changed = true;
            }

            if (changed)
            {
                RequestDeferredSaveNoLock();
                networkStatus = BuildNetworkStatusNoLock();
            }
        }

        if (changed && networkStatus != null)
        {
            _logger.LogInformation(
                "Synced current-round metadata for state {StateId} from {SourceEndpoint}.",
                stateId,
                sourceEndpoint);
            await _hubContext.Clients.All.SendAsync("UpdateNetworkState", networkStatus);
        }

        return changed;
    }

    private void LoadState()
    {
        lock (_sync)
        {
            if (File.Exists(BootPortalPaths.PoolStateFilePath))
            {
                if (TryLoadStateFromPathNoLock(BootPortalPaths.PoolStateFilePath, "primary"))
                {
                    LoadHistoryStateNoLock();
                    return;
                }

                string backupPath = GetPoolStateBackupPath();
                if (File.Exists(backupPath) && TryLoadStateFromPathNoLock(backupPath, "backup"))
                {
                    _logger.LogWarning(
                        "Recovered Boot protocol state from backup after failing to read the primary state file.");
                    LoadHistoryStateNoLock();
                    SaveStateNoLock();
                    return;
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
            CurrentRoundNumber = 0,
            CurrentTipBlockHash = null,
            CurrentTipBlockHeight = null,
            LastTestingTriggerBlockHeight = null,
            LastRotationUtc = null
        };

        _state.WinnersList = BuildGenesisWinnersListNoLock();
        _state.CurrentStateId = ComputeStateIdFromPayoutsNoLock(_state.WinnersList, null);
        _state.CandidateStateId = ComputeCandidateStateIdNoLock();
    }

    private List<PayoutInfo> BuildGenesisWinnersListNoLock()
    {
        return
        [
            new PayoutInfo
            {
                Value = Program.BLOCK_REWARD / 2,
                Address = GenesisFoundationAddress,
                Username = GenesisFoundationAddress
            }
        ];
    }

    private void SaveStateNoLock([CallerMemberName] string? reason = null)
    {
        CombinedStateSaveSnapshot snapshot = CaptureFullStateSnapshotNoLock();
        string effectiveReason = reason ?? "unknown";
        WriteStateFileSnapshot(snapshot.Core, effectiveReason);
        WriteStateFileSnapshot(snapshot.History, $"{effectiveReason}-history", compact: true);
    }

    private void RequestDeferredSaveNoLock()
    {
        _deferredSavePending = true;
        if (_deferredSaveTask is { IsCompleted: false })
        {
            return;
        }

        _deferredSaveTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DeferredSaveInterval);
                StateFileSnapshot<PoolState>? snapshot = null;
                lock (_sync)
                {
                    if (!_deferredSavePending)
                    {
                        return;
                    }

                    _deferredSavePending = false;
                    snapshot = CaptureCoreStateSnapshotNoLock();
                }

                if (snapshot != null)
                {
                    WriteStateFileSnapshot(snapshot, "deferred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deferred pool-state save failed.");
            }
        });
    }

    private void RequestDeferredHistorySaveNoLock()
    {
        _deferredHistorySavePending = true;
        if (_deferredHistorySaveTask is { IsCompleted: false })
        {
            return;
        }

        _deferredHistorySaveTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DeferredHistorySaveInterval);
                StateFileSnapshot<PoolStateHistory>? snapshot = null;
                lock (_sync)
                {
                    if (!_deferredHistorySavePending)
                    {
                        return;
                    }

                    _deferredHistorySavePending = false;
                    snapshot = CaptureHistoryStateSnapshotNoLock();
                }

                if (snapshot != null)
                {
                    WriteStateFileSnapshot(snapshot, "deferred-history", compact: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deferred pool-state history save failed.");
            }
        });
    }

    private void QueueRealtimeSend(Task sendTask, string updateName)
    {
        _ = sendTask.ContinueWith(
            completed =>
            {
                if (completed.Exception != null)
                {
                    _logger.LogDebug(
                        completed.Exception,
                        "Realtime dashboard update {UpdateName} failed.",
                        updateName);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private CombinedStateSaveSnapshot CaptureFullStateSnapshotNoLock()
    {
        return new CombinedStateSaveSnapshot
        {
            Core = CaptureCoreStateSnapshotNoLock(),
            History = CaptureHistoryStateSnapshotNoLock()
        };
    }

    private StateFileSnapshot<PoolState> CaptureCoreStateSnapshotNoLock()
    {
        _state.Metadata.NetworkId = _poolConfig.BootNetworkId;
        _state.Metadata.ProtocolVersion = _poolConfig.BootProtocolVersion;

        return new StateFileSnapshot<PoolState>
        {
            Payload = BuildCoreStateSnapshotNoLock(),
            TargetPath = BootPortalPaths.PoolStateFilePath,
            BackupPath = GetPoolStateBackupPath()
        };
    }

    private StateFileSnapshot<PoolStateHistory> CaptureHistoryStateSnapshotNoLock()
    {
        return new StateFileSnapshot<PoolStateHistory>
        {
            Payload = BuildHistoryStateSnapshotNoLock(),
            TargetPath = BootPortalPaths.PoolStateHistoryFilePath,
            BackupPath = GetPoolStateHistoryBackupPath()
        };
    }

    private void WriteStateFileSnapshot<T>(StateFileSnapshot<T> snapshot, string reason, bool compact = false)
    {
        var saveStopwatch = Stopwatch.StartNew();
        BootPortalPaths.EnsureParentDirectory(snapshot.TargetPath);
        string tempPath = $"{snapshot.TargetPath}.tmp";
        JsonSerializerOptions options = compact ? _compactJsonOptions : _jsonOptions;
        string json = JsonSerializer.Serialize(snapshot.Payload, options);

        File.WriteAllText(tempPath, json);
        if (File.Exists(snapshot.TargetPath))
        {
            File.Replace(tempPath, snapshot.TargetPath, snapshot.BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, snapshot.TargetPath);
        }

        saveStopwatch.Stop();
        if (saveStopwatch.Elapsed.TotalMilliseconds >= SlowStateSaveWarningMs)
        {
            _logger.LogWarning(
                "Slow pool-state save: {DurationMs:F1} ms (reason={Reason}, bytes={Bytes}).",
                saveStopwatch.Elapsed.TotalMilliseconds,
                reason,
                json.Length);
        }
    }

    private sealed class CombinedStateSaveSnapshot
    {
        public StateFileSnapshot<PoolState> Core { get; init; } = new();
        public StateFileSnapshot<PoolStateHistory> History { get; init; } = new();
    }

    private sealed class StateFileSnapshot<T>
    {
        public T Payload { get; init; } = default!;
        public string TargetPath { get; init; } = string.Empty;
        public string BackupPath { get; init; } = string.Empty;
    }

    private PoolState BuildCoreStateSnapshotNoLock()
    {
        return new PoolState
        {
            Metadata = new BootProtocolMetadata
            {
                NetworkId = _poolConfig.BootNetworkId,
                ProtocolVersion = _poolConfig.BootProtocolVersion
            },
            CurrentStateId = _state.CurrentStateId,
            CandidateStateId = _state.CandidateStateId,
            CurrentRoundNumber = _state.CurrentRoundNumber,
            CurrentTipBlockHash = _state.CurrentTipBlockHash,
            CurrentTipBlockHeight = _state.CurrentTipBlockHeight,
            LastTestingTriggerBlockHash = _state.LastTestingTriggerBlockHash,
            LastTestingTriggerBlockHeight = _state.LastTestingTriggerBlockHeight,
            AcceptedParentBlockHashes = _state.AcceptedParentBlockHashes.ToList(),
            LastRotationUtc = _state.LastRotationUtc,
            WinnersList = ClonePayouts(_state.WinnersList),
            OnDeckList = ClonePayouts(_state.OnDeckList),
            OnDeckProofs = _state.OnDeckProofs.Select(CloneProof).ToList(),
            Peers = _state.Peers.Select(ClonePeer).ToList(),
            KnownDatumPayoutAddresses = new Dictionary<string, string>(_state.KnownDatumPayoutAddresses, StringComparer.Ordinal),
            BestShare = CloneBestShare(_state.BestShare),
            RecentAcceptedShares = [],
            RecentRejectedShareDiagnostics = [],
            RecentCoinbaserDiagnostics = [],
            RecentDatumShareResponses = [],
            RecentDatumSessions = [],
            RecentNetworkEvents = [],
            HashrateSamples = [],
            ArchivedStateBundles = []
        };
    }

    private PoolStateHistory BuildHistoryStateSnapshotNoLock()
    {
        return new PoolStateHistory
        {
            RecentAcceptedShares = _state.RecentAcceptedShares.Select(CloneAcceptedShareTelemetry).ToList(),
            RecentRejectedShareDiagnostics = _state.RecentRejectedShareDiagnostics.Select(CloneShareDiagnostic).ToList(),
            RecentCoinbaserDiagnostics = _state.RecentCoinbaserDiagnostics.Select(CloneCoinbaserDiagnostic).ToList(),
            RecentDatumShareResponses = _state.RecentDatumShareResponses.Select(CloneDatumShareResponse).ToList(),
            RecentDatumSessions = _state.RecentDatumSessions.Select(CloneDatumSession).ToList(),
            RecentNetworkEvents = _state.RecentNetworkEvents.Select(CloneNetworkEvent).ToList(),
            HashrateSamples = _state.HashrateSamples.Select(CloneHashratePoint).ToList(),
            ArchivedStateBundles = _state.ArchivedStateBundles.Select(CloneBundle).ToList()
        };
    }

    private bool TryLoadStateFromPathNoLock(string path, string label)
    {
        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<PoolState>(json);
            if (loaded == null)
            {
                return false;
            }

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
            _state.LastTestingTriggerBlockHash = NormalizeCanonicalBlockHash(_state.LastTestingTriggerBlockHash);
            _state.KnownDatumPayoutAddresses ??= [];
            _state.RecentAcceptedShares ??= [];
            _state.RecentRejectedShareDiagnostics ??= [];
            _state.RecentCoinbaserDiagnostics ??= [];
            _state.RecentDatumShareResponses ??= [];
            _state.RecentDatumSessions ??= [];
            _state.RecentNetworkEvents ??= [];
            _state.HashrateSamples ??= [];
            NormalizeArchivedBundlesNoLock();
            EnsureRoundMetadataNoLock();
            UpdateKnownBlockHeightNoLock(_state.CurrentTipBlockHash, _state.CurrentTipBlockHeight);
            UpdateKnownBlockHeightNoLock(_state.LastTestingTriggerBlockHash, _state.LastTestingTriggerBlockHeight);
            _state.AcceptedParentBlockHashes = GetAcceptedParentBlockHashesNoLock();
            TrimAcceptedShareTelemetryNoLock(DateTime.UtcNow);
            TrimShareDiagnosticsNoLock(DateTime.UtcNow);
            TrimCoinbaserDiagnosticsNoLock(DateTime.UtcNow);
            TrimDatumShareResponsesNoLock(DateTime.UtcNow);
            FinalizeStaleDatumSessionsNoLock(DateTime.UtcNow, "service-restart", "Recovered open DATUM session from prior process state.");
            RebuildActiveDatumSessionIndexNoLock();
            TrimDatumSessionsNoLock(DateTime.UtcNow);
            TrimNetworkEventsNoLock(DateTime.UtcNow);
            TrimHashrateSamplesNoLock(DateTime.UtcNow);
            _recentShareDiagnostics.Clear();
            _recentShareDiagnostics.AddRange(_state.RecentRejectedShareDiagnostics.Select(CloneShareDiagnostic));

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
            CacheCurrentCandidateBundleNoLock();
            SeedSeenSharesNoLock();
            _logger.LogInformation("Loaded Boot protocol state from {Label} disk file.", label);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Boot protocol state from {Label} disk file.", label);
            return false;
        }
    }

    private void LoadHistoryStateNoLock()
    {
        if (File.Exists(BootPortalPaths.PoolStateHistoryFilePath))
        {
            if (TryLoadHistoryFromPathNoLock(BootPortalPaths.PoolStateHistoryFilePath, "history-primary"))
            {
                return;
            }

            string backupPath = GetPoolStateHistoryBackupPath();
            if (File.Exists(backupPath) && TryLoadHistoryFromPathNoLock(backupPath, "history-backup"))
            {
                _logger.LogWarning(
                    "Recovered Boot protocol history from backup after failing to read the primary history file.");
                SaveStateNoLock();
            }
        }
    }

    private bool TryLoadHistoryFromPathNoLock(string path, string label)
    {
        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<PoolStateHistory>(json);
            if (loaded == null)
            {
                return false;
            }

            _state.RecentAcceptedShares = loaded.RecentAcceptedShares ?? [];
            _state.RecentRejectedShareDiagnostics = loaded.RecentRejectedShareDiagnostics ?? [];
            _state.RecentCoinbaserDiagnostics = loaded.RecentCoinbaserDiagnostics ?? [];
            _state.RecentDatumShareResponses = loaded.RecentDatumShareResponses ?? [];
            _state.RecentDatumSessions = loaded.RecentDatumSessions ?? [];
            _state.RecentNetworkEvents = loaded.RecentNetworkEvents ?? [];
            _state.HashrateSamples = loaded.HashrateSamples ?? [];
            _state.ArchivedStateBundles = loaded.ArchivedStateBundles ?? [];

            NormalizeArchivedBundlesNoLock();
            EnsureRoundMetadataNoLock();
            TrimAcceptedShareTelemetryNoLock(DateTime.UtcNow);
            TrimShareDiagnosticsNoLock(DateTime.UtcNow);
            TrimCoinbaserDiagnosticsNoLock(DateTime.UtcNow);
            TrimDatumShareResponsesNoLock(DateTime.UtcNow);
            FinalizeStaleDatumSessionsNoLock(DateTime.UtcNow, "service-restart", "Recovered open DATUM session from prior process history.");
            RebuildActiveDatumSessionIndexNoLock();
            TrimDatumSessionsNoLock(DateTime.UtcNow);
            TrimNetworkEventsNoLock(DateTime.UtcNow);
            TrimHashrateSamplesNoLock(DateTime.UtcNow);
            _recentShareDiagnostics.Clear();
            _recentShareDiagnostics.AddRange(_state.RecentRejectedShareDiagnostics.Select(CloneShareDiagnostic));
            _logger.LogInformation("Loaded Boot protocol history from {Label} disk file.", label);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Boot protocol history from {Label} disk file.", label);
            return false;
        }
    }

    private string GetPoolStateBackupPath()
    {
        return $"{BootPortalPaths.PoolStateFilePath}.bak";
    }

    private string GetPoolStateHistoryBackupPath()
    {
        return $"{BootPortalPaths.PoolStateHistoryFilePath}.bak";
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
        DateTime nowUtc = DateTime.UtcNow;
        long? currentRoundElapsedSeconds = GetElapsedSeconds(_state.LastRotationUtc, nowUtc);
        List<double> onDeckDifficulties = _state.OnDeckProofs
            .Select(x => x.Difficulty)
            .Where(x => x > 0)
            .ToList();
        double currentStateTotalDifficulty = _state.WinnersList.Sum(x => x.Difficulty);
        double onDeckTotalDifficulty = onDeckDifficulties.Sum();
        double? currentRoundObservedHashrateThs = EstimateRankAdjustedHashrateThs(onDeckDifficulties, currentRoundElapsedSeconds);
        double? localDatumHashrateThs = EstimateLocalDatumHashrateThsNoLock(nowUtc);
        BootDatumDiagnosticsDto localDatumDiagnostics = BuildLocalDatumDiagnosticsNoLock(nowUtc);
        List<BootLocalDatumMinerSummaryDto> localDatumMiners = BuildLocalDatumMinerSummariesNoLock(nowUtc);
        BootCoinbaserDiagnosticsSummaryDto coinbaserDiagnostics = BuildCoinbaserDiagnosticsSummaryNoLock(nowUtc);

        return new BootNetworkStatusDto
        {
            SelfEndpoint = NormalizePeerEndpoint(_poolConfig.PublicBaseUrl),
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            NetworkId = _poolConfig.BootNetworkId,
            CurrentRoundNumber = _state.CurrentRoundNumber,
            SharedWinnerSlotCount = _poolConfig.SharedWinnerSlotCount,
            TotalPayoutSlotCount = _poolConfig.TotalPayoutSlotCount,
            CurrentStateId = _state.CurrentStateId,
            CandidateStateId = _state.CandidateStateId,
            CurrentTipBlockHash = _state.CurrentTipBlockHash,
            CurrentTipBlockHeight = _state.CurrentTipBlockHeight,
            LastRotationUtc = _state.LastRotationUtc,
            WinnersCount = _state.WinnersList.Count,
            CurrentStateTotalDifficulty = currentStateTotalDifficulty,
            OnDeckCount = _state.OnDeckList.Count,
            OnDeckTotalDifficulty = onDeckTotalDifficulty,
            CurrentRoundElapsedSeconds = currentRoundElapsedSeconds,
            CurrentRoundObservedHashrateThs = currentRoundObservedHashrateThs,
            CurrentRoundObservedHashrateDisplay = FormatObservedHashrate(currentRoundObservedHashrateThs),
            LocalDatumHashrateThs = localDatumHashrateThs,
            LocalDatumHashrateDisplay = FormatObservedHashrate(localDatumHashrateThs),
            PeerCount = _state.Peers.Count,
            AdminApiEnabled = _poolConfig.EnableAdminApi,
            TestingRoundResetEnabled = _poolConfig.TestingRoundResetEnabled,
            TestingRoundResetMode = _poolConfig.TestingRoundResetMode,
            TestingRoundResetLowNibbleThreshold = _poolConfig.TestingRoundResetLowNibbleThreshold,
            TestingRoundResetDescription = BuildTestingRoundResetDescriptionNoLock(),
            LastTestingTriggerBlockHash = _state.LastTestingTriggerBlockHash,
            LastTestingTriggerBlockHeight = _state.LastTestingTriggerBlockHeight,
            LocalDatumDiagnostics = localDatumDiagnostics,
            LocalDatumMiners = localDatumMiners,
            CoinbaserDiagnostics = coinbaserDiagnostics,
            Peers = _state.Peers.Select(ClonePeer).ToList(),
            Commitment = BuildCommitmentNoLock()
        };
    }

    private BootHashrateSeriesDto BuildHashrateSeriesNoLock(string? windowKey)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateTime cutoffUtc = ResolveHashrateSeriesCutoffUtc(windowKey, nowUtc);
        TrimHashrateSamplesNoLock(nowUtc);

        return new BootHashrateSeriesDto
        {
            SampleIntervalSeconds = GetHashrateSampleIntervalSeconds(),
            LocalWindowSeconds = GetHashrateLocalWindowSeconds(),
            Points = _state.HashrateSamples
                .Where(point => point.TimestampUtc >= cutoffUtc)
                .OrderBy(point => point.TimestampUtc)
                .Select(CloneHashratePoint)
                .ToList()
        };
    }

    private BootShareDiagnosticsSeriesDto BuildShareDiagnosticsSeriesNoLock(
        string? windowKey,
        string? source,
        bool? accepted,
        int limit,
        string? minerAddress,
        string? category)
    {
        DateTime nowUtc = DateTime.UtcNow;
        TrimShareDiagnosticsNoLock(nowUtc);
        DateTime cutoffUtc = ResolveTelemetryCutoffUtc(windowKey, nowUtc, GetShareDiagnosticRetentionHours());
        string? normalizedMinerAddress = string.IsNullOrWhiteSpace(minerAddress)
            ? null
            : BitcoinScript.NormalizeAddress(minerAddress);
        string? normalizedCategory = NormalizeDiagnosticReason(category);

        IEnumerable<BootShareDiagnosticTelemetry> query = _recentShareDiagnostics
            .Where(item => item.TimestampUtc >= cutoffUtc);

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(item => string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase));
        }

        if (accepted.HasValue)
        {
            query = query.Where(item => item.Accepted == accepted.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedMinerAddress))
        {
            query = query.Where(item => string.Equals(
                BitcoinScript.NormalizeAddress(item.MinerAddress),
                normalizedMinerAddress,
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            query = query.Where(item => string.Equals(
                item.RejectionCategory,
                normalizedCategory,
                StringComparison.OrdinalIgnoreCase));
        }

        List<BootShareDiagnosticTelemetry> events = query
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(Math.Clamp(limit, 1, 5000))
            .Select(CloneShareDiagnostic)
            .ToList();

        return new BootShareDiagnosticsSeriesDto
        {
            WindowSeconds = (int)Math.Max(0, (nowUtc - cutoffUtc).TotalSeconds),
            TotalEvents = events.Count,
            Events = events
        };
    }

    private BootCoinbaserDiagnosticsSeriesDto BuildCoinbaserDiagnosticsSeriesNoLock(
        string? windowKey,
        int limit,
        string? remoteEndpoint,
        bool? temporarySlotZero)
    {
        DateTime nowUtc = DateTime.UtcNow;
        TrimCoinbaserDiagnosticsNoLock(nowUtc);
        DateTime cutoffUtc = ResolveTelemetryCutoffUtc(windowKey, nowUtc, GetShareDiagnosticRetentionHours());

        IEnumerable<BootCoinbaserFetchTelemetry> query = _state.RecentCoinbaserDiagnostics
            .Where(item => item.TimestampUtc >= cutoffUtc);

        if (!string.IsNullOrWhiteSpace(remoteEndpoint))
        {
            query = query.Where(item => string.Equals(item.RemoteEndpoint, remoteEndpoint, StringComparison.OrdinalIgnoreCase));
        }

        if (temporarySlotZero.HasValue)
        {
            query = query.Where(item => item.UsingTemporarySlotZero == temporarySlotZero.Value);
        }

        List<BootCoinbaserFetchTelemetry> events = query
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(Math.Clamp(limit, 1, 5000))
            .Select(CloneCoinbaserDiagnostic)
            .ToList();

        return new BootCoinbaserDiagnosticsSeriesDto
        {
            WindowSeconds = (int)Math.Max(0, (nowUtc - cutoffUtc).TotalSeconds),
            TotalEvents = events.Count,
            Events = events
        };
    }

    private BootDatumShareResponseSeriesDto BuildDatumShareResponseSeriesNoLock(
        string? windowKey,
        int limit,
        string? remoteEndpoint,
        bool? accepted,
        string? reason)
    {
        DateTime nowUtc = DateTime.UtcNow;
        TrimDatumShareResponsesNoLock(nowUtc);
        DateTime cutoffUtc = ResolveTelemetryCutoffUtc(windowKey, nowUtc, GetShareDiagnosticRetentionHours());

        IEnumerable<BootDatumShareResponseTelemetry> query = _state.RecentDatumShareResponses
            .Where(item => item.TimestampUtc >= cutoffUtc);

        if (!string.IsNullOrWhiteSpace(remoteEndpoint))
        {
            query = query.Where(item => string.Equals(item.RemoteEndpoint, remoteEndpoint, StringComparison.OrdinalIgnoreCase));
        }

        if (accepted.HasValue)
        {
            query = query.Where(item => item.Accepted == accepted.Value);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            string? normalizedReason = NormalizeDiagnosticReason(reason);
            if (!string.IsNullOrWhiteSpace(normalizedReason))
            {
                query = query.Where(item => string.Equals(
                    NormalizeDiagnosticReason(item.RejectionReason),
                    normalizedReason,
                    StringComparison.OrdinalIgnoreCase));
            }
        }

        List<BootDatumShareResponseTelemetry> events = query
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(Math.Clamp(limit, 1, 5000))
            .Select(CloneDatumShareResponse)
            .ToList();

        return new BootDatumShareResponseSeriesDto
        {
            WindowSeconds = (int)Math.Max(0, (nowUtc - cutoffUtc).TotalSeconds),
            TotalEvents = events.Count,
            Events = events
        };
    }

    private BootDatumSessionSeriesDto BuildDatumSessionSeriesNoLock(
        string? windowKey,
        int limit,
        string? remoteEndpoint,
        bool? active,
        string? protocol)
    {
        DateTime nowUtc = DateTime.UtcNow;
        TrimDatumSessionsNoLock(nowUtc);
        DateTime cutoffUtc = ResolveTelemetryCutoffUtc(windowKey, nowUtc, GetShareDiagnosticRetentionHours());

        IEnumerable<BootDatumSessionTelemetry> query = _state.RecentDatumSessions
            .Where(item => item.StartedUtc >= cutoffUtc || item.ClosedUtc == null || item.ClosedUtc >= cutoffUtc);

        if (!string.IsNullOrWhiteSpace(remoteEndpoint))
        {
            query = query.Where(item => string.Equals(item.RemoteEndpoint, remoteEndpoint, StringComparison.OrdinalIgnoreCase));
        }

        if (active.HasValue)
        {
            query = query.Where(item => (item.ClosedUtc == null) == active.Value);
        }

        if (!string.IsNullOrWhiteSpace(protocol))
        {
            query = query.Where(item => string.Equals(item.Protocol, protocol, StringComparison.OrdinalIgnoreCase));
        }

        List<BootDatumSessionTelemetry> events = query
            .OrderBy(item => item.StartedUtc)
            .TakeLast(Math.Clamp(limit, 1, 5000))
            .Select(CloneDatumSession)
            .ToList();

        return new BootDatumSessionSeriesDto
        {
            WindowSeconds = (int)Math.Max(0, (nowUtc - cutoffUtc).TotalSeconds),
            TotalEvents = events.Count,
            Events = events
        };
    }

    private BootNetworkEventSeriesDto BuildNetworkEventSeriesNoLock(
        string? windowKey,
        int limit,
        string? eventType,
        string? source)
    {
        DateTime nowUtc = DateTime.UtcNow;
        TrimNetworkEventsNoLock(nowUtc);
        DateTime cutoffUtc = ResolveTelemetryCutoffUtc(windowKey, nowUtc, GetShareDiagnosticRetentionHours());

        IEnumerable<BootNetworkEvent> query = _state.RecentNetworkEvents
            .Where(item => item.TimestampUtc >= cutoffUtc);

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(item => string.Equals(item.EventType, eventType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(item => string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase));
        }

        List<BootNetworkEvent> events = query
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(Math.Clamp(limit, 1, 5000))
            .Select(CloneNetworkEvent)
            .ToList();

        return new BootNetworkEventSeriesDto
        {
            WindowSeconds = (int)Math.Max(0, (nowUtc - cutoffUtc).TotalSeconds),
            TotalEvents = events.Count,
            Events = events
        };
    }

    private bool TryGetDatumSessionNoLock(string sessionId, out BootDatumSessionTelemetry session)
    {
        if (_activeDatumSessions.TryGetValue(sessionId, out session!))
        {
            return true;
        }

        BootDatumSessionTelemetry? existing = _state.RecentDatumSessions.LastOrDefault(item =>
            string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));
        if (existing == null)
        {
            session = null!;
            return false;
        }

        session = existing;
        if (session.ClosedUtc == null)
        {
            _activeDatumSessions[sessionId] = session;
        }

        return true;
    }

    private BootDatumSessionTelemetry FindOrCreateDatumSessionNoLock(
        string sessionId,
        string remoteEndpoint,
        DateTime startedUtc)
    {
        if (TryGetDatumSessionNoLock(sessionId, out var existing) && existing.ClosedUtc == null)
        {
            return existing;
        }

        var session = new BootDatumSessionTelemetry
        {
            SessionId = sessionId,
            RemoteEndpoint = remoteEndpoint,
            StartedUtc = startedUtc,
            LastActivityUtc = startedUtc,
            LastActivityType = "opened"
        };
        _state.RecentDatumSessions.Add(session);
        _activeDatumSessions[sessionId] = session;
        return session;
    }

    private void RebuildActiveDatumSessionIndexNoLock()
    {
        _activeDatumSessions.Clear();
        foreach (BootDatumSessionTelemetry session in _state.RecentDatumSessions.Where(item => item.ClosedUtc == null))
        {
            _activeDatumSessions[session.SessionId] = session;
        }
    }

    private void FinalizeStaleDatumSessionsNoLock(DateTime closedUtc, string closeDisposition, string closeReason)
    {
        foreach (BootDatumSessionTelemetry session in _state.RecentDatumSessions.Where(item => item.ClosedUtc == null))
        {
            session.ServerInitiatedClose = true;
            session.ServerCloseEventType = "service-restart";
            session.CloseDisposition = closeDisposition;
            session.CloseReason = closeReason;
            session.ClosedUtc = closedUtc;
            session.DurationMs = Math.Max(0, (closedUtc - session.StartedUtc).TotalMilliseconds);
            session.IdleBeforeCloseMs = session.LastActivityUtc.HasValue
                ? Math.Max(0, (closedUtc - session.LastActivityUtc.Value).TotalMilliseconds)
                : null;
        }

        _activeDatumSessions.Clear();
    }

    private BootCoinbaserDiagnosticsSummaryDto BuildCoinbaserDiagnosticsSummaryNoLock(DateTime nowUtc)
    {
        TrimCoinbaserDiagnosticsNoLock(nowUtc);
        DateTime cutoffUtc = nowUtc.AddMinutes(-30);
        List<BootCoinbaserFetchTelemetry> recent = _state.RecentCoinbaserDiagnostics
            .Where(item => item.TimestampUtc >= cutoffUtc)
            .OrderBy(item => item.TimestampUtc)
            .ToList();

        if (recent.Count == 0)
        {
            return new BootCoinbaserDiagnosticsSummaryDto
            {
                WindowSeconds = (int)Math.Max(0, (nowUtc - cutoffUtc).TotalSeconds)
            };
        }

        List<double> durations = recent
            .Select(item => item.DurationMs)
            .OrderBy(x => x)
            .ToList();
        int p95Index = Math.Clamp((int)Math.Ceiling(durations.Count * 0.95) - 1, 0, durations.Count - 1);
        List<double> sendDurations = recent
            .Select(item => item.SendDurationMs)
            .OrderBy(x => x)
            .ToList();
        int sendP95Index = Math.Clamp((int)Math.Ceiling(sendDurations.Count * 0.95) - 1, 0, sendDurations.Count - 1);

        return new BootCoinbaserDiagnosticsSummaryDto
        {
            WindowSeconds = (int)Math.Max(0, (nowUtc - cutoffUtc).TotalSeconds),
            TotalFetches = recent.Count,
            LastFetchUtc = recent[^1].TimestampUtc,
            AverageDurationMs = durations.Average(),
            P95DurationMs = durations[p95Index],
            AverageParseDurationMs = recent.Average(item => item.ParseDurationMs),
            AverageStateReadDurationMs = recent.Average(item => item.StateReadDurationMs),
            AverageBuildDurationMs = recent.Average(item => item.BuildDurationMs),
            AverageSerializeDurationMs = recent.Average(item => item.SerializeDurationMs),
            AverageSendDurationMs = recent.Average(item => item.SendDurationMs),
            P95SendDurationMs = sendDurations[sendP95Index],
            TemporarySlotZeroCount = recent.Count(item => item.UsingTemporarySlotZero),
            SlowFetchCount = recent.Count(item => item.DurationMs >= 1000),
            SlowStateReadCount = recent.Count(item => item.StateReadDurationMs >= 250),
            SlowBuildCount = recent.Count(item => item.BuildDurationMs >= 250),
            SlowSerializeCount = recent.Count(item => item.SerializeDurationMs >= 250),
            SlowSendCount = recent.Count(item => item.SendDurationMs >= 250)
        };
    }

    private List<BootRoundHistoryEntry> BuildRoundHistoryNoLock(int limit)
    {
        int effectiveLimit = Math.Clamp(limit, 1, Math.Max(1, _poolConfig.MaxStateBundleHistory));
        List<BootStateBundle> completedRounds = _state.ArchivedStateBundles
            .Where(bundle => bundle.WinnersList.Count > 0 || bundle.ProofWinnersList.Count > 0)
            .ToList();
        HashSet<string> canonicalStateIds = BuildCanonicalStateIdSetNoLock();

        return completedRounds
            .Take(effectiveLimit)
            .Select((bundle, index) => BuildRoundHistoryEntryNoLock(
                bundle,
                canonicalStateIds.Contains(bundle.StateId),
                index + 1 < completedRounds.Count ? completedRounds[index + 1] : null))
            .ToList();
    }

    private BootRoundHistoryEntry BuildRoundHistoryEntryNoLock(BootStateBundle bundle, bool isCanonical, BootStateBundle? priorBundle)
    {
        List<BootRoundPayoutAggregate> paidRecipients = AggregateRoundPayoutsNoLock(
            bundle.ProofWinnersList.Count > 0 ? bundle.ProofWinnersList : bundle.WinnersList);
        List<BootRoundPayoutAggregate> nextRecipients = AggregateRoundPayoutsNoLock(bundle.WinnersList);
        long? roundElapsedSeconds = GetElapsedSeconds(priorBundle?.CreatedAtUtc, bundle.CreatedAtUtc);
        List<double> roundDifficulties = bundle.ShareProofs
            .Select(x => x.Difficulty)
            .Where(x => x > 0)
            .ToList();
        double? observedHashrateThs = EstimateRankAdjustedHashrateThs(roundDifficulties, roundElapsedSeconds);
        int roundNumber = Math.Max(0, bundle.CurrentRoundNumber - 1);

        return new BootRoundHistoryEntry
        {
            RoundNumber = roundNumber,
            StateId = bundle.StateId,
            PreviousStateId = bundle.PreviousStateId,
            Kind = bundle.Kind,
            IsCanonical = isCanonical,
            IsOrphaned = !isCanonical,
            TriggerBlockHash = bundle.LockedByBlockHash,
            TriggerBlockHeight = bundle.LockedByBlockHeight,
            ParentBlockHash = bundle.ParentBlockHash,
            ParentBlockHeight = bundle.ParentBlockHeight,
            LockedAtUtc = bundle.CreatedAtUtc,
            RoundElapsedSeconds = roundElapsedSeconds,
            WinningShareCount = bundle.ShareProofs.Count,
            WinningTotalDifficulty = bundle.TotalDifficulty,
            WinningTotalDifficultyDisplay = ClientHandler.FormatDifficulty(bundle.TotalDifficulty),
            ObservedHashrateThs = observedHashrateThs,
            ObservedHashrateDisplay = FormatObservedHashrate(observedHashrateThs),
            PaidSlotCount = bundle.ProofWinnersList.Count > 0 ? bundle.ProofWinnersList.Count : bundle.WinnersList.Count,
            PaidRecipientCount = paidRecipients.Count,
            PaidTotalValue = paidRecipients.Aggregate<BootRoundPayoutAggregate, ulong>(0, (sum, item) => sum + item.TotalValue),
            NextWinnerSlotCount = bundle.WinnersList.Count,
            NextWinnerRecipientCount = nextRecipients.Count,
            NextWinnerTotalValue = nextRecipients.Aggregate<BootRoundPayoutAggregate, ulong>(0, (sum, item) => sum + item.TotalValue),
            PaidRecipients = paidRecipients,
            NextRecipients = nextRecipients
        };
    }

    private HashSet<string> BuildCanonicalStateIdSetNoLock()
    {
        var canonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? cursor = _state.CurrentStateId;

        while (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!canonical.Add(cursor))
            {
                break;
            }

            BootStateBundle? bundle = _state.ArchivedStateBundles.FirstOrDefault(existing =>
                string.Equals(existing.StateId, cursor, StringComparison.OrdinalIgnoreCase));
            if (bundle == null)
            {
                break;
            }

            cursor = bundle.PreviousStateId;
        }

        return canonical;
    }

    private static List<BootRoundPayoutAggregate> AggregateRoundPayoutsNoLock(IEnumerable<PayoutInfo> payouts)
    {
        return payouts
            .GroupBy(payout => BitcoinScript.NormalizeAddress(payout.Address), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                string username = group
                    .Select(item => string.IsNullOrWhiteSpace(item.Username) ? item.Address : item.Username)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key;
                double totalDifficulty = group.Sum(item => item.Difficulty);

                return new BootRoundPayoutAggregate
                {
                    Address = group.Key,
                    Username = username,
                    SlotCount = group.Count(),
                    TotalValue = group.Aggregate<PayoutInfo, ulong>(0, (sum, item) => sum + item.Value),
                    TotalDifficulty = totalDifficulty,
                    TotalDifficultyDisplay = ClientHandler.FormatDifficulty(totalDifficulty)
                };
            })
            .OrderByDescending(item => item.TotalValue)
            .ThenByDescending(item => item.SlotCount)
            .ThenBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long? GetElapsedSeconds(DateTime? startedAtUtc, DateTime? endedAtUtc)
    {
        if (!startedAtUtc.HasValue || !endedAtUtc.HasValue)
        {
            return null;
        }

        if (startedAtUtc.Value == default || endedAtUtc.Value == default)
        {
            return null;
        }

        double totalSeconds = (endedAtUtc.Value - startedAtUtc.Value).TotalSeconds;
        if (totalSeconds < 0)
        {
            return null;
        }

        return (long)Math.Floor(totalSeconds);
    }

    private BootDatumDiagnosticsDto BuildLocalDatumDiagnosticsNoLock(DateTime nowUtc)
    {
        TrimShareDiagnosticsNoLock(nowUtc);

        DateTime cutoffUtc = nowUtc.AddHours(-GetShareDiagnosticRetentionHours());
        if (_serviceStartedUtc > cutoffUtc)
        {
            cutoffUtc = _serviceStartedUtc;
        }

        List<BootShareDiagnosticTelemetry> localDatumDiagnostics = _recentShareDiagnostics
            .Where(item => string.Equals(item.Source, "datum", StringComparison.OrdinalIgnoreCase) &&
                           item.TimestampUtc >= cutoffUtc)
            .OrderBy(item => item.TimestampUtc)
            .ToList();

        return new BootDatumDiagnosticsDto
        {
            WindowSeconds = GetShareDiagnosticRetentionHours() * 3600,
            TotalSubmissions = localDatumDiagnostics.Count,
            AcceptedCount = localDatumDiagnostics.Count(item => item.Accepted),
            AcceptedOnDeckCount = localDatumDiagnostics.Count(item => item.Accepted && item.AffectedOnDeck),
            RejectedCount = localDatumDiagnostics.Count(item => !item.Accepted),
            LastAcceptedUtc = localDatumDiagnostics.LastOrDefault(item => item.Accepted)?.TimestampUtc,
            LastRejectedUtc = localDatumDiagnostics.LastOrDefault(item => !item.Accepted)?.TimestampUtc,
            RejectionReasons = localDatumDiagnostics
                .Where(item => !item.Accepted && !string.IsNullOrWhiteSpace(item.RejectionCategory))
                .GroupBy(item => item.RejectionCategory!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new BootReasonCountDto
                {
                    Reason = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Reason, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList()
        };
    }

    private void RecordShareDiagnosticNoLock(
        string source,
        string minerAddress,
        string username,
        bool accepted,
        bool affectedOnDeck,
        string? rejectionReason,
        double difficulty,
        DateTime timestampUtc)
    {
        BootShareDiagnosticTelemetry diagnostic = CreateShareDiagnosticNoLock(
            source,
            minerAddress,
            username,
            accepted,
            affectedOnDeck,
            rejectionReason,
            difficulty,
            timestampUtc);
        _recentShareDiagnostics.Add(diagnostic);
        if (!accepted)
        {
            _state.RecentRejectedShareDiagnostics.Add(CloneShareDiagnostic(diagnostic));
        }

        TrimShareDiagnosticsNoLock(timestampUtc);
    }

    private BootShareDiagnosticTelemetry CreateShareDiagnosticNoLock(
        string source,
        string minerAddress,
        string username,
        bool accepted,
        bool affectedOnDeck,
        string? rejectionReason,
        double difficulty,
        DateTime timestampUtc)
    {
        return new BootShareDiagnosticTelemetry
        {
            Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source,
            MinerAddress = minerAddress,
            Username = username,
            Accepted = accepted,
            AffectedOnDeck = affectedOnDeck,
            RejectionReason = rejectionReason,
            RejectionCategory = NormalizeDiagnosticReason(rejectionReason),
            Difficulty = difficulty,
            CurrentRoundNumber = _state.CurrentRoundNumber,
            CurrentStateId = _state.CurrentStateId,
            CandidateStateId = _state.CandidateStateId,
            CurrentTipBlockHash = _state.CurrentTipBlockHash,
            CurrentTipBlockHeight = _state.CurrentTipBlockHeight,
            TimestampUtc = timestampUtc
        };
    }

    private void RecordNetworkEventNoLock(
        string eventType,
        string source,
        string? message,
        string? blockHash,
        long? blockHeight,
        DateTime? timestampUtc = null)
    {
        _state.RecentNetworkEvents.Add(new BootNetworkEvent
        {
            EventType = string.IsNullOrWhiteSpace(eventType) ? "unknown" : eventType,
            Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source,
            Message = string.IsNullOrWhiteSpace(message) ? null : message,
            BlockHash = NormalizeCanonicalBlockHash(blockHash),
            BlockHeight = blockHeight,
            CurrentRoundNumber = _state.CurrentRoundNumber,
            CurrentStateId = _state.CurrentStateId,
            CandidateStateId = _state.CandidateStateId,
            CurrentTipBlockHash = _state.CurrentTipBlockHash,
            CurrentTipBlockHeight = _state.CurrentTipBlockHeight,
            TimestampUtc = timestampUtc ?? DateTime.UtcNow
        });

        TrimNetworkEventsNoLock(timestampUtc ?? DateTime.UtcNow);
    }

    private void RecordAcceptedShareTelemetryNoLock(BootShareProof proof)
    {
        _state.RecentAcceptedShares.Add(new BootAcceptedShareTelemetry
        {
            MinerAddress = proof.MinerAddress,
            Username = proof.Username,
            Source = proof.Source,
            Difficulty = proof.Difficulty,
            TimestampUtc = proof.Timestamp
        });

        TrimAcceptedShareTelemetryNoLock(proof.Timestamp);
    }

    private bool MaybeCaptureHashrateSampleNoLock(DateTime nowUtc, bool force)
    {
        TrimAcceptedShareTelemetryNoLock(nowUtc);
        TrimHashrateSamplesNoLock(nowUtc);

        int intervalSeconds = GetHashrateSampleIntervalSeconds();
        BootHashratePoint? lastSample = _state.HashrateSamples.Count > 0 ? _state.HashrateSamples[^1] : null;
        if (!force && lastSample != null && (nowUtc - lastSample.TimestampUtc).TotalSeconds < intervalSeconds)
        {
            return false;
        }

        long? currentRoundElapsedSeconds = GetElapsedSeconds(_state.LastRotationUtc, nowUtc);
        List<double> onDeckDifficulties = _state.OnDeckProofs
            .Select(x => x.Difficulty)
            .Where(x => x > 0)
            .ToList();
        double? teamEstimatedHashrateThs = EstimateRankAdjustedHashrateThs(onDeckDifficulties, currentRoundElapsedSeconds);
        double? localDatumHashrateThs = EstimateLocalDatumHashrateThsNoLock(nowUtc);

        _state.HashrateSamples.Add(new BootHashratePoint
        {
            TimestampUtc = nowUtc,
            CurrentRoundNumber = _state.CurrentRoundNumber,
            TeamEstimatedHashrateThs = teamEstimatedHashrateThs,
            TeamEstimatedHashrateDisplay = FormatObservedHashrate(teamEstimatedHashrateThs),
            LocalDatumHashrateThs = localDatumHashrateThs,
            LocalDatumHashrateDisplay = FormatObservedHashrate(localDatumHashrateThs)
        });

        TrimHashrateSamplesNoLock(nowUtc);
        return true;
    }

    private double? EstimateLocalDatumHashrateThsNoLock(DateTime nowUtc)
    {
        int localWindowSeconds = GetHashrateLocalWindowSeconds();
        DateTime windowStartUtc = nowUtc.AddSeconds(-localWindowSeconds);
        List<BootAcceptedShareTelemetry> localDatumShares = _state.RecentAcceptedShares
            .Where(share => string.Equals(share.Source, "datum", StringComparison.OrdinalIgnoreCase) &&
                            share.TimestampUtc >= windowStartUtc &&
                            share.Difficulty > 0)
            .OrderBy(share => share.TimestampUtc)
            .ToList();
        if (localDatumShares.Count == 0)
        {
            return null;
        }

        DateTime effectiveStartUtc = localDatumShares[0].TimestampUtc > windowStartUtc
            ? localDatumShares[0].TimestampUtc
            : windowStartUtc;
        long? elapsedSeconds = GetElapsedSeconds(effectiveStartUtc, nowUtc);
        return EstimateRankAdjustedHashrateThs(localDatumShares.Select(share => share.Difficulty), elapsedSeconds);
    }

    private List<BootLocalDatumMinerSummaryDto> BuildLocalDatumMinerSummariesNoLock(DateTime nowUtc)
    {
        TrimAcceptedShareTelemetryNoLock(nowUtc);

        int localWindowSeconds = GetHashrateLocalWindowSeconds();
        DateTime windowStartUtc = nowUtc.AddSeconds(-localWindowSeconds);
        DateTime? roundStartUtc = _state.LastRotationUtc;

        return _state.RecentAcceptedShares
            .Where(share =>
                string.Equals(share.Source, "datum", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(share.MinerAddress))
            .GroupBy(share => BitcoinScript.NormalizeAddress(share.MinerAddress), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                List<BootAcceptedShareTelemetry> shares = group
                    .OrderBy(share => share.TimestampUtc)
                    .ToList();
                List<BootAcceptedShareTelemetry> rateShares = shares
                    .Where(share => share.TimestampUtc >= windowStartUtc && share.Difficulty > 0)
                    .ToList();
                DateTime? firstRateShareUtc = rateShares.Count > 0 ? rateShares[0].TimestampUtc : null;
                DateTime effectiveRateStartUtc = firstRateShareUtc.HasValue && firstRateShareUtc.Value > windowStartUtc
                    ? firstRateShareUtc.Value
                    : windowStartUtc;
                long? rateElapsedSeconds = rateShares.Count > 0
                    ? GetElapsedSeconds(effectiveRateStartUtc, nowUtc)
                    : null;
                double? hashrateThs = rateShares.Count > 0
                    ? EstimateRankAdjustedHashrateThs(rateShares.Select(share => share.Difficulty), rateElapsedSeconds)
                    : null;

                IEnumerable<BootAcceptedShareTelemetry> currentRoundShares = roundStartUtc.HasValue
                    ? shares.Where(share => share.TimestampUtc >= roundStartUtc.Value)
                    : shares;
                double currentRoundBestDifficulty = currentRoundShares
                    .Select(share => share.Difficulty)
                    .DefaultIfEmpty(0)
                    .Max();
                string username = shares
                    .Select(share => string.IsNullOrWhiteSpace(share.Username) ? string.Empty : share.Username)
                    .LastOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key;

                return new BootLocalDatumMinerSummaryDto
                {
                    Address = group.Key,
                    Username = username,
                    RecentAcceptedShareCount = shares.Count,
                    CurrentRoundAcceptedShareCount = currentRoundShares.Count(),
                    CurrentHashrateThs = hashrateThs,
                    CurrentHashrateDisplay = FormatObservedHashrate(hashrateThs),
                    CurrentRoundBestDifficulty = currentRoundBestDifficulty,
                    CurrentRoundBestDifficultyDisplay = ClientHandler.FormatDifficulty(currentRoundBestDifficulty),
                    LastShareUtc = shares[^1].TimestampUtc
                };
            })
            .OrderByDescending(item => item.CurrentHashrateThs ?? 0)
            .ThenByDescending(item => item.CurrentRoundBestDifficulty)
            .ThenBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void TrimShareDiagnosticsNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc.AddHours(-GetShareDiagnosticRetentionHours());
        _recentShareDiagnostics.RemoveAll(item => item.TimestampUtc < cutoffUtc);
        while (_recentShareDiagnostics.Count > MaxSeenShareIds)
        {
            _recentShareDiagnostics.RemoveAt(0);
        }
        _state.RecentRejectedShareDiagnostics = _state.RecentRejectedShareDiagnostics
            .Where(item => item.TimestampUtc >= cutoffUtc && !item.Accepted)
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(MaxRecentRejectedShareDiagnostics)
            .ToList();
    }

    private void TrimCoinbaserDiagnosticsNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc.AddHours(-GetShareDiagnosticRetentionHours());
        _state.RecentCoinbaserDiagnostics = _state.RecentCoinbaserDiagnostics
            .Where(item => item.TimestampUtc >= cutoffUtc)
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(MaxRecentCoinbaserDiagnostics)
            .ToList();
    }

    private void TrimDatumShareResponsesNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc.AddHours(-GetShareDiagnosticRetentionHours());
        _state.RecentDatumShareResponses = _state.RecentDatumShareResponses
            .Where(item => item.TimestampUtc >= cutoffUtc)
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(MaxRecentDatumShareResponses)
            .ToList();
    }

    private void TrimDatumSessionsNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc.AddHours(-GetShareDiagnosticRetentionHours());
        _state.RecentDatumSessions = _state.RecentDatumSessions
            .Where(item => item.ClosedUtc == null || item.ClosedUtc >= cutoffUtc || item.StartedUtc >= cutoffUtc)
            .OrderBy(item => item.StartedUtc)
            .ToList();

        while (_state.RecentDatumSessions.Count > MaxRecentDatumSessions)
        {
            int removeIndex = _state.RecentDatumSessions.FindIndex(item => item.ClosedUtc != null);
            if (removeIndex < 0)
            {
                break;
            }

            _activeDatumSessions.Remove(_state.RecentDatumSessions[removeIndex].SessionId);
            _state.RecentDatumSessions.RemoveAt(removeIndex);
        }
    }

    private void TrimNetworkEventsNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc.AddHours(-GetShareDiagnosticRetentionHours());
        _state.RecentNetworkEvents = _state.RecentNetworkEvents
            .Where(item => item.TimestampUtc >= cutoffUtc)
            .OrderBy(item => item.TimestampUtc)
            .TakeLast(MaxRecentNetworkEvents)
            .ToList();
    }

    private void TrimAcceptedShareTelemetryNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc.AddHours(-GetAcceptedShareTelemetryRetentionHours());
        _state.RecentAcceptedShares = _state.RecentAcceptedShares
            .Where(share => share.TimestampUtc >= cutoffUtc)
            .OrderBy(share => share.TimestampUtc)
            .ToList();
    }

    private void TrimHashrateSamplesNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc.AddDays(-GetHashrateSampleRetentionDays());
        _state.HashrateSamples = _state.HashrateSamples
            .Where(point => point.TimestampUtc >= cutoffUtc)
            .OrderBy(point => point.TimestampUtc)
            .ToList();
    }

    private int GetHashrateSampleIntervalSeconds() => Math.Clamp(_poolConfig.HashrateSampleIntervalSeconds, 10, 3600);

    private int GetHashrateLocalWindowSeconds() => Math.Clamp(_poolConfig.HashrateLocalWindowSeconds, 60, 86400);

    private int GetHashrateSampleRetentionDays() => Math.Clamp(_poolConfig.HashrateSampleRetentionDays, 1, 365);

    private int GetAcceptedShareTelemetryRetentionHours() => Math.Clamp(_poolConfig.AcceptedShareTelemetryRetentionHours, 1, 168);

    private int GetShareDiagnosticRetentionHours() => Math.Clamp(_poolConfig.ShareDiagnosticRetentionHours, 1, 168);

    private int GetDatumShareResponseSlowMs() => Math.Clamp(_poolConfig.DatumShareResponseSlowMs, 50, 30000);

    private DateTime ResolveTelemetryCutoffUtc(string? windowKey, DateTime nowUtc, int retentionHours)
    {
        DateTime retentionCutoffUtc = nowUtc.AddHours(-Math.Clamp(retentionHours, 1, 168));
        TimeSpan? requestedWindow = windowKey?.ToLowerInvariant() switch
        {
            "1h" => TimeSpan.FromHours(1),
            "6h" => TimeSpan.FromHours(6),
            "12h" => TimeSpan.FromHours(12),
            "24h" => TimeSpan.FromHours(24),
            "48h" => TimeSpan.FromHours(48),
            "7d" => TimeSpan.FromDays(7),
            _ => null
        };

        if (!requestedWindow.HasValue)
        {
            return retentionCutoffUtc;
        }

        DateTime requestedCutoffUtc = nowUtc.Subtract(requestedWindow.Value);
        return requestedCutoffUtc > retentionCutoffUtc ? requestedCutoffUtc : retentionCutoffUtc;
    }

    private DateTime ResolveHashrateSeriesCutoffUtc(string? windowKey, DateTime nowUtc)
    {
        DateTime retentionCutoffUtc = nowUtc.AddDays(-GetHashrateSampleRetentionDays());
        TimeSpan? requestedWindow = windowKey?.ToLowerInvariant() switch
        {
            "6h" => TimeSpan.FromHours(6),
            "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            "30d" => TimeSpan.FromDays(30),
            _ => null
        };

        if (!requestedWindow.HasValue)
        {
            return retentionCutoffUtc;
        }

        DateTime requestedCutoffUtc = nowUtc.Subtract(requestedWindow.Value);
        return requestedCutoffUtc > retentionCutoffUtc ? requestedCutoffUtc : retentionCutoffUtc;
    }

    private static string? NormalizeDiagnosticReason(string? rejectionReason)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
        {
            return null;
        }

        if (rejectionReason.StartsWith("Coinbase winners payouts do not match", StringComparison.OrdinalIgnoreCase))
        {
            return "Payout mismatch";
        }

        if (rejectionReason.StartsWith("Coinbase appears to use a non-Boot single-recipient template", StringComparison.OrdinalIgnoreCase))
        {
            return "Solo fallback template";
        }

        if (rejectionReason.StartsWith("Share builds on the wrong parent block", StringComparison.OrdinalIgnoreCase))
        {
            return "Wrong parent block";
        }

        if (rejectionReason.StartsWith("Coinbase slot 0 does not pay", StringComparison.OrdinalIgnoreCase))
        {
            return "Slot 0 mismatch";
        }

        if (rejectionReason.StartsWith("Prev block hash does not match", StringComparison.OrdinalIgnoreCase))
        {
            return "Prev block mismatch";
        }

        if (rejectionReason.StartsWith("Round changed during validation", StringComparison.OrdinalIgnoreCase))
        {
            return "Round changed";
        }

        if (rejectionReason.StartsWith("Accepted parent set changed during validation", StringComparison.OrdinalIgnoreCase))
        {
            return "Accepted parent changed";
        }

        if (rejectionReason.StartsWith("Duplicate share", StringComparison.OrdinalIgnoreCase))
        {
            return "Duplicate share";
        }

        if (rejectionReason.StartsWith("Low difficulty", StringComparison.OrdinalIgnoreCase))
        {
            return "Low difficulty";
        }

        return rejectionReason;
    }

    private static double? EstimateRankAdjustedHashrateThs(IEnumerable<double> difficulties, long? elapsedSeconds)
    {
        if (!elapsedSeconds.HasValue || elapsedSeconds.Value <= 0)
        {
            return null;
        }

        List<double> rankedDifficulties = difficulties
            .Where(difficulty => difficulty > 0)
            .OrderByDescending(difficulty => difficulty)
            .ToList();
        if (rankedDifficulties.Count == 0)
        {
            return null;
        }

        var perShareEstimatesThs = new List<double>(rankedDifficulties.Count);
        for (int index = 0; index < rankedDifficulties.Count; index++)
        {
            double hashesPerSecond = (index + 1) * rankedDifficulties[index] * 4294967296d / elapsedSeconds.Value;
            perShareEstimatesThs.Add(hashesPerSecond / 1_000_000_000_000d);
        }

        perShareEstimatesThs.Sort();
        int middle = perShareEstimatesThs.Count / 2;
        if (perShareEstimatesThs.Count % 2 == 1)
        {
            return perShareEstimatesThs[middle];
        }

        return (perShareEstimatesThs[middle - 1] + perShareEstimatesThs[middle]) / 2d;
    }

    private static string FormatObservedHashrate(double? hashrateThs)
    {
        if (!hashrateThs.HasValue)
        {
            return "--";
        }

        double hashesPerSecond = hashrateThs.Value * 1_000_000_000_000d;
        if (hashesPerSecond >= 1_000_000_000_000_000_000d)
        {
            return $"{hashesPerSecond / 1_000_000_000_000_000_000d:0.##} EH/s";
        }

        if (hashesPerSecond >= 1_000_000_000_000_000d)
        {
            return $"{hashesPerSecond / 1_000_000_000_000_000d:0.##} PH/s";
        }

        if (hashesPerSecond >= 1_000_000_000_000d)
        {
            return $"{hashesPerSecond / 1_000_000_000_000d:0.##} TH/s";
        }

        if (hashesPerSecond >= 1_000_000_000d)
        {
            return $"{hashesPerSecond / 1_000_000_000d:0.##} GH/s";
        }

        if (hashesPerSecond >= 1_000_000d)
        {
            return $"{hashesPerSecond / 1_000_000d:0.##} MH/s";
        }

        if (hashesPerSecond >= 1_000d)
        {
            return $"{hashesPerSecond / 1_000d:0.##} kH/s";
        }

        return $"{hashesPerSecond:0.##} H/s";
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

    private void CacheCurrentCandidateBundleNoLock()
    {
        CacheCandidateBundleNoLock(BuildBundleFromCurrentCandidateNoLock());
    }

    private void CacheCandidateBundleNoLock(BootStateBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.StateId))
        {
            return;
        }

        _recentCandidateBundles.RemoveAll(existing =>
            string.Equals(existing.StateId, bundle.StateId, StringComparison.OrdinalIgnoreCase));
        _recentCandidateBundles.Add(CloneBundle(bundle));

        if (_recentCandidateBundles.Count <= MaxRecentCandidateBundles)
        {
            return;
        }

        int overflow = _recentCandidateBundles.Count - MaxRecentCandidateBundles;
        _recentCandidateBundles.RemoveRange(0, overflow);
    }

    private BootStateBundle BuildBundleFromCurrentCandidateNoLock()
    {
        return new BootStateBundle
        {
            StateId = _state.CandidateStateId,
            PreviousStateId = _state.CurrentStateId,
            Kind = "candidate",
            CurrentRoundNumber = _state.CurrentRoundNumber + 1,
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            NetworkId = _poolConfig.BootNetworkId,
            LockedByBlockHash = null,
            LockedByBlockHeight = null,
            ParentBlockHash = _state.CurrentTipBlockHash,
            ParentBlockHeight = _state.CurrentTipBlockHeight,
            CreatedAtUtc = DateTime.UtcNow,
            TotalDifficulty = _state.OnDeckProofs.Sum(x => x.Difficulty),
            ValidParentBlockHashes = GetAcceptedParentBlockHashesNoLock(),
            WinnersList = ClonePayouts(_state.OnDeckList),
            ProofWinnersList = ClonePayouts(_state.WinnersList),
            ShareProofs = _state.OnDeckProofs.Select(CloneProof).ToList(),
            Commitment = BuildCommitmentNoLock()
        };
    }

    private BootStateBundle BuildBundleFromCurrentWinnersNoLock()
    {
        string? previousStateId = _state.ArchivedStateBundles
            .FirstOrDefault(bundle => string.Equals(bundle.StateId, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase))
            ?.PreviousStateId;

        return new BootStateBundle
        {
            StateId = _state.CurrentStateId,
            PreviousStateId = previousStateId,
            Kind = "current",
            CurrentRoundNumber = _state.CurrentRoundNumber,
            ProtocolVersion = _poolConfig.BootProtocolVersion,
            NetworkId = _poolConfig.BootNetworkId,
            LockedByBlockHash = _state.CurrentTipBlockHash,
            LockedByBlockHeight = _state.CurrentTipBlockHeight,
            ParentBlockHash = null,
            ParentBlockHeight = null,
            CreatedAtUtc = _state.LastRotationUtc ?? DateTime.UtcNow,
            TotalDifficulty = _state.WinnersList.Sum(x => x.Difficulty),
            ValidParentBlockHashes = GetAcceptedParentBlockHashesNoLock(),
            WinnersList = ClonePayouts(_state.WinnersList),
            ProofWinnersList = [],
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
            ShareId = ComputePlaceholderShareId(minerAddress, payout.DiffString, payout.Address),
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
        return ComputeCandidateStateId(_state.CurrentStateId, _state.OnDeckProofs);
    }

    private string BuildTestingRoundResetDescriptionNoLock()
    {
        if (!_poolConfig.TestingRoundResetEnabled)
        {
            return "Disabled";
        }

        return _poolConfig.TestingRoundResetMode switch
        {
            "block_hash_low_nibble" =>
                $"Auto-rotate when a new Bitcoin block hash ends in hex 0-{Math.Max(0, _poolConfig.TestingRoundResetLowNibbleThreshold - 1):x}.",
            _ => "Disabled"
        };
    }

    private bool IsPlaceholderOrEmptyCurrentStateNoLock()
    {
        return (_state.WinnersList.Count == 0 && _state.OnDeckList.Count == 0 && _state.OnDeckProofs.Count == 0) ||
               (_state.WinnersList.Count == 1 &&
                _state.OnDeckList.Count == 0 &&
                _state.OnDeckProofs.Count == 0 &&
                _state.WinnersList[0].Difficulty <= 0 &&
                _state.WinnersList[0].Value == Program.BLOCK_REWARD / 2);
    }

    private List<string> GetAcceptedParentBlockHashesNoLock()
    {
        var normalized = new List<string>();

        void addHash(string? hash)
        {
            string? canonical = NormalizeCanonicalBlockHash(hash);
            if (string.IsNullOrWhiteSpace(canonical) ||
                normalized.Any(existing => BitcoinHashes.AreEquivalent(existing, canonical)))
            {
                return;
            }

            normalized.Add(canonical);
        }

        foreach (string hash in _state.AcceptedParentBlockHashes)
        {
            addHash(hash);
        }

        foreach (BootShareProof proof in _state.OnDeckProofs)
        {
            addHash(proof.PrevBlockHash);
        }

        addHash(_state.CurrentTipBlockHash);

        return normalized
            .Take(MaxAcceptedParentBlockHashes)
            .ToList();
    }

    private void ResetAcceptedParentBlockHashesNoLock(string? primaryHash)
    {
        _state.AcceptedParentBlockHashes = [];
        RememberAcceptedParentBlockHashNoLock(primaryHash);
    }

    private void PreserveAcceptedParentContinuityAfterRotationNoLock(string? previousTipBlockHash, string? newTipBlockHash)
    {
        _state.AcceptedParentBlockHashes = [];
        RememberAcceptedParentBlockHashNoLock(previousTipBlockHash);
        RememberAcceptedParentBlockHashNoLock(newTipBlockHash);
    }

    private void SetAcceptedParentBlockHashesNoLock(IEnumerable<string> hashes, string? currentTipBlockHash)
    {
        _state.AcceptedParentBlockHashes = [];
        foreach (string hash in hashes.Reverse())
        {
            RememberAcceptedParentBlockHashNoLock(hash);
        }

        RememberAcceptedParentBlockHashNoLock(currentTipBlockHash);
    }

    private void TrimAcceptedParentBlockHashesToRoundNoLock(string? roundStartBlockHash, string? currentTipBlockHash)
    {
        string? normalizedRoundStart = NormalizeCanonicalBlockHash(roundStartBlockHash);
        string? normalizedCurrentTip = NormalizeCanonicalBlockHash(currentTipBlockHash);

        if (string.IsNullOrWhiteSpace(normalizedRoundStart))
        {
            ResetAcceptedParentBlockHashesNoLock(normalizedCurrentTip);
            return;
        }

        List<string> accepted = GetAcceptedParentBlockHashesNoLock();
        int roundStartIndex = accepted.FindIndex(existing => BitcoinHashes.AreEquivalent(existing, normalizedRoundStart));
        if (roundStartIndex < 0)
        {
            ResetAcceptedParentBlockHashesNoLock(normalizedCurrentTip ?? normalizedRoundStart);
            if (!string.IsNullOrWhiteSpace(normalizedRoundStart) &&
                !BitcoinHashes.AreEquivalent(normalizedCurrentTip, normalizedRoundStart))
            {
                RememberAcceptedParentBlockHashNoLock(normalizedRoundStart);
            }

            return;
        }

        _state.AcceptedParentBlockHashes = accepted
            .Take(roundStartIndex + 1)
            .Select(NormalizeCanonicalBlockHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Cast<string>()
            .ToList();
    }

    private void RememberAcceptedParentBlockHashNoLock(string? blockHash)
    {
        string? normalized = NormalizeCanonicalBlockHash(blockHash);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _state.AcceptedParentBlockHashes.RemoveAll(existing => BitcoinHashes.AreEquivalent(existing, normalized));
        _state.AcceptedParentBlockHashes.Insert(0, normalized);

        while (_state.AcceptedParentBlockHashes.Count > MaxAcceptedParentBlockHashes)
        {
            _state.AcceptedParentBlockHashes.RemoveAt(_state.AcceptedParentBlockHashes.Count - 1);
        }
    }

    private bool IsAcceptedParentBlockHashNoLock(string? blockHash)
    {
        string? normalized = NormalizeCanonicalBlockHash(blockHash);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return GetAcceptedParentBlockHashesNoLock()
            .Any(existing => BitcoinHashes.AreEquivalent(existing, normalized));
    }

    private List<BootShareProof> ValidateImportedProofs(
        IEnumerable<BootShareProof> shareProofs,
        IReadOnlyList<PayoutInfo> expectedWinners,
        IReadOnlyCollection<string> expectedPrevBlockHashes,
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
            BootShareValidationResult validation = _shareVerifier.ValidateShare(proof, expectedWinners, expectedPrevBlockHashes);
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

    private string ComputeCandidateStateId(string? currentStateId, IEnumerable<BootShareProof> shares)
    {
        var builder = new StringBuilder();
        builder.Append("boot-protocol-candidate-state").Append('\n');
        builder.Append(_poolConfig.BootProtocolVersion).Append('\n');
        builder.Append(_poolConfig.BootNetworkId).Append('\n');
        builder.Append(currentStateId ?? string.Empty).Append('\n');

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

    private static List<string> NormalizeAcceptedParentBlockHashes(IEnumerable<string?> hashes)
    {
        var normalized = new List<string>();
        foreach (string? hash in hashes)
        {
            string? canonical = NormalizeCanonicalBlockHash(hash);
            if (string.IsNullOrWhiteSpace(canonical) ||
                normalized.Any(existing => BitcoinHashes.AreEquivalent(existing, canonical)))
            {
                continue;
            }

            normalized.Add(canonical);
        }

        return normalized;
    }

    private static List<string> MergeAcceptedParentBlockHashes(
        IEnumerable<string> primary,
        IEnumerable<string> secondary)
    {
        return NormalizeAcceptedParentBlockHashes(primary.Concat(secondary));
    }

    private bool ShouldTriggerTestingRoundResetNoLock(string normalizedBlockHash)
    {
        if (!_poolConfig.TestingRoundResetEnabled ||
            string.IsNullOrWhiteSpace(normalizedBlockHash) ||
            string.Equals(_poolConfig.TestingRoundResetMode, "none", StringComparison.OrdinalIgnoreCase) ||
            _state.OnDeckProofs.Count == 0)
        {
            return false;
        }

        if (BitcoinHashes.AreEquivalent(_state.LastTestingTriggerBlockHash, normalizedBlockHash))
        {
            return false;
        }

        if (string.Equals(_poolConfig.TestingRoundResetMode, "block_hash_low_nibble", StringComparison.OrdinalIgnoreCase))
        {
            int nibble = Convert.ToInt32(normalizedBlockHash[^1].ToString(), 16);
            return nibble < _poolConfig.TestingRoundResetLowNibbleThreshold;
        }

        return false;
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

    private async Task NotifyWorkTemplatesInvalidatedAsync(string reason)
    {
        Func<string, Task>? handlers = WorkTemplatesInvalidated;
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
                _logger.LogWarning(ex, "A WorkTemplatesInvalidated handler failed for reason {Reason}.", reason);
            }
        }
    }

    private static string ComputePlaceholderShareId(string minerAddress, string diffString, string payoutAddress)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{minerAddress}|{diffString}|{payoutAddress}"));
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

    private void NormalizeArchivedBundlesNoLock()
    {
        for (int index = 0; index < _state.ArchivedStateBundles.Count; index++)
        {
            BootStateBundle bundle = _state.ArchivedStateBundles[index];
            bundle.PreviousStateId = string.IsNullOrWhiteSpace(bundle.PreviousStateId) ? null : bundle.PreviousStateId;
            bundle.LockedByBlockHash = NormalizeCanonicalBlockHash(bundle.LockedByBlockHash);
            bundle.ParentBlockHash = NormalizeCanonicalBlockHash(bundle.ParentBlockHash);
            bundle.ValidParentBlockHashes = NormalizeAcceptedParentBlockHashes(
                bundle.ValidParentBlockHashes
                    .Append(bundle.ParentBlockHash)
                    .Append(bundle.LockedByBlockHash));
            bundle.WinnersList = ClonePayouts(bundle.WinnersList);
            bundle.ProofWinnersList = ClonePayouts(bundle.ProofWinnersList);
            bundle.ShareProofs = bundle.ShareProofs
                .Select(CloneProof)
                .OrderByDescending(x => x.Difficulty)
                .ThenBy(x => x.ShareId, StringComparer.Ordinal)
                .ToList();

            if (bundle.ShareProofs.Count > 0 && bundle.ProofWinnersList.Count == 0)
            {
                List<PayoutInfo>? inferredProofWinners = index + 1 < _state.ArchivedStateBundles.Count
                    ? ClonePayouts(_state.ArchivedStateBundles[index + 1].WinnersList)
                    : null;
                if (inferredProofWinners is { Count: > 0 })
                {
                    bundle.ProofWinnersList = inferredProofWinners;
                }
            }
        }
    }

    private void EnsureRoundMetadataNoLock()
    {
        if (_state.ArchivedStateBundles.Count == 0)
        {
            _state.CurrentRoundNumber = Math.Max(0, _state.CurrentRoundNumber);
            return;
        }

        var orderedByTime = _state.ArchivedStateBundles
            .OrderBy(bundle => bundle.CreatedAtUtc == default ? DateTime.MaxValue : bundle.CreatedAtUtc)
            .ThenBy(bundle => bundle.StateId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string genesisStateId = ComputeStateIdFromPayoutsNoLock(BuildGenesisWinnersListNoLock(), null);
        string? previousStateId = genesisStateId;
        int nextCurrentRoundNumber = 1;

        foreach (BootStateBundle bundle in orderedByTime)
        {
            if (string.IsNullOrWhiteSpace(bundle.PreviousStateId))
            {
                bundle.PreviousStateId = previousStateId;
            }

            if (bundle.CurrentRoundNumber <= 0)
            {
                bundle.CurrentRoundNumber = nextCurrentRoundNumber;
            }

            previousStateId = bundle.StateId;
            nextCurrentRoundNumber = Math.Max(nextCurrentRoundNumber + 1, bundle.CurrentRoundNumber + 1);
        }

        if (_state.CurrentRoundNumber <= 0)
        {
            BootStateBundle? currentBundle = _state.ArchivedStateBundles.FirstOrDefault(bundle =>
                string.Equals(bundle.StateId, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase));
            _state.CurrentRoundNumber = currentBundle?.CurrentRoundNumber ?? Math.Max(0, nextCurrentRoundNumber - 1);
        }
    }

    private bool UpdateKnownBlockHeightNoLock(string? blockHash, long? blockHeight)
    {
        if (string.IsNullOrWhiteSpace(blockHash) || !blockHeight.HasValue || blockHeight <= 0)
        {
            return false;
        }

        bool changed = false;
        if (BitcoinHashes.AreEquivalent(blockHash, _state.CurrentTipBlockHash) &&
            _state.CurrentTipBlockHeight != blockHeight)
        {
            _state.CurrentTipBlockHeight = blockHeight;
            changed = true;
        }

        if (BitcoinHashes.AreEquivalent(blockHash, _state.LastTestingTriggerBlockHash) &&
            _state.LastTestingTriggerBlockHeight != blockHeight)
        {
            _state.LastTestingTriggerBlockHeight = blockHeight;
            changed = true;
        }

        foreach (BootStateBundle bundle in _state.ArchivedStateBundles)
        {
            if (BitcoinHashes.AreEquivalent(blockHash, bundle.LockedByBlockHash) &&
                bundle.LockedByBlockHeight != blockHeight)
            {
                bundle.LockedByBlockHeight = blockHeight;
                changed = true;
            }

            if (BitcoinHashes.AreEquivalent(blockHash, bundle.ParentBlockHash) &&
                bundle.ParentBlockHeight != blockHeight)
            {
                bundle.ParentBlockHeight = blockHeight;
                changed = true;
            }
        }

        return changed;
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

    private void RecordFreshParentRetryEvent(
        string eventType,
        string source,
        string? blockHash,
        string message)
    {
        lock (_sync)
        {
            RecordNetworkEventNoLock(eventType, source, message, blockHash, blockHeight: null);
            RequestDeferredHistorySaveNoLock();
        }
    }

    private bool TryLearnFreshParentFromTrustedShare(
        string source,
        BootShareValidationResult validation,
        string expectedStateId)
    {
        if (!IsTrustedFreshParentSource(source) ||
            string.IsNullOrWhiteSpace(validation.PrevBlockHash))
        {
            return false;
        }

        lock (_sync)
        {
            if (!string.Equals(expectedStateId, _state.CurrentStateId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsAcceptedParentBlockHashNoLock(validation.PrevBlockHash))
            {
                return true;
            }

            RememberAcceptedParentBlockHashNoLock(validation.PrevBlockHash);
            RecordNetworkEventNoLock(
                "fresh-parent-learned",
                source,
                $"Learned fresh parent from otherwise-valid local share at difficulty {ClientHandler.FormatDifficulty(validation.Difficulty)}.",
                validation.PrevBlockHash,
                blockHeight: null);
            RequestDeferredSaveNoLock();
            RequestDeferredHistorySaveNoLock();
            return true;
        }
    }

    private static bool IsTrustedFreshParentSource(string source)
    {
        return string.Equals(source, "datum", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWrongParentRejection(string? rejectionReason)
    {
        return !string.IsNullOrWhiteSpace(rejectionReason) &&
               rejectionReason.StartsWith("Share builds on the wrong parent block", StringComparison.OrdinalIgnoreCase);
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

    private static BootHashratePoint CloneHashratePoint(BootHashratePoint point)
    {
        return new BootHashratePoint
        {
            TimestampUtc = point.TimestampUtc,
            CurrentRoundNumber = point.CurrentRoundNumber,
            TeamEstimatedHashrateThs = point.TeamEstimatedHashrateThs,
            TeamEstimatedHashrateDisplay = point.TeamEstimatedHashrateDisplay,
            LocalDatumHashrateThs = point.LocalDatumHashrateThs,
            LocalDatumHashrateDisplay = point.LocalDatumHashrateDisplay
        };
    }

    private static BootAcceptedShareTelemetry CloneAcceptedShareTelemetry(BootAcceptedShareTelemetry telemetry)
    {
        return new BootAcceptedShareTelemetry
        {
            MinerAddress = telemetry.MinerAddress,
            Username = telemetry.Username,
            Source = telemetry.Source,
            Difficulty = telemetry.Difficulty,
            TimestampUtc = telemetry.TimestampUtc
        };
    }

    private static BootShareDiagnosticTelemetry CloneShareDiagnostic(BootShareDiagnosticTelemetry diagnostic)
    {
        return new BootShareDiagnosticTelemetry
        {
            Source = diagnostic.Source,
            MinerAddress = diagnostic.MinerAddress,
            Username = diagnostic.Username,
            Accepted = diagnostic.Accepted,
            AffectedOnDeck = diagnostic.AffectedOnDeck,
            RejectionReason = diagnostic.RejectionReason,
            RejectionCategory = diagnostic.RejectionCategory,
            Difficulty = diagnostic.Difficulty,
            CurrentRoundNumber = diagnostic.CurrentRoundNumber,
            CurrentStateId = diagnostic.CurrentStateId,
            CandidateStateId = diagnostic.CandidateStateId,
            CurrentTipBlockHash = diagnostic.CurrentTipBlockHash,
            CurrentTipBlockHeight = diagnostic.CurrentTipBlockHeight,
            TimestampUtc = diagnostic.TimestampUtc
        };
    }

    private static BootCoinbaserFetchTelemetry CloneCoinbaserDiagnostic(BootCoinbaserFetchTelemetry diagnostic)
    {
        return new BootCoinbaserFetchTelemetry
        {
            Source = diagnostic.Source,
            RemoteEndpoint = diagnostic.RemoteEndpoint,
            ClientIdentityPreview = diagnostic.ClientIdentityPreview,
            RequestSequence = diagnostic.RequestSequence,
            RewardValue = diagnostic.RewardValue,
            TeamPayoutTotal = diagnostic.TeamPayoutTotal,
            SlotZeroValue = diagnostic.SlotZeroValue,
            SlotZeroAddress = diagnostic.SlotZeroAddress,
            UsingTemporarySlotZero = diagnostic.UsingTemporarySlotZero,
            WinnersCount = diagnostic.WinnersCount,
            CoinbaseOutputCount = diagnostic.CoinbaseOutputCount,
            ResponsePayloadBytes = diagnostic.ResponsePayloadBytes,
            DurationMs = diagnostic.DurationMs,
            ParseDurationMs = diagnostic.ParseDurationMs,
            StateReadDurationMs = diagnostic.StateReadDurationMs,
            BuildDurationMs = diagnostic.BuildDurationMs,
            SerializeDurationMs = diagnostic.SerializeDurationMs,
            SendDurationMs = diagnostic.SendDurationMs,
            CurrentRoundNumber = diagnostic.CurrentRoundNumber,
            CurrentStateId = diagnostic.CurrentStateId,
            CandidateStateId = diagnostic.CandidateStateId,
            CurrentTipBlockHash = diagnostic.CurrentTipBlockHash,
            CurrentTipBlockHeight = diagnostic.CurrentTipBlockHeight,
            TimestampUtc = diagnostic.TimestampUtc
        };
    }

    private static BootDatumShareResponseTelemetry CloneDatumShareResponse(BootDatumShareResponseTelemetry telemetry)
    {
        return new BootDatumShareResponseTelemetry
        {
            SessionId = telemetry.SessionId,
            RemoteEndpoint = telemetry.RemoteEndpoint,
            MinerAddress = telemetry.MinerAddress,
            Username = telemetry.Username,
            Accepted = telemetry.Accepted,
            AffectedOnDeck = telemetry.AffectedOnDeck,
            RejectionReason = telemetry.RejectionReason,
            Difficulty = telemetry.Difficulty,
            PrevBlockHash = telemetry.PrevBlockHash,
            JobId = telemetry.JobId,
            CoinbaseId = telemetry.CoinbaseId,
            Nonce = telemetry.Nonce,
            IsBlock = telemetry.IsBlock,
            SubsidyOnly = telemetry.SubsidyOnly,
            QuickDiff = telemetry.QuickDiff,
            NonceOnlySubmit = telemetry.NonceOnlySubmit,
            UsedCachedJob = telemetry.UsedCachedJob,
            CachedJobAgeMs = telemetry.CachedJobAgeMs,
            TargetByte = telemetry.TargetByte,
            TargetByteIndex = telemetry.TargetByteIndex,
            PayloadBytes = telemetry.PayloadBytes,
            CoinbaseBytes = telemetry.CoinbaseBytes,
            Coinb1Bytes = telemetry.Coinb1Bytes,
            Coinb2Bytes = telemetry.Coinb2Bytes,
            MerkleBranchCount = telemetry.MerkleBranchCount,
            ParseDurationMs = telemetry.ParseDurationMs,
            BuildDurationMs = telemetry.BuildDurationMs,
            ValidationDurationMs = telemetry.ValidationDurationMs,
            StaleHandlingDurationMs = telemetry.StaleHandlingDurationMs,
            ResponseSendDurationMs = telemetry.ResponseSendDurationMs,
            TotalDurationMs = telemetry.TotalDurationMs,
            CurrentRoundNumber = telemetry.CurrentRoundNumber,
            CurrentStateId = telemetry.CurrentStateId,
            CandidateStateId = telemetry.CandidateStateId,
            CurrentTipBlockHash = telemetry.CurrentTipBlockHash,
            CurrentTipBlockHeight = telemetry.CurrentTipBlockHeight,
            TimestampUtc = telemetry.TimestampUtc
        };
    }

    private static BootDatumSessionTelemetry CloneDatumSession(BootDatumSessionTelemetry session)
    {
        return new BootDatumSessionTelemetry
        {
            SessionId = session.SessionId,
            Protocol = session.Protocol,
            RemoteEndpoint = session.RemoteEndpoint,
            ClientIdentityKey = session.ClientIdentityKey,
            ClientEncryptIdentityKey = session.ClientEncryptIdentityKey,
            LockedPayoutAddress = session.LockedPayoutAddress,
            HandshakeCompleted = session.HandshakeCompleted,
            ServerInitiatedClose = session.ServerInitiatedClose,
            ServerCloseEventType = session.ServerCloseEventType,
            CloseDisposition = session.CloseDisposition,
            CloseReason = session.CloseReason,
            HelloCount = session.HelloCount,
            CoinbaserFetchCount = session.CoinbaserFetchCount,
            RefreshRequestCount = session.RefreshRequestCount,
            ShareResponseCount = session.ShareResponseCount,
            AcceptedShareCount = session.AcceptedShareCount,
            RejectedShareCount = session.RejectedShareCount,
            AffectedOnDeckCount = session.AffectedOnDeckCount,
            StartedUtc = session.StartedUtc,
            HelloReceivedUtc = session.HelloReceivedUtc,
            PayoutLockedUtc = session.PayoutLockedUtc,
            LastCoinbaserFetchUtc = session.LastCoinbaserFetchUtc,
            LastShareResponseUtc = session.LastShareResponseUtc,
            LastRefreshRequestUtc = session.LastRefreshRequestUtc,
            LastActivityUtc = session.LastActivityUtc,
            LastActivityType = session.LastActivityType,
            ClosedUtc = session.ClosedUtc,
            DurationMs = session.DurationMs,
            HandshakeMs = session.HandshakeMs,
            IdleBeforeCloseMs = session.IdleBeforeCloseMs
        };
    }

    private static BootNetworkEvent CloneNetworkEvent(BootNetworkEvent networkEvent)
    {
        return new BootNetworkEvent
        {
            EventType = networkEvent.EventType,
            Source = networkEvent.Source,
            Message = networkEvent.Message,
            BlockHash = networkEvent.BlockHash,
            BlockHeight = networkEvent.BlockHeight,
            CurrentRoundNumber = networkEvent.CurrentRoundNumber,
            CurrentStateId = networkEvent.CurrentStateId,
            CandidateStateId = networkEvent.CandidateStateId,
            CurrentTipBlockHash = networkEvent.CurrentTipBlockHash,
            CurrentTipBlockHeight = networkEvent.CurrentTipBlockHeight,
            TimestampUtc = networkEvent.TimestampUtc
        };
    }

    private static BootStateBundle CloneBundle(BootStateBundle bundle)
    {
        return new BootStateBundle
        {
            StateId = bundle.StateId,
            PreviousStateId = bundle.PreviousStateId,
            Kind = bundle.Kind,
            CurrentRoundNumber = bundle.CurrentRoundNumber,
            ProtocolVersion = bundle.ProtocolVersion,
            NetworkId = bundle.NetworkId,
            LockedByBlockHash = bundle.LockedByBlockHash,
            LockedByBlockHeight = bundle.LockedByBlockHeight,
            ParentBlockHash = bundle.ParentBlockHash,
            ParentBlockHeight = bundle.ParentBlockHeight,
            CreatedAtUtc = bundle.CreatedAtUtc,
            TotalDifficulty = bundle.TotalDifficulty,
            ValidParentBlockHashes = bundle.ValidParentBlockHashes.ToList(),
            WinnersList = ClonePayouts(bundle.WinnersList),
            ProofWinnersList = ClonePayouts(bundle.ProofWinnersList),
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
