namespace GridPool.DashboardSimulator;

public static class SimulatorScenarios
{
    public static readonly IReadOnlyList<ScenarioDefinition> All =
    [
        new("cold-start", "Cold start", "No peers, no work observations, and an attached node still synchronizing."),
        new("healthy-mesh", "Healthy mixed-transport mesh", "Four compatible peers, mixed adapters, fresh pulses, and synchronized state."),
        new("sovereign-node", "Sovereign single node", "A healthy attached node mining alone without peer dependencies."),
        new("full-reserve", "Full reserve", "All 897 provisional Work Set positions are occupied."),
        new("small-miner", "Small-miner retention", "One low-hashrate miner has one proof near the reserve floor."),
        new("peer-tip-lead", "Peer tip lead", "A verified peer header arrives before local full-node validation."),
        new("stale-local-node", "Stale local node", "RPC is behind and mining work is unsafe."),
        new("pulse-outage", "Pulse outage", "Work is valid but pulse relay is stale."),
        new("adapter-dropout", "Adapter dropout", "The SV2 adapter disconnects while other mining sources continue."),
        new("peer-fallback", "Peer fallback", "UDP is unavailable and a peer continues over WebSocket and HTTP."),
        new("sibling-merge", "Sibling merge-forward", "Compatible sibling work is merged without branch election."),
        new("state-divergence", "State divergence", "A peer advertises a different candidate state."),
        new("version-mismatch", "Version mismatch", "One connected peer runs an incompatible protocol version."),
        new("regular-boundary", "Regular Bitcoin boundary", "A non-GridPool block advances the snapshot without paid-proof removal."),
        new("gridpool-payment", "GridPool payment", "A GridPool block pays the locked snapshot exactly once."),
        new("shallow-reorg", "Shallow reorganization", "A one-block reorganization rolls back the synthetic boundary."),
        new("transport-interruption", "Dashboard transport interruption", "HTTP remains available while SignalR invalidations are dropped.")
    ];

    public static SimulatorState Create(string id, int seed = 42)
    {
        SimulatorState state = Base(seed);
        state.Scenario = id;
        switch (id)
        {
            case "cold-start":
                state.Node.Ready = false;
                state.Node.MiningSafe = false;
                state.Node.SafetyReason = "Attached Bitcoin node is in initial block download.";
                state.Node.RpcSynced = false;
                state.Node.InitialBlockDownload = true;
                state.Peers.Clear();
                state.Adapters.Clear();
                state.Work.PoolHashrateThs = 0;
                state.Work.ObservationCount = 0;
                state.Reserve.Clear();
                state.LockedPayouts.Clear();
                state.Pulse.Accepted = 0;
                state.Pulse.LastAcceptedUtc = null;
                break;
            case "sovereign-node":
                state.Peers.Clear();
                state.Work.PoolHashrateThs = 800;
                break;
            case "full-reserve":
                FillReserve(state, 897);
                state.Work.ObservationCount = 897;
                break;
            case "small-miner":
                FillReserve(state, 896);
                state.Reserve.Add(new ProofControl
                {
                    Id = Id(seed, "small-miner-proof"),
                    Address = "tb1qsmallminer0000000000000000000000000",
                    Difficulty = state.Work.AdmissionFloorDifficulty * 1.02,
                    FirstSeenUtc = state.VirtualTimeUtc.AddMinutes(-15)
                });
                break;
            case "peer-tip-lead":
                state.Chain.ProvisionalTipHash = Id(seed, "peer-tip");
                state.Node.SafetyReason = "Verified peer header leads local validation; snapshot is provisional only.";
                break;
            case "stale-local-node":
                state.Node.Ready = false;
                state.Node.MiningSafe = false;
                state.Node.RpcSynced = false;
                state.Node.SafetyReason = "Local Bitcoin node is behind a verified peer header.";
                break;
            case "pulse-outage":
                state.Pulse.LastAcceptedUtc = state.VirtualTimeUtc.AddMinutes(-12);
                state.Pulse.LastRelayUtc = state.VirtualTimeUtc.AddMinutes(-12);
                state.Node.OutboundRelayHealthy = false;
                break;
            case "adapter-dropout":
                state.Adapters.First(adapter => adapter.Kind == "sv2").Connected = false;
                break;
            case "peer-fallback":
                PeerControl fallback = state.Peers[0];
                fallback.Udp = false;
                fallback.LatencyMs = 91;
                break;
            case "sibling-merge":
                state.Chain.FamilyMembers = 2;
                state.Chain.SiblingAdmissions = 1;
                state.Chain.UnionAdditions = 17;
                state.Chain.FamilyUnionProofs = state.Reserve.Count + 17;
                break;
            case "state-divergence":
                state.Peers[0].CandidateStateId = Id(seed, "divergent-candidate");
                break;
            case "version-mismatch":
                state.Peers[0].Compatible = false;
                break;
            case "regular-boundary":
                state.Chain.Round++;
                state.Chain.LastRotationUtc = state.VirtualTimeUtc;
                break;
            case "gridpool-payment":
                state.Chain.PaidProofRemovals = state.LockedPayouts.Count;
                state.LockedPayouts.Clear();
                break;
            case "shallow-reorg":
                state.Chain.Reorganizations = 1;
                state.Node.MiningSafe = false;
                state.Node.SafetyReason = "Synthetic one-block reorganization is reconciling.";
                break;
            case "transport-interruption":
                state.Faults.SignalRDrop = true;
                break;
            case "healthy-mesh":
                break;
            default:
                throw new ArgumentException($"Unknown scenario '{id}'.", nameof(id));
        }

        Normalize(state);
        return state;
    }

