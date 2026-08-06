using System.Text.Json;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;
namespace GridPool.DashboardSimulator;

public sealed class SimulatorEngine
{
    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly ISimulatorBroadcaster _broadcaster;
    private readonly DashboardVisualizationJournalService _diagramJournal = new();
    private SimulatorState _state;

    public SimulatorEngine(ISimulatorBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
        _state = SimulatorScenarios.Create("healthy-mesh");
    }

    public long Revision { get; private set; } = 1;

    public SimulatorState Read()
    {
        lock (_gate)
        {
            return Clone(_state);
        }
    }

    public async Task ReplaceAsync(SimulatorState state, string topic = "simulator")
    {
        SimulatorScenarios.Normalize(state);
        lock (_gate)
        {
            _diagramJournal.Reset();
            _state = Clone(state);
            Revision++;
        }
        await BroadcastAsync([topic, "diagram", "summary", "reserve", "network", "miners"]);
    }

    public Task LoadScenarioAsync(string id)
    {
        int seed;
        lock (_gate)
        {
            seed = _state.Seed;
        }
        SimulatorState state = SimulatorScenarios.Create(id, seed);
        if (string.Equals(id, "living-minute-c", StringComparison.OrdinalIgnoreCase))
        {
            state.Timeline = SimulatorScenarios.LivingMinuteTimeline(seed);
            state.LoopTimeline = true;
            state.Playing = true;
        }
        return ReplaceAsync(state, "scenario");
    }

