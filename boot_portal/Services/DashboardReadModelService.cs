using System.Numerics;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public sealed class DashboardReadModelService
{
    private static readonly BigInteger DifficultyOneTarget = DecodeCompactTarget(0x1d00ffff);
    private readonly PoolConfig _poolConfig;
    private readonly BootProtocolStateService _stateService;
    private readonly DashboardTelemetryService _telemetry;
    private readonly DashboardRevisionService _revision;

    public DashboardReadModelService(
        PoolConfig poolConfig,
        BootProtocolStateService stateService,
        DashboardTelemetryService telemetry,
        DashboardRevisionService revision)
    {
        _poolConfig = poolConfig;
        _stateService = stateService;
        _telemetry = telemetry;
        _revision = revision;
    }

    public DashboardSummaryDto BuildSummary(string? windowKey)
    {
        string window = DashboardWindows.Normalize(windowKey);
        BootNetworkStatusDto status = _stateService.GetPublicNetworkStatus();
        MiningShareAdviceDto advice = _stateService.GetShareAdviceResponse();
        DashboardWorkRateEstimateDto estimate = _telemetry.GetEstimate(window);
        string healthStatus = !status.MiningWorkSafe
            ? "unsafe"
            : !status.PeerLoopsHealthy ||
              !status.OutboundRelayHealthy ||
              !string.IsNullOrWhiteSpace(status.BitcoinNotification.DegradedReason)
                ? "degraded"
                : "ready";

        return new DashboardSummaryDto
        {
            Revision = _revision.CurrentRevision,
            GeneratedAtUtc = DateTime.UtcNow,
            Node = new DashboardNodeDto
            {
                NodeId = status.NodeId,
                DisplayName = status.PublicNodeDisplayName,
                Region = status.PublicNodeRegion,
                Role = status.PublicNodeRole,
                PublicEndpoint = status.SelfEndpoint,
                NetworkId = status.NetworkId,
                BitcoinNetwork = status.BitcoinNetwork,
                ReleaseVersion = status.ReleaseVersion,
                ConsensusVersion = status.ConsensusVersion,
                ProtocolVersion = status.ProtocolVersion,
                HttpApiVersion = status.HttpApiVersion,
                ServiceStartedUtc = status.ServiceStartedUtc
            },
            Health = new DashboardHealthDto
            {
                Status = healthStatus,
                MiningWorkSafe = status.MiningWorkSafe,
                MiningWorkSafetyReason = status.MiningWorkSafetyReason,
                PeerCount = status.PeerCount,
                PeerLoopsHealthy = status.PeerLoopsHealthy,
                OutboundRelayHealthy = status.OutboundRelayHealthy,
                BitcoinNotificationMode = status.BitcoinNotification.Mode,
                BitcoinAuthorityClass = status.BitcoinNotification.AuthorityClass,
                BitcoinRpcReachable = status.BitcoinNotification.Rpc.Reachable,
                BitcoinRpcSynced = status.BitcoinNotification.Rpc.Synced,
                BitcoinInitialBlockDownload = status.BitcoinNotification.Rpc.InitialBlockDownload,
                CurrentTipBlockHash = status.CurrentTipBlockHash ?? string.Empty,
                CurrentTipBlockHeight = status.CurrentTipBlockHeight,
                ProvisionalTipBlockHash = status.ProvisionalTipBlockHash ?? string.Empty,
                LastPeerPollCompletedUtc = status.LastPeerPollCompletedUtc
            },
            Snapshot = new DashboardSnapshotDto
            {
                RoundNumber = status.CurrentRoundNumber,
                CurrentStateId = status.CurrentStateId,
                CandidateStateId = status.CandidateStateId,
                ActiveSnapshotId = status.ActiveSnapshotId,
                ActiveSnapshotFamilyId = status.ActiveSnapshotFamilyId,
                LockedPayoutCount = status.WinnersCount,
                LockedProofCount = status.ActiveSnapshotProofCount,
                ReserveCount = status.WorkSetCount,
                ReserveLimit = status.WorkSetReserveLimit,
                ReserveFloorDifficulty = advice.CurrentWorkSetFloorDifficulty,
                ReserveFloorDifficultyDisplay = advice.CurrentWorkSetFloorDifficultyDisplay,
                LastRotationUtc = status.LastRotationUtc,
                FamilyMemberCount = status.SnapshotFamilyMemberCount,
                FamilyUnionProofCount = status.SnapshotFamilyUnionProofCount,
                Reconciliation = status.ReconciliationCounters
            },
            WorkRate = estimate,
            Pulse = new DashboardPulseDto
            {
                Enabled = status.PulseProofsEnabled,
                AcceptedTotal = status.LocalPulseAcceptedCount,
                AcceptedInWindow = _telemetry.GetPulseCount(window),
                AcceptedPerMinute = status.LocalPulseAcceptRatePerMinute,
                LastAcceptedUtc = status.LastLocalPulseUtc,
                LastSuccessfulOutboundRelayUtc = status.LastSuccessfulOutboundRelayUtc,
                OutboundRelayHealthy = status.OutboundRelayHealthy,
                TargetIntervalSeconds = status.PulseTargetIntervalSeconds,
                RelayTtl = status.PulseRelayTtl
            },
            Capabilities = new DashboardCapabilitiesDto
            {
                WebUiEnabled = _poolConfig.EnableWebUi,
                LegacyUiEnabled = _poolConfig.EnableLegacyUi,
                OperatorApiAvailable =
                    _poolConfig.EnableAdminApi &&
                    !string.IsNullOrWhiteSpace(_poolConfig.AdminApiKey),
                WatchtowerAvailable = false
            }
        };
    }

    public DashboardHistoryDto BuildHistory(string? windowKey) =>
        _telemetry.GetHistory(windowKey);

    public DashboardAddressDto BuildAddress(string address)
    {
        string normalized = BitcoinScript.NormalizeAddress(address);
        _ = BitcoinScript.AddressToScriptPubKey(normalized, _poolConfig.BitcoinNetwork);
        List<PayoutInfo> locked = _stateService.GetWinnersList();
        List<PayoutInfo> reserve = _stateService.GetOnDeckList();
        MiningShareAdviceDto advice = _stateService.GetShareAdviceResponse();
        BootNetworkStatusDto status = _stateService.GetPublicNetworkStatus();

        List<int> lockedPositions = locked
            .Select((payout, index) => new { payout, position = index })
            .Where(item => string.Equals(
                BitcoinScript.NormalizeAddress(item.payout.Address),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.position)
            .ToList();
        List<(PayoutInfo payout, int position)> provisional = reserve
            .Select((payout, index) => (payout, position: index + 1))
            .Where(item => string.Equals(
                BitcoinScript.NormalizeAddress(item.payout.Address),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        double? bestDifficulty = provisional.Count == 0
            ? null
            : provisional.Max(item => item.payout.Difficulty);
        double? survival = bestDifficulty.HasValue && status.CurrentTipCompactTarget.HasValue
            ? CalculateSurvivalProbability(bestDifficulty.Value, status.CurrentTipCompactTarget.Value, 300)
            : null;

        return new DashboardAddressDto
        {
            Address = normalized,
            Found = lockedPositions.Count > 0 || provisional.Count > 0,
            LockedSlotCount = lockedPositions.Count,
            LockedValueSats = locked
                .Where(item => string.Equals(
                    BitcoinScript.NormalizeAddress(item.Address),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
                .Aggregate(0UL, (sum, item) => checked(sum + item.Value)),
            LockedPositions = lockedPositions,
            ProvisionalPositionCount = provisional.Count,
            ProvisionalPositions = provisional.Select(item => item.position).ToList(),
            BestProvisionalDifficulty = bestDifficulty,
            BestProvisionalDifficultyDisplay = bestDifficulty.HasValue
                ? ClientHandler.FormatDifficulty(bestDifficulty.Value)
                : "--",
            ReserveFloorDifficulty = advice.CurrentWorkSetFloorDifficulty,
            ReserveFloorDifficultyDisplay = advice.CurrentWorkSetFloorDifficultyDisplay,
            EstimatedTop300SurvivalProbability = survival,
            Interpretation = lockedPositions.Count > 0
                ? "This address is present in the active payout snapshot used by current block templates."
                : provisional.Count > 0
                    ? "This address is currently ranked in the unpaid reserve. Its position is provisional until a snapshot boundary."
                    : "This address is not present in the active payout snapshot or current unpaid reserve."
        };
    }

    public DashboardOperatorDto BuildOperator()
    {
        BootNetworkStatusDto status = _stateService.GetNetworkStatus();
        return new DashboardOperatorDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            LocalMiningSources = status.LocalMiningSources,
            LocalMiners = status.LocalDatumMiners,
            Peers = status.Peers,
            BitcoinNotification = status.BitcoinNotification,
            DatumDiagnostics = status.LocalDatumDiagnostics,
            CoinbaserDiagnostics = status.CoinbaserDiagnostics,
            PeerLoopFaults = status.PeerLoopFaults
        };
    }

    private static double? CalculateSurvivalProbability(double shareDifficulty, uint compactTarget, int slots)
    {
        if (shareDifficulty <= 0 || slots <= 0)
        {
            return null;
        }

        BigInteger target = DecodeCompactTarget(compactTarget);
        if (target <= 0)
        {
            return null;
        }

        double networkDifficulty = (double)DifficultyOneTarget / (double)target;
        if (!double.IsFinite(networkDifficulty) || networkDifficulty <= 0)
        {
            return null;
        }

        double missOne = networkDifficulty / (shareDifficulty + networkDifficulty);
        return 1d - Math.Pow(missOne, slots);
    }

    private static BigInteger DecodeCompactTarget(uint compact)
    {
        int exponent = (int)(compact >> 24);
        uint mantissa = compact & 0x007fffff;
        if ((compact & 0x00800000) != 0 || mantissa == 0)
        {
            return BigInteger.Zero;
        }

        BigInteger target = mantissa;
        return exponent <= 3
            ? target >> (8 * (3 - exponent))
            : target << (8 * (exponent - 3));
    }
}