    public static void Normalize(SimulatorState state)
    {
        state.Speed = Math.Clamp(state.Speed, 0.25, 20);
        state.Work.ReserveLimit = 897;
        state.Work.ObservationCount = Math.Max(0, state.Work.ObservationCount);
        state.Pulse.TargetIntervalSeconds = Math.Max(1, state.Pulse.TargetIntervalSeconds);
        state.Pulse.RelayTtl = Math.Max(0, state.Pulse.RelayTtl);
        state.Reserve = state.Reserve
            .GroupBy(proof => proof.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(proof => proof.Difficulty)
            .ThenBy(proof => proof.Id, StringComparer.Ordinal)
            .Take(897)
            .ToList();
        if (!state.AdvancedOverrides)
        {
            double local = state.Adapters.Where(adapter => adapter.Connected).Sum(adapter => adapter.HashrateThs);
            state.Work.PoolHashrateThs = Math.Max(state.Work.PoolHashrateThs, local);
            state.Node.Ready = state.Node.RpcReachable && state.Node.RpcSynced &&
                               !state.Node.InitialBlockDownload && state.Node.MiningSafe;
        }
    }

    private static SimulatorState Base(int seed)
    {
        SimulatorState state = new() { Seed = seed };
        state.Chain.TipHash = Id(seed, "tip");
        state.Chain.CurrentStateId = Id(seed, "current");
        state.Chain.CandidateStateId = Id(seed, "candidate");
        state.Chain.ActiveSnapshotId = Id(seed, "snapshot");
        state.Chain.SnapshotFamilyId = Id(seed, "family");
        state.Pulse.LastAcceptedUtc = state.VirtualTimeUtc.AddSeconds(-8);
        state.Pulse.LastRelayUtc = state.VirtualTimeUtc.AddSeconds(-8);
        state.Peers =
        [
            Peer("main", "https://main.gridpool.net", 34, true, true, true, state),
            Peer("dallas", "https://dallas.gridpool.net", 47, true, true, true, state),
            Peer("detroit", "https://detroit.gridpool.net", 22, true, true, true, state),
            Peer("evomining", "https://evomining.farted.net", 58, true, true, false, state)
        ];
        state.Adapters =
        [
            Adapter("sv2", "sv2", "Native SV2", 2, 1_100),
            Adapter("datum", "datum", "DATUM", 1, 650),
            Adapter("ckpool", "ckpool", "CKPool", 1, 220),
            Adapter("hydra", "hydrapool", "Hydrapool", 1, 130),
            Adapter("http", "http", "Direct HTTP", 1, 15)
        ];
        FillReserve(state, 240);
        state.LockedPayouts = state.Reserve.Take(40).Select((proof, index) => new PayoutControl
        {
            ProofId = proof.Id,
            Address = proof.Address,
            Position = index + 1,
            ValueSats = 12_500
        }).ToList();
        state.History = Enumerable.Range(0, 24).Select(index => new HistoryControl
        {
            TimestampUtc = state.VirtualTimeUtc.AddHours(index - 23),
            WorkRateThs = 2_250 + Math.Sin(index / 3d) * 220,
            ObservationCount = Math.Max(1, 240 - (23 - index) * 4),
            PulseCount = 90 + index
        }).ToList();
        return state;
    }

    private static void FillReserve(SimulatorState state, int count)
    {
        state.Reserve = Enumerable.Range(0, count).Select(index => new ProofControl
        {
            Id = Id(state.Seed, $"proof-{index}"),
            Address = index % 19 == 0
                ? "tb1qexampleminer000000000000000000000000"
                : $"tb1qsim{index:D4}000000000000000000000000000",
            Difficulty = state.Work.AdmissionFloorDifficulty * (count + 2d) / (index + 1d),
            FirstSeenUtc = state.VirtualTimeUtc.AddMinutes(-index * 3)
        }).ToList();
    }

    private static PeerControl Peer(
        string id, string endpoint, double latency, bool http, bool websocket, bool udp, SimulatorState state) =>
        new()
        {
            Id = id,
            Endpoint = endpoint,
            LatencyMs = latency,
            Http = http,
            WebSocket = websocket,
            Udp = udp,
            CurrentStateId = state.Chain.CurrentStateId,
            CandidateStateId = state.Chain.CandidateStateId
        };

    private static AdapterControl Adapter(
        string id, string kind, string name, int clients, double hashrate) =>
        new()
        {
            Id = id,
            Kind = kind,
            DisplayName = name,
            ClientCount = clients,
            HashrateThs = hashrate,
            AcceptedShares = 120
        };

    public static string Id(int seed, string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{seed}:{value}")));
}