    public async Task ApplyAsync(SimulatorAction action, bool timeline = false)
    {
        List<string> topics = ["summary"];
        lock (_gate)
        {
            ApplyLocked(action, topics);
            SimulatorScenarios.Normalize(_state);
            RecordLocked(action, timeline);
            Revision++;
        }
        await BroadcastAsync(topics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task AdvanceAsync(double realSeconds)
    {
        List<SimulatorAction> due = [];
        bool changed = false;
        bool timelineReset = false;
        lock (_gate)
        {
            if (!_state.Playing)
            {
                return;
            }

            double elapsed = Math.Max(0, realSeconds) * _state.Speed;
            _state.VirtualTimeUtc = _state.VirtualTimeUtc.AddSeconds(elapsed);
            _state.TimelineElapsedSeconds += elapsed;
            changed = TickPulseLocked(elapsed);
            if (_state.Timeline != null)
            {
                while (_state.TimelineCursor < _state.Timeline.Events.Count)
                {
                    TimelineEvent item = _state.Timeline.Events[_state.TimelineCursor];
                    if (ParseDuration(item.At).TotalSeconds > _state.TimelineElapsedSeconds)
                    {
                        break;
                    }
                    due.Add(ToAction(item));
                    _state.TimelineCursor++;
                }

                if (_state.TimelineCursor >= _state.Timeline.Events.Count &&
                    _state.LoopTimeline && _state.Timeline.Events.Count > 0 && due.Count == 0)
                {
                    ResetTimelineLocked(true);
                    Revision++;
                    timelineReset = true;
                }
            }

            if (changed)
            {
                Revision++;
            }
        }

        if (changed)
        {
            await BroadcastAsync(["pulse", "diagram"]);
        }
        if (timelineReset)
        {
            await BroadcastAsync(["timeline", "diagram", "summary", "reserve", "network", "miners"]);
        }
        foreach (SimulatorAction action in due)
        {
            await ApplyAsync(action, true);
        }
    }

    public async Task StepTimelineAsync()
    {
        SimulatorAction? action = null;
        lock (_gate)
        {
            if (_state.Timeline != null && _state.TimelineCursor < _state.Timeline.Events.Count)
            {
                TimelineEvent item = _state.Timeline.Events[_state.TimelineCursor++];
                double nextElapsed = ParseDuration(item.At).TotalSeconds;
                _state.VirtualTimeUtc = _state.VirtualTimeUtc.AddSeconds(
                    Math.Max(0, nextElapsed - _state.TimelineElapsedSeconds));
                _state.TimelineElapsedSeconds = nextElapsed;
                action = ToAction(item);
            }
        }
        if (action != null)
        {
            await ApplyAsync(action, true);
        }
    }

    public async Task SetTimelineAsync(TimelineDocument timeline)
    {
        ValidateTimeline(timeline);
        SimulatorState state = SimulatorScenarios.Create(timeline.InitialScenario, timeline.Seed);
        state.Timeline = timeline;
        state.Playing = false;
        await ReplaceAsync(state, "timeline");
    }

    public async Task ResetTimelineAsync()
    {
        lock (_gate)
        {
            ResetTimelineLocked();
            Revision++;
        }
        await BroadcastAsync(["timeline", "summary", "history"]);
    }

    public async Task SetPlayingAsync(bool playing)
    {
        lock (_gate)
        {
            _state.Playing = playing;
            Revision++;
        }
        await BroadcastAsync(["timeline"]);
    }

    public DashboardSummaryDto Summary(string? window)
    {
        SimulatorState state = Read();
        MaybeFail(state);
        Delay(state);
        string normalizedWindow = DashboardWindows.Normalize(window);
        int windowSeconds = (int)DashboardWindows.Supported[normalizedWindow].TotalSeconds;
        int m = Math.Clamp(state.Work.ObservationCount, 0, 897);
        double? boundary = m > 0 && state.Work.PoolHashrateThs > 0
            ? state.Work.PoolHashrateThs * 1e12 * windowSeconds / (m * Math.Pow(2, 32))
            : null;
        double localHashrate = state.Adapters.Where(adapter => adapter.Connected).Sum(adapter => adapter.HashrateThs);
        int peers = state.Peers.Count(peer => peer.Connected);
        string health = !state.Node.MiningSafe ? "unsafe" :
            state.Node.Ready && state.Node.OutboundRelayHealthy && state.Node.VersionCompatible ? "ready" : "degraded";

        return new DashboardSummaryDto
        {
            Revision = Revision,
            GeneratedAtUtc = state.VirtualTimeUtc,
            Node = new DashboardNodeDto
            {
                NodeId = SimulatorScenarios.Id(state.Seed, "simulator-node"),
                DisplayName = state.Node.DisplayName,
                Region = state.Node.Region,
                Role = "development-simulator",
                PublicEndpoint = "synthetic://dashboard",
                NetworkId = state.Node.NetworkId,
                BitcoinNetwork = state.Node.BitcoinNetwork,
                ReleaseVersion = state.Node.ReleaseVersion,
                ConsensusVersion = state.Node.ConsensusVersion,
                ProtocolVersion = state.Node.ProtocolVersion,
                HttpApiVersion = 1,
                ServiceStartedUtc = state.VirtualTimeUtc.AddHours(-9)
            },
            Health = new DashboardHealthDto
            {
                Status = health,
                MiningWorkSafe = state.Node.MiningSafe,
                MiningWorkSafetyReason = state.Node.SafetyReason,
                PeerCount = peers,
                PeerLoopsHealthy = state.Node.PeerLoopsHealthy,
                OutboundRelayHealthy = state.Node.OutboundRelayHealthy,
                BitcoinNotificationMode = "attached-node",
                BitcoinAuthorityClass = "synthetic-full-node",
                BitcoinRpcReachable = state.Node.RpcReachable,
                BitcoinRpcSynced = state.Node.RpcSynced,
                BitcoinInitialBlockDownload = state.Node.InitialBlockDownload,
                CurrentTipBlockHash = state.Chain.TipHash,
                CurrentTipBlockHeight = state.Chain.Height,
                ProvisionalTipBlockHash = state.Chain.ProvisionalTipHash,
                LastPeerPollCompletedUtc = state.VirtualTimeUtc.AddSeconds(-2)
            },
            Snapshot = new DashboardSnapshotDto
            {
                RoundNumber = state.Chain.Round,
                CurrentStateId = state.Chain.CurrentStateId,
                CandidateStateId = state.Chain.CandidateStateId,
                ActiveSnapshotId = state.Chain.ActiveSnapshotId,
                ActiveSnapshotFamilyId = state.Chain.SnapshotFamilyId,
                LockedPayoutCount = state.LockedPayouts.Count,
                LockedProofCount = state.LockedPayouts.Count,
                ReserveCount = state.Reserve.Count,
                ReserveLimit = 897,
                ReserveFloorDifficulty = state.Reserve.Count == 0 ? null : state.Reserve[^1].Difficulty,
                ReserveFloorDifficultyDisplay = FormatDifficulty(
                    state.Reserve.Count == 0 ? null : state.Reserve[^1].Difficulty),
                LastRotationUtc = state.Chain.LastRotationUtc,
                FamilyMemberCount = state.Chain.FamilyMembers,
                FamilyUnionProofCount = Math.Max(state.Chain.FamilyUnionProofs, state.Reserve.Count),
                Reconciliation = new BootSnapshotReconciliationCounters
                {
                    SiblingAdmissions = state.Chain.SiblingAdmissions,
                    UnionAdditions = state.Chain.UnionAdditions,
                    ConvergenceCount = state.Chain.Convergences
                }
            },
            WorkRate = new DashboardWorkRateEstimateDto
            {
                Window = normalizedWindow,
                WindowSeconds = windowSeconds,
                WindowStartUtc = state.VirtualTimeUtc.AddSeconds(-windowSeconds),
                WindowEndUtc = state.VirtualTimeUtc,
                ObservationCount = state.Work.ObservationCount,
                RetainedOrderStatisticCount = m,
                EstimateThs = m == 0 ? null : state.Work.PoolHashrateThs,
                EstimateDisplay = m == 0 ? "--" : FormatHashrate(state.Work.PoolHashrateThs),
                OrderStatisticDifficulty = boundary,
                OrderStatisticDifficultyDisplay = FormatDifficulty(boundary),
                EffectiveAdmissionFloorDifficulty = state.Work.AdmissionFloorDifficulty,
                EffectiveAdmissionFloorDisplay = FormatDifficulty(state.Work.AdmissionFloorDifficulty),
                RelativeStandardErrorPercent = m == 0 ? null : 100 / Math.Sqrt(m),
                Confidence = m switch { < 30 => "collecting", < 100 => "low", < 897 => "medium", _ => "high" },
                Warmup = m < 30,
                CompleteWindow = m >= 30,
                Note = $"Synthetic coherent state. Local adapters report {FormatHashrate(localHashrate)}."
            },
            Pulse = new DashboardPulseDto
            {
                Enabled = state.Pulse.Enabled,
                AcceptedTotal = state.Pulse.Accepted,
                AcceptedInWindow = (int)Math.Min(int.MaxValue, state.Pulse.Accepted),
                AcceptedPerMinute = state.Pulse.Enabled ? 60d / state.Pulse.TargetIntervalSeconds : 0,
                LastAcceptedUtc = state.Pulse.LastAcceptedUtc,
                LastSuccessfulOutboundRelayUtc = state.Pulse.LastRelayUtc,
                OutboundRelayHealthy = state.Node.OutboundRelayHealthy,
                TargetIntervalSeconds = state.Pulse.TargetIntervalSeconds,
                RelayTtl = state.Pulse.RelayTtl
            },
            Capabilities = new DashboardCapabilitiesDto
            {
                WebUiEnabled = true,
                LegacyUiEnabled = false,
                OperatorApiAvailable = true
            }
        };
    }

    public DashboardHistoryDto History(string? window)
    {
        SimulatorState state = Read();
        MaybeFail(state);
        Delay(state);
        string normalized = DashboardWindows.Normalize(window);
        int seconds = (int)DashboardWindows.Supported[normalized].TotalSeconds;
        return new DashboardHistoryDto
        {
            Window = normalized,
            WindowSeconds = seconds,
            GeneratedAtUtc = state.VirtualTimeUtc,
            Points = state.History
                .Where(point => point.TimestampUtc >= state.VirtualTimeUtc.AddSeconds(-seconds))
                .Select(point => new DashboardHistoryPointDto
                {
                    TimestampUtc = point.TimestampUtc,
                    WorkRateThs = point.WorkRateThs,
                    WorkObservationCount = point.ObservationCount,
                    RelativeStandardErrorPercent = point.ObservationCount > 0
                        ? 100 / Math.Sqrt(Math.Min(point.ObservationCount, 897))
                        : null,
                    PulseCount = point.PulseCount
                }).ToList()
        };
    }

    public DashboardAddressDto Address(string address)
    {
        SimulatorState state = Read();
        MaybeFail(state);
        Delay(state);
        string normalized = BitcoinScript.NormalizeAddress(address);
        _ = BitcoinScript.AddressToScriptPubKey(normalized, state.Node.BitcoinNetwork);
        List<PayoutControl> locked = state.LockedPayouts
            .Where(item => item.Address.Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        List<(ProofControl Proof, int Position)> provisional = state.Reserve
            .Select((proof, index) => (proof, index + 1))
            .Where(item => item.proof.Address.Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        double? floor = state.Reserve.Count == 0 ? null : state.Reserve[^1].Difficulty;
        double? best = provisional.Count == 0 ? null : provisional.Max(item => item.Proof.Difficulty);
        return new DashboardAddressDto
        {
            Address = normalized,
            Found = locked.Count > 0 || provisional.Count > 0,
            LockedSlotCount = locked.Count,
            LockedValueSats = (ulong)locked.Sum(item => (decimal)item.ValueSats),
            LockedPositions = locked.Select(item => item.Position).ToList(),
            ProvisionalPositionCount = provisional.Count,
            ProvisionalPositions = provisional.Select(item => item.Position).ToList(),
            BestProvisionalDifficulty = best,
            BestProvisionalDifficultyDisplay = FormatDifficulty(best),
            ReserveFloorDifficulty = floor,
            ReserveFloorDifficultyDisplay = FormatDifficulty(floor),
            EstimatedTop300SurvivalProbability = provisional.Count == 0
                ? null
                : Math.Clamp((897 - provisional.Min(item => item.Position)) / 897d, 0, 1),
            Interpretation = "Synthetic address lookup for dashboard presentation testing."
        };
    }

    public DashboardOperatorDto Operator()
    {
        SimulatorState state = Read();
        MaybeFail(state);
        Delay(state);
        return new DashboardOperatorDto
        {
            GeneratedAtUtc = state.VirtualTimeUtc,
            LocalMiningSources = state.Adapters.Select(adapter => new BootLocalMiningSourceSummaryDto
            {
                Source = adapter.Kind,
                DisplayName = adapter.DisplayName,
                ActiveMinerCount = adapter.Connected ? adapter.ClientCount : 0,
                RecentAcceptedShareCount = adapter.AcceptedShares,
                HashrateSampleCount = adapter.Connected ? Math.Max(1, (int)Math.Min(adapter.AcceptedShares, 1000)) : 0,
                CurrentHashrateThs = adapter.Connected ? adapter.HashrateThs : null,
                CurrentHashrateDisplay = adapter.Connected ? FormatHashrate(adapter.HashrateThs) : "--",
                EstimationMethod = "simulator-control",
                LastShareUtc = adapter.LastShareUtc ?? state.VirtualTimeUtc.AddSeconds(-12)
            }).ToList(),
            LocalMiners = state.Adapters.SelectMany(adapter => adapter.Miners.Select(miner =>
                new BootLocalDatumMinerSummaryDto
                {
                    Address = miner.Address,
                    Username = miner.Username,
                    Source = adapter.Kind,
                    TotalAcceptedShareCount = miner.AcceptedShares,
                    RecentAcceptedShareCount = (int)Math.Min(int.MaxValue, miner.AcceptedShares),
                    HashrateSampleCount = miner.AcceptedShares > 0 ? 1 : 0,
                    CurrentHashrateThs = miner.HashrateThs,
                    CurrentHashrateDisplay = FormatHashrate(miner.HashrateThs),
                    LastShareUtc = miner.LastShareUtc
                })).ToList(),
            Peers = state.Peers.Select(peer => new BootPeerStatus
            {
                Endpoint = peer.Endpoint,
                NodeId = peer.Id,
                Status = peer.Connected ? "connected" : "disconnected",
                Source = "simulator",
                ConnectionMode = string.Join("+", Capabilities(peer)),
                SessionConnected = peer.Connected,
                Capabilities = Capabilities(peer),
                LastSuccessUtc = peer.Connected ? state.VirtualTimeUtc.AddMilliseconds(-peer.LatencyMs) : null,
                LatencyMs = peer.LatencyMs,
                LastCurrentStateId = peer.CurrentStateId,
                LastCandidateStateId = peer.CandidateStateId,
                LastTipBlockHash = state.Chain.TipHash,
                CompatibilityStatus = peer.Compatible ? "compatible" : "incompatible",
                CompatibilityReason = peer.Compatible ? string.Empty : "Synthetic protocol version mismatch."
            }).ToList(),
            BitcoinNotification = new BootBitcoinNotificationDto
            {
                Mode = BitcoinNotificationModes.AttachedNode,
                AuthorityClass = "synthetic-full-node",
                MiningSafe = state.Node.MiningSafe,
                DegradedReason = state.Node.MiningSafe ? string.Empty : state.Node.SafetyReason,
                Rpc = new BootBitcoinRpcHealthDto
                {
                    Configured = true,
                    Reachable = state.Node.RpcReachable,
                    Synced = state.Node.RpcSynced,
                    InitialBlockDownload = state.Node.InitialBlockDownload,
                    BestHeight = state.Chain.Height,
                    HeaderHeight = state.Chain.Height,
                    BestBlockHash = state.Chain.TipHash,
                    VerificationProgress = state.Node.RpcSynced ? 1 : 0.94,
                    LastCheckUtc = state.VirtualTimeUtc.AddSeconds(-2),
                    LastSuccessUtc = state.Node.RpcReachable ? state.VirtualTimeUtc.AddSeconds(-2) : null,
                    LastError = state.Node.RpcReachable ? string.Empty : "Synthetic RPC connection failure."
                },
                ZmqTopics =
                [
                    new BootBitcoinZmqTopicHealthDto
                    {
                        Topic = "hashblock",
                        Configured = true,
                        SubscriberRunning = state.Node.ZmqHealthy,
                        PublisherAdvertisedByRpc = state.Node.ZmqHealthy,
                        PublisherCount = state.Node.ZmqHealthy ? 1 : 0,
                        LastEventUtc = state.Chain.LastRotationUtc,
                        LastBlockHash = state.Chain.TipHash
                    },
                    new BootBitcoinZmqTopicHealthDto
                    {
                        Topic = "rawblock",
                        Configured = true,
                        SubscriberRunning = state.Node.ZmqHealthy,
                        PublisherAdvertisedByRpc = state.Node.ZmqHealthy,
                        PublisherCount = state.Node.ZmqHealthy ? 1 : 0,
                        LastEventUtc = state.Chain.LastRotationUtc,
                        LastBlockHash = state.Chain.TipHash
                    }
                ]
            },
            DatumDiagnostics = new BootDatumDiagnosticsDto
            {
                WindowSeconds = 900,
                TotalSubmissions = state.Adapters.Where(adapter => adapter.Kind == "datum")
                    .Sum(adapter => (int)Math.Min(int.MaxValue, adapter.AcceptedShares)),
                AcceptedCount = state.Adapters.Where(adapter => adapter.Kind == "datum")
                    .Sum(adapter => (int)Math.Min(int.MaxValue, adapter.AcceptedShares))
            }
        };
    }

    public DashboardDiagramDto Diagram(bool operatorDetails)
    {
        SimulatorState state = Read();
        MaybeFail(state);
        Delay(state);
        (long oldest, long latest) = _diagramJournal.Bounds(state.VirtualTimeUtc);
        HashSet<string> locked = state.LockedPayouts
            .Select(item => item.ProofId)
            .ToHashSet(StringComparer.Ordinal);
        double localHashrateThs = state.Adapters
            .Where(adapter => adapter.Connected)
            .Sum(adapter => adapter.HashrateThs);
        double remoteHashrateThs = Math.Max(0, state.Work.PoolHashrateThs - localHashrateThs);
        double? remoteRelativeError = remoteHashrateThs > 0 && state.Work.ObservationCount > 0
            ? 100d / Math.Sqrt(state.Work.ObservationCount) * state.Work.PoolHashrateThs / remoteHashrateThs
            : null;
        var result = new DashboardDiagramDto
        {
            GeneratedAtUtc = state.VirtualTimeUtc,
            Redacted = !operatorDetails,
            OldestSequence = oldest,
            LatestSequence = latest,
            SlotZero = new DashboardDiagramSlotZeroDto
            {
                Verified = !string.IsNullOrWhiteSpace(state.SlotZeroAddress),
                Address = state.SlotZeroAddress,
                ObservedUtc = state.SlotZeroObservedUtc
            },
            Grid = new DashboardDiagramGridDto
            {
                HashrateThs = remoteHashrateThs,
                HashrateDisplay = FormatHashrate(remoteHashrateThs),
                RelativeStandardErrorPercent = remoteRelativeError,
                Confidence = remoteHashrateThs <= 0
                    ? "collecting"
                    : state.Work.ObservationCount >= 897
                    ? "high"
                    : state.Work.ObservationCount >= 300 ? "medium" : "collecting"
            },
            Bitcoin = new DashboardDiagramBitcoinDto
            {
                Reachable = state.Node.RpcReachable,
                Synced = state.Node.RpcSynced,
                InitialBlockDownload = state.Node.InitialBlockDownload,
                TipHash = state.Chain.TipHash,
                TipHeight = state.Chain.Height,
                ProvisionalTipHash = state.Chain.ProvisionalTipHash,
                NetworkDifficulty = 129_000_000_000_000d,
                NetworkDifficultyDisplay = "129 T"
            },
            WorkGenerator = new DashboardDiagramWorkGeneratorDto
            {
                DetailAvailable = operatorDetails,
                Connected = state.Adapters.Any(adapter => adapter.Connected),
                DisplayName = state.Adapters.Count == 1
                    ? state.Adapters[0].DisplayName
                    : "Local work generator",
                MinerCount = state.Adapters.Where(adapter => adapter.Connected).Sum(adapter => adapter.Miners.Count),
                HashrateThs = localHashrateThs,
                HashrateDisplay = FormatHashrate(localHashrateThs),
                LastActivityUtc = operatorDetails
                    ? state.Adapters.Select(adapter => adapter.LastShareUtc).DefaultIfEmpty(null).Max()
                    : null
            },
            WorkSet = state.Reserve.Select((proof, index) => new DashboardDiagramProofDto
            {
                VisualId = _diagramJournal.VisualId("proof", proof.Id),
                ProofId = proof.Id,
                Rank = index + 1,
                Address = proof.Address,
                Difficulty = proof.Difficulty,
                DifficultyDisplay = FormatDifficulty(proof.Difficulty),
                FirstSeenUtc = proof.FirstSeenUtc,
                Locked = locked.Contains(proof.Id)
            }).ToList()
        };

        result.Peers = state.Peers.Select(peer => new DashboardDiagramPeerDto
        {
            VisualId = _diagramJournal.VisualId("peer", peer.Id),
            DisplayName = SimulatorPeerDisplayName(peer),
            NodeId = peer.Id,
            Endpoint = operatorDetails ? peer.Endpoint : string.Empty,
            Status = operatorDetails ? peer.Connected ? "connected" : "disconnected" : "redacted",
            Connected = peer.Connected,
            LatencyMs = operatorDetails ? peer.LatencyMs : null,
            LastActivityUtc = operatorDetails && peer.Connected
                ? state.VirtualTimeUtc.AddMilliseconds(-peer.LatencyMs)
                : null,
            CompatibilityStatus = operatorDetails
                ? peer.Compatible ? "compatible" : "incompatible"
                : "redacted"
        }).ToList();
        if (operatorDetails)
        {
            result.Miners = state.Adapters.SelectMany(adapter => adapter.Miners.Select(miner =>
                new DashboardDiagramMinerDto
                {
                    VisualId = _diagramJournal.VisualId(
                        "miner",
                        $"{adapter.Kind}:{miner.Address}:{miner.Username}"),
                    Address = miner.Address,
                    Username = miner.Username,
                    Source = adapter.Kind,
                    HashrateThs = miner.HashrateThs,
                    HashrateDisplay = FormatHashrate(miner.HashrateThs),
                    LastShareUtc = miner.LastShareUtc
                })).ToList();
        }
        else
        {
            result.Miners = state.Adapters.SelectMany(adapter => adapter.Miners.Select(miner =>
                new DashboardDiagramMinerDto
                {
                    VisualId = _diagramJournal.VisualId(
                        "miner",
                        $"{adapter.Kind}:{miner.Address}:{miner.Username}"),
                    HashrateThs = miner.HashrateThs,
                    HashrateDisplay = FormatHashrate(miner.HashrateThs)
                })).ToList();
        }
        return result;
    }

    private static string SimulatorPeerDisplayName(PeerControl peer) => peer.Endpoint switch
    {
        "https://dallas.gridpool.net" => "Dallas",
        "https://detroit.gridpool.net" => "Detroit",
        "https://oregon.gridpool.net" => "Oregon",
        _ => peer.Id
    };

    public DashboardDiagramEventPageDto DiagramEvents(long after, int limit, bool operatorDetails)
    {
        SimulatorState state = Read();
        MaybeFail(state);
        Delay(state);
        return _diagramJournal.Read(
            Math.Max(0, after),
            limit,
            redacted: !operatorDetails,
            state.VirtualTimeUtc);
    }

    private void ApplyLocked(SimulatorAction action, List<string> topics)
    {
        switch (action.Action.Trim().ToLowerInvariant())
        {
            case "peer.disconnect":
                PeerControl disconnectedPeer = FindPeer(action.Peer);
                disconnectedPeer.Connected = false;
                RecordPeerConnectionLocked(disconnectedPeer);
                topics.Add("network");
                topics.Add("diagram");
                break;
            case "peer.reconnect":
                PeerControl reconnectedPeer = FindPeer(action.Peer);
                reconnectedPeer.Connected = true;
                RecordPeerConnectionLocked(reconnectedPeer);
                topics.Add("network");
                topics.Add("diagram");
                break;
            case "peer.transport":
                SetTransport(FindPeer(action.Peer), action.Transport, action.Value is not 0);
                topics.Add("network");
                break;
            case "adapter.disconnect":
                FindAdapter(action.Adapter).Connected = false;
                topics.Add("miners");
                break;
            case "adapter.reconnect":
                FindAdapter(action.Adapter).Connected = true;
                topics.Add("miners");
                break;
            case "adapter.hashrate":
                FindAdapter(action.Adapter).HashrateThs = Math.Max(0, action.Value ?? 0);
                topics.Add("miners");
                break;
            case "pool.hashrate":
                _state.Work.PoolHashrateThs = Math.Max(0, action.Value ?? 0);
                topics.Add("work-rate");
                break;
            case "pulse.emit":
            case "proof.heartbeat":
                EmitPulseLocked(action);
                topics.Add("pulse");
                topics.Add("diagram");
                break;
            case "proof.top897":
                AddProofLocked(action, false);
                topics.Add("reserve");
                topics.Add("diagram");
                break;
            case "proof.top300":
                AddProofLocked(action, true);
                topics.Add("reserve");
                topics.Add("diagram");
                break;
            case "proof.block":
                AddProofLocked(action, true, true);
                topics.Add("reserve");
                topics.Add("diagram");
                break;
            case "miner.activity":
                RecordMinerActivityLocked(action);
                topics.Add("miners");
                topics.Add("diagram");
                break;
            case "chain.peer-header":
                _state.Chain.ProvisionalTipHash = NextId("peer-header");
                _state.Node.SafetyReason = "Verified peer header received; provisional freeze is active.";
                _diagramJournal.Append(new DashboardDiagramEventDto
                {
                    TimestampUtc = _state.VirtualTimeUtc,
                    Kind = DashboardDiagramEventKinds.PeerHeader,
                    SourceKind = "peer",
                    SourceId = action.Peer ?? _state.Peers.FirstOrDefault()?.Id ?? string.Empty,
                    SourceVisualId = _diagramJournal.VisualId(
                        "peer",
                        action.Peer ?? _state.Peers.FirstOrDefault()?.Id ?? string.Empty),
                    Transport = action.Transport ?? "websocket",
                    BlockHash = _state.Chain.ProvisionalTipHash,
                    BlockHeight = _state.Chain.Height + 1,
                    ReceivedUtc = _state.VirtualTimeUtc
                });
                topics.Add("tip");
                topics.Add("diagram");
                break;
            case "chain.local-validate":
                ValidateTipLocked();
                topics.Add("tip");
                topics.Add("snapshot");
                topics.Add("diagram");
                break;
            case "chain.invalid-header":
                _state.Chain.ProvisionalTipHash = string.Empty;
                _state.Node.SafetyReason = "Invalid synthetic peer header was discarded.";
                topics.Add("tip");
                break;
            case "snapshot.regular":
            case "chain.regular-boundary":
                RotateLocked(false);
                topics.Add("snapshot");
                topics.Add("diagram");
                break;
            case "snapshot.gridpool-paid":
            case "chain.gridpool-payment":
                RotateLocked(true);
                topics.Add("snapshot");
                topics.Add("reserve");
                topics.Add("diagram");
                break;
            case "snapshot.sibling-merge":
                MergeSiblingLocked(action.Count ?? 12);
                topics.Add("snapshot");
                topics.Add("reserve");
                topics.Add("diagram");
                break;
            case "state.diverge":
                FindPeer(action.Peer ?? _state.Peers.FirstOrDefault()?.Id).CandidateStateId = NextId("diverge");
                topics.Add("network");
                break;
            case "state.converge":
                foreach (PeerControl peer in _state.Peers)
                {
                    peer.CurrentStateId = _state.Chain.CurrentStateId;
                    peer.CandidateStateId = _state.Chain.CandidateStateId;
                }
                _state.Chain.Convergences++;
                topics.Add("network");
                break;
            case "chain.reorg":
                _state.Chain.Height = Math.Max(0, _state.Chain.Height - Math.Max(1, action.Count ?? 1));
                _state.Chain.TipHash = NextId("reorg-tip");
                _state.Chain.CurrentStateId = NextId("reorg-state");
                _state.Chain.CandidateStateId = NextId("reorg-candidate");
                _state.Chain.Reorganizations++;
                topics.Add("tip");
                topics.Add("snapshot");
                break;
            case "fault.api":
                _state.Faults.ApiFailure = action.Value is not 0;
                break;
            case "fault.api-latency":
                _state.Faults.ApiLatencyMs = Math.Clamp((int)(action.Value ?? 0), 0, 10_000);
                break;
            case "fault.signalr":
                _state.Faults.SignalRDrop = action.Value is not 0;
                break;
            case "timeline.marker":
                topics.Add("timeline");
                break;
            default:
                throw new ArgumentException($"Unsupported simulator action '{action.Action}'.");
        }
    }

    private void RotateLocked(bool gridPoolPayment, string? boundaryTipHash = null)
    {
        if (gridPoolPayment)
        {
            HashSet<string> paid = _state.LockedPayouts.Select(item => item.ProofId)
                .ToHashSet(StringComparer.Ordinal);
            int before = _state.Reserve.Count;
            _state.Reserve.RemoveAll(proof => paid.Contains(proof.Id));
            _state.Chain.PaidProofRemovals += before - _state.Reserve.Count;
        }

        _state.Chain.Height++;
        _state.Chain.Round++;
        _state.Chain.TipHash = boundaryTipHash ?? NextId("tip");
        _state.Chain.ActiveSnapshotId = NextId("snapshot");
        _state.Chain.SnapshotFamilyId = NextId("family");
        _state.Chain.CurrentStateId = NextId("current");
        _state.Chain.CandidateStateId = NextId("candidate");
        _state.Chain.ProvisionalTipHash = string.Empty;
        _state.Chain.LastRotationUtc = _state.VirtualTimeUtc;
        _state.Chain.FamilyMembers = 1;
        _state.LockedPayouts = _state.Reserve.Take(300).Select((proof, index) => new PayoutControl
        {
            ProofId = proof.Id,
            Address = proof.Address,
            Position = index + 1,
            ValueSats = 12_500
        }).ToList();
        _diagramJournal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = _state.VirtualTimeUtc,
            Kind = DashboardDiagramEventKinds.BoundaryValidated,
            SourceKind = "bitcoin",
            SourceId = "local-bitcoin",
            BlockHash = _state.Chain.TipHash,
            BlockHeight = _state.Chain.Height,
            SnapshotId = _state.Chain.ActiveSnapshotId,
            LockedProofIds = _state.LockedPayouts.Select(item => item.ProofId).ToList(),
            MutatedUtc = _state.VirtualTimeUtc
        });
    }

    private void ValidateTipLocked()
    {
        if (string.IsNullOrWhiteSpace(_state.Chain.ProvisionalTipHash))
        {
            _state.Chain.ProvisionalTipHash = NextId("local-tip");
        }
        string validatedTip = _state.Chain.ProvisionalTipHash;
        RotateLocked(false, validatedTip);
        _state.Node.RpcSynced = true;
        _state.Node.MiningSafe = true;
        _state.Node.SafetyReason = "Local full node validated the provisional boundary.";
    }

    private void MergeSiblingLocked(int count)
    {
        _state.Chain.FamilyMembers++;
        _state.Chain.SiblingAdmissions++;
        int before = _state.Reserve.Count;
        for (int index = 0; index < Math.Max(0, count); index++)
        {
            AddProofLocked(new SimulatorAction
            {
                Address = $"tb1qsibling{index:D3}00000000000000000000000"
            }, index < 2);
        }
        _state.Chain.UnionAdditions += _state.Reserve.Count - before;
        _state.Chain.FamilyUnionProofs = _state.Reserve.Count;
        _state.Chain.CandidateStateId = NextId("merged-candidate");
    }

    private void AddProofLocked(SimulatorAction action, bool top300, bool block = false)
    {
        double floor = _state.Reserve.Count == 0
            ? _state.Work.AdmissionFloorDifficulty
            : _state.Reserve[^1].Difficulty;
        int? targetRank = action.Rank;
        double difficulty;
        if (targetRank.HasValue && _state.Reserve.Count > 0)
        {
            int rank = Math.Clamp(targetRank.Value, 1, Math.Min(897, _state.Reserve.Count + 1));
            if (rank == 1)
            {
                difficulty = _state.Reserve[0].Difficulty * 1.05;
            }
            else if (rank > _state.Reserve.Count)
            {
                difficulty = Math.Max(floor * 1.001, _state.Work.AdmissionFloorDifficulty);
            }
            else
            {
                double upper = _state.Reserve[rank - 2].Difficulty;
                double lower = _state.Reserve[rank - 1].Difficulty;
                difficulty = lower + (upper - lower) / 2d;
            }
        }
        else
        {
            difficulty = block
                ? Math.Max(1e12, floor * 10_000)
                : top300
                    ? Math.Max(floor * 4, _state.Reserve.ElementAtOrDefault(Math.Min(299, _state.Reserve.Count - 1))?.Difficulty * 1.05 ?? floor * 4)
                    : floor * 1.05;
        }
        string id = NextId("proof");
        string displaced = _state.Reserve.Count >= 897 ? _state.Reserve[^1].Id : string.Empty;
        var proof = new ProofControl
        {
            Id = id,
            Address = string.IsNullOrWhiteSpace(action.Address)
                ? "tb1qmanualproof000000000000000000000000"
                : action.Address,
            Difficulty = difficulty,
            FirstSeenUtc = _state.VirtualTimeUtc
        };
        _state.Reserve.Add(proof);
        _state.Reserve = _state.Reserve
            .OrderByDescending(item => item.Difficulty)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(897)
            .ToList();
        int admittedRank = _state.Reserve.FindIndex(item => item.Id == id) + 1;
        if (admittedRank <= 0)
        {
            return;
        }
        _state.Work.ObservationCount++;
        (AdapterControl Adapter, MinerControl Miner)? sourceMiner = FindMiner(action.Miner);
        string sourceKind = !string.IsNullOrWhiteSpace(action.Peer)
            ? "peer"
            : sourceMiner.HasValue
                ? "miner"
                : "local";
        string sourceId = action.Peer ?? sourceMiner?.Miner.Id ?? action.Adapter ?? "work-generator";
        string sourceVisualId = !string.IsNullOrWhiteSpace(action.Peer)
            ? _diagramJournal.VisualId("peer", action.Peer)
            : sourceMiner.HasValue
                ? _diagramJournal.VisualId(
                    "miner",
                    $"{sourceMiner.Value.Adapter.Kind}:{sourceMiner.Value.Miner.Address}:{sourceMiner.Value.Miner.Username}")
                : string.Empty;
        _diagramJournal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = _state.VirtualTimeUtc,
            Kind = DashboardDiagramEventKinds.ProofAdmitted,
            SourceKind = sourceKind,
            SourceId = sourceId,
            SourceVisualId = sourceVisualId,
            Transport = action.Transport ?? (string.IsNullOrWhiteSpace(action.Peer) ? "local" : "websocket"),
            ProofId = proof.Id,
            Address = proof.Address,
            Difficulty = proof.Difficulty,
            BlockQuality = block,
            ReceivedUtc = _state.VirtualTimeUtc.AddMilliseconds(-140),
            ValidatedUtc = _state.VirtualTimeUtc.AddMilliseconds(-30),
            MutatedUtc = _state.VirtualTimeUtc,
            Rank = admittedRank,
            DisplacedProofId = string.Equals(displaced, proof.Id, StringComparison.Ordinal)
                ? string.Empty
                : displaced
        });
        if (string.IsNullOrWhiteSpace(action.Peer))
        {
            _state.SlotZeroAddress = proof.Address;
            _state.SlotZeroObservedUtc = _state.VirtualTimeUtc;
        }
    }

    private void RecordMinerActivityLocked(SimulatorAction action)
    {
        (AdapterControl Adapter, MinerControl Miner)? match = _state.Adapters
            .SelectMany(adapter => adapter.Miners.Select(miner => (Adapter: adapter, Miner: miner)))
            .Cast<(AdapterControl Adapter, MinerControl Miner)?>()
            .FirstOrDefault(item => item.HasValue &&
                item.Value.Miner.Id.Equals(action.Miner, StringComparison.OrdinalIgnoreCase));
        if (!match.HasValue)
        {
            throw new ArgumentException($"Unknown miner '{action.Miner}'.");
        }
        long accepted = Math.Max(1, action.Count ?? 1);
        AdapterControl adapter = match.Value.Adapter;
        MinerControl miner = match.Value.Miner;
        miner.AcceptedShares += accepted;
        miner.LastShareUtc = _state.VirtualTimeUtc;
        adapter.AcceptedShares += accepted;
        adapter.LastShareUtc = _state.VirtualTimeUtc;
        _diagramJournal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = _state.VirtualTimeUtc,
            Kind = DashboardDiagramEventKinds.LocalMinerActivity,
            SourceKind = "miner",
            SourceId = miner.Id,
            SourceVisualId = _diagramJournal.VisualId(
                "miner",
                $"{adapter.Kind}:{miner.Address}:{miner.Username}"),
            VisualId = _diagramJournal.VisualId(
                "miner",
                $"{adapter.Kind}:{miner.Address}:{miner.Username}"),
            Address = miner.Address,
            AcceptedShareDelta = accepted,
            HashrateThs = miner.HashrateThs,
            ReceivedUtc = _state.VirtualTimeUtc
        });
    }

    private void RecordPeerConnectionLocked(PeerControl peer)
    {
        _diagramJournal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = _state.VirtualTimeUtc,
            Kind = DashboardDiagramEventKinds.PeerConnection,
            SourceKind = "peer",
            SourceId = peer.Id,
            SourceVisualId = _diagramJournal.VisualId("peer", peer.Id),
            VisualId = _diagramJournal.VisualId("peer", peer.Id),
            Connected = peer.Connected,
            LatencyMs = peer.LatencyMs
        });
    }

    private bool TickPulseLocked(double elapsed)
    {
        if (!_state.Pulse.Enabled)
        {
            return false;
        }
        _state.Pulse.SecondsUntilNext -= elapsed;
        bool changed = false;
        while (_state.Pulse.SecondsUntilNext <= 0)
        {
            EmitPulseLocked();
            _state.Pulse.SecondsUntilNext += _state.Pulse.TargetIntervalSeconds;
            changed = true;
        }
        return changed;
    }

    private void EmitPulseLocked(SimulatorAction? action = null)
    {
        _state.Pulse.Accepted++;
        _state.Pulse.LastAcceptedUtc = _state.VirtualTimeUtc;
        _state.Pulse.LastRelayUtc = _state.VirtualTimeUtc;
        _state.Node.OutboundRelayHealthy = true;
        (AdapterControl Adapter, MinerControl Miner)? sourceMiner = FindMiner(action?.Miner);
        string sourceKind = !string.IsNullOrWhiteSpace(action?.Peer)
            ? "peer"
            : sourceMiner.HasValue
                ? "miner"
                : "local";
        string sourceId = action?.Peer ?? sourceMiner?.Miner.Id ?? "work-generator";
        string sourceVisualId = !string.IsNullOrWhiteSpace(action?.Peer)
            ? _diagramJournal.VisualId("peer", action.Peer)
            : sourceMiner.HasValue
                ? _diagramJournal.VisualId(
                    "miner",
                    $"{sourceMiner.Value.Adapter.Kind}:{sourceMiner.Value.Miner.Address}:{sourceMiner.Value.Miner.Username}")
                : string.Empty;
        _diagramJournal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = _state.VirtualTimeUtc,
            Kind = DashboardDiagramEventKinds.PulseAccepted,
            SourceKind = sourceKind,
            SourceId = sourceId,
            SourceVisualId = sourceVisualId,
            ReceivedUtc = _state.VirtualTimeUtc
        });
    }

    private void ResetTimelineLocked(bool preservePlaying = false)
    {
        TimelineDocument? timeline = _state.Timeline;
        if (timeline == null)
        {
            return;
        }
        bool loop = _state.LoopTimeline;
        double speed = _state.Speed;
        _state = SimulatorScenarios.Create(timeline.InitialScenario, timeline.Seed);
        _diagramJournal.Reset();
        _state.Timeline = timeline;
        _state.LoopTimeline = loop;
        _state.Speed = speed;
        _state.Playing = preservePlaying;
    }

    private void RecordLocked(SimulatorAction action, bool timeline)
    {
        Dictionary<string, string> arguments = [];
        if (action.Peer != null) arguments["peer"] = action.Peer;
        if (action.Adapter != null) arguments["adapter"] = action.Adapter;
        if (action.Miner != null) arguments["miner"] = action.Miner;
        if (action.Address != null) arguments["address"] = action.Address;
        if (action.Transport != null) arguments["transport"] = action.Transport;
        if (action.Value != null) arguments["value"] = action.Value.Value.ToString("G");
        if (action.Count != null) arguments["count"] = action.Count.Value.ToString();
        if (action.Rank != null) arguments["rank"] = action.Rank.Value.ToString();
        _state.Events.Add(new SimulatorEvent
        {
            Sequence = ++_state.Sequence,
            TimestampUtc = _state.VirtualTimeUtc,
            Action = action.Action,
            Summary = timeline ? "Timeline action" : "Manual action",
            Arguments = arguments
        });
        if (_state.Events.Count > 1_000)
        {
            _state.Events.RemoveRange(0, _state.Events.Count - 1_000);
        }
    }

    private async Task BroadcastAsync(string[] topics)
    {
        SimulatorState state = Read();
        if (state.Faults.SignalRDrop)
        {
            return;
        }
        await _broadcaster.BroadcastAsync(new DashboardChangedDto
        {
            Revision = Revision,
            TimestampUtc = state.VirtualTimeUtc,
            Topics = topics.ToList()
        });
    }

    private PeerControl FindPeer(string? id) =>
        _state.Peers.FirstOrDefault(peer => peer.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown peer '{id}'.");

    private (AdapterControl Adapter, MinerControl Miner)? FindMiner(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        return _state.Adapters
            .SelectMany(adapter => adapter.Miners.Select(miner => (Adapter: adapter, Miner: miner)))
            .Cast<(AdapterControl Adapter, MinerControl Miner)?>()
            .FirstOrDefault(item => item.HasValue &&
                item.Value.Miner.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private AdapterControl FindAdapter(string? id) =>
        _state.Adapters.FirstOrDefault(adapter => adapter.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown adapter '{id}'.");

    private string NextId(string label) =>
        SimulatorScenarios.Id(_state.Seed, $"{label}:{++_state.Sequence}");

    private static void SetTransport(PeerControl peer, string? transport, bool enabled)
    {
        switch (transport?.ToLowerInvariant())
        {
            case "http": peer.Http = enabled; break;
            case "websocket":
            case "ws": peer.WebSocket = enabled; break;
            case "udp": peer.Udp = enabled; break;
            default: throw new ArgumentException($"Unknown transport '{transport}'.");
        }
    }

    private static List<string> Capabilities(PeerControl peer)
    {
        List<string> result = [];
        if (peer.Http) result.Add("http");
        if (peer.WebSocket) result.Add("websocket");
        if (peer.Udp) result.Add("udp");
        return result;
    }

    private static void MaybeFail(SimulatorState state)
    {
        if (state.Faults.ApiFailure)
        {
            throw new SimulatorApiException("Synthetic dashboard API failure.");
        }
    }

    private static void Delay(SimulatorState state)
    {
        if (state.Faults.ApiLatencyMs > 0)
        {
            Thread.Sleep(state.Faults.ApiLatencyMs);
        }
    }

    private static string FormatHashrate(double ths) =>
        ths >= 1_000_000 ? $"{ths / 1_000_000:0.##} EH/s" :
        ths >= 1_000 ? $"{ths / 1_000:0.##} PH/s" :
        $"{ths:0.##} TH/s";

    private static string FormatDifficulty(double? difficulty) =>
        difficulty == null ? "--" :
        difficulty >= 1e12 ? $"{difficulty / 1e12:0.##}T" :
        difficulty >= 1e9 ? $"{difficulty / 1e9:0.##}G" :
        difficulty >= 1e6 ? $"{difficulty / 1e6:0.##}M" :
        difficulty.Value.ToString("0.##");

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CloneOptions), CloneOptions)!;

    public static TimeSpan ParseDuration(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.EndsWith("ms") && double.TryParse(normalized[..^2], out double milliseconds))
            return TimeSpan.FromMilliseconds(milliseconds);
        if (normalized.EndsWith('s') && double.TryParse(normalized[..^1], out double seconds))
            return TimeSpan.FromSeconds(seconds);
        if (normalized.EndsWith('m') && double.TryParse(normalized[..^1], out double minutes))
            return TimeSpan.FromMinutes(minutes);
        throw new FormatException($"Invalid timeline duration '{value}'. Use ms, s, or m.");
    }

    private static SimulatorAction ToAction(TimelineEvent item) => new()
    {
        Action = item.Action,
        Peer = item.Peer,
        Adapter = item.Adapter,
        Miner = item.Miner,
        Address = item.Address,
        Transport = item.Transport,
        Value = item.Value,
        Count = item.Count,
        Rank = item.Rank
    };

    public static void ValidateTimeline(TimelineDocument timeline)
    {
        if (timeline.Version != 1) throw new FormatException("Timeline version must be 1.");
        if (!SimulatorScenarios.All.Any(item => item.Id == timeline.InitialScenario))
            throw new FormatException($"Unknown initial scenario '{timeline.InitialScenario}'.");
        double previous = -1;
        foreach (TimelineEvent item in timeline.Events)
        {
            if (string.IsNullOrWhiteSpace(item.Action))
                throw new FormatException("Every timeline event requires an action.");
            double current = ParseDuration(item.At).TotalSeconds;
            if (current < previous) throw new FormatException("Timeline events must be ordered by time.");
            previous = current;
        }
    }
}

public sealed class SimulatorApiException(string message) : Exception(message);

public sealed class SimulatorTicker(SimulatorEngine engine) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await engine.AdvanceAsync(0.1);
        }
    }
}
