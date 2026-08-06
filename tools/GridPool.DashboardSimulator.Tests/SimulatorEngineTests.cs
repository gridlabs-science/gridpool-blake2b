using System.Net;
using GridPool.DashboardSimulator;
using boot_portal.Models;

namespace GridPool.DashboardSimulator.Tests;

[TestClass]
public sealed class SimulatorEngineTests
{
    [TestMethod]
    public void ScenarioGenerationIsDeterministic()
    {
        SimulatorState first = SimulatorScenarios.Create("healthy-mesh", 91);
        SimulatorState second = SimulatorScenarios.Create("healthy-mesh", 91);

        Assert.AreEqual(first.Chain.CurrentStateId, second.Chain.CurrentStateId);
        Assert.AreEqual(first.Reserve[100].Id, second.Reserve[100].Id);
        Assert.AreEqual(first.Reserve[100].Difficulty, second.Reserve[100].Difficulty);
    }

    [TestMethod]
    public void CoherentModeBoundsReserveAndPoolHashrate()
    {
        SimulatorState state = SimulatorScenarios.Create("full-reserve");
        state.Reserve.AddRange(state.Reserve.Take(10));
        state.Work.PoolHashrateThs = 1;

        SimulatorScenarios.Normalize(state);

        Assert.AreEqual(897, state.Reserve.Count);
        Assert.IsTrue(state.Work.PoolHashrateThs >=
            state.Adapters.Where(adapter => adapter.Connected).Sum(adapter => adapter.HashrateThs));
        Assert.AreEqual(897, state.Work.ReserveLimit);
    }

    [TestMethod]
    public async Task GridPoolPaymentRemovesPaidProofsExactlyOnce()
    {
        SimulatorEngine engine = new(new RecordingBroadcaster());
        SimulatorState before = engine.Read();
        HashSet<string> paid = before.LockedPayouts.Select(item => item.ProofId).ToHashSet();

        await engine.ApplyAsync(new SimulatorAction { Action = "snapshot.gridpool-paid" });
        SimulatorState afterFirst = engine.Read();
        await engine.ApplyAsync(new SimulatorAction { Action = "snapshot.gridpool-paid" });
        SimulatorState afterSecond = engine.Read();

        Assert.IsFalse(afterFirst.Reserve.Any(proof => paid.Contains(proof.Id)));
        Assert.AreEqual(897, afterFirst.Reserve.Count);
        Assert.IsTrue(afterSecond.Chain.PaidProofRemovals >= afterFirst.Chain.PaidProofRemovals);
        Assert.IsFalse(afterSecond.Reserve.Any(proof => paid.Contains(proof.Id)));
        Assert.AreEqual(897, afterSecond.Reserve.Count);
    }

    [TestMethod]
    public async Task RegularBoundaryDoesNotRemoveReserveWork()
    {
        SimulatorEngine engine = new(new RecordingBroadcaster());
        List<string> before = engine.Read().Reserve.Select(proof => proof.Id).ToList();

        await engine.ApplyAsync(new SimulatorAction { Action = "snapshot.regular" });

        CollectionAssert.AreEqual(before, engine.Read().Reserve.Select(proof => proof.Id).ToList());
    }

    [TestMethod]
    public async Task JournalProducingSnapshotActionsBroadcastDiagramInvalidations()
    {
        var broadcaster = new RecordingBroadcaster();
        SimulatorEngine engine = new(broadcaster);

        foreach (string action in new[]
        {
            "snapshot.regular",
            "snapshot.gridpool-paid",
            "snapshot.sibling-merge"
        })
        {
            await engine.ApplyAsync(new SimulatorAction { Action = action, Count = 1 });
            Assert.IsTrue(
                broadcaster.Changes[^1].Topics.Contains("diagram"),
                $"Action '{action}' did not invalidate the diagram journal.");
        }
    }

    [TestMethod]
    public async Task AutomaticPulseBroadcastsDiagramInvalidation()
    {
        var broadcaster = new RecordingBroadcaster();
        SimulatorEngine engine = new(broadcaster);
        SimulatorState state = engine.Read();
        state.Playing = true;
        state.Pulse.Enabled = true;
        state.Pulse.SecondsUntilNext = 0.1;
        await engine.ReplaceAsync(state);
        broadcaster.Changes.Clear();

        await engine.AdvanceAsync(1);

        Assert.IsTrue(broadcaster.Changes.Any(change => change.Topics.Contains("diagram")));
        Assert.IsTrue(engine.DiagramEvents(0, 10, true).Events.Any(
            item => item.Kind == DashboardDiagramEventKinds.PulseAccepted));
    }

    [TestMethod]
    public async Task Top300ProofIsAlsoRetainedInTop897()
    {
        SimulatorEngine engine = new(new RecordingBroadcaster());

        await engine.ApplyAsync(new SimulatorAction
        {
            Action = "proof.top300",
            Address = "tb1qtest"
        });

        SimulatorState state = engine.Read();
        int position = state.Reserve.FindIndex(proof => proof.Address == "tb1qtest") + 1;
        Assert.IsTrue(position is > 0 and <= 300);
        Assert.IsTrue(state.Reserve.Count <= 897);
    }

    [TestMethod]
    public async Task TimelineStepIsDeterministic()
    {
        TimelineDocument timeline = new()
        {
            Seed = 7,
            InitialScenario = "healthy-mesh",
            Events =
            [
                new TimelineEvent { At = "5s", Action = "peer.disconnect", Peer = "dallas" },
                new TimelineEvent { At = "8s", Action = "chain.peer-header" }
            ]
        };
        SimulatorEngine first = new(new RecordingBroadcaster());
        SimulatorEngine second = new(new RecordingBroadcaster());
        await first.SetTimelineAsync(timeline);
        await second.SetTimelineAsync(timeline);

        await first.StepTimelineAsync();
        await first.StepTimelineAsync();
        await second.StepTimelineAsync();
        await second.StepTimelineAsync();

        Assert.AreEqual(first.Read().Chain.ProvisionalTipHash, second.Read().Chain.ProvisionalTipHash);
        Assert.IsFalse(first.Read().Peers.Single(peer => peer.Id == "dallas").Connected);
    }

    [TestMethod]
    public void WorkRateProjectionUsesConfiguredHashrateAndUncertainty()
    {
        SimulatorEngine engine = new(new RecordingBroadcaster());

        DashboardSummaryDto summary = engine.Summary("24h");

        Assert.AreEqual(2_400d, summary.WorkRate.EstimateThs);
        Assert.AreEqual(897, summary.WorkRate.RetainedOrderStatisticCount);
        Assert.AreEqual(
            100 / Math.Sqrt(897),
            summary.WorkRate.RelativeStandardErrorPercent!.Value,
            0.0001);
        Assert.AreEqual("high", summary.WorkRate.Confidence);
    }

    [TestMethod]
    public void EveryBuiltInScenarioHasAnExactFullWorkSet()
    {
        foreach (ScenarioDefinition scenario in SimulatorScenarios.All)
        {
            Assert.AreEqual(
                897,
                SimulatorScenarios.Create(scenario.Id).Reserve.Count,
                $"Scenario '{scenario.Id}' did not have a full Work Set.");
        }
    }

    [TestMethod]
    public async Task PublicDiagramPublishesConsensusEvidenceWithoutPrivateEndpointsOrMinerIdentity()
    {
        SimulatorEngine engine = new(new RecordingBroadcaster());
        SimulatorState state = engine.Read();
        state.Peers.Add(new PeerControl
        {
            Id = "private-node-id",
            Endpoint = "https://192.168.1.44:5000",
            Connected = true
        });
        await engine.ReplaceAsync(state);

        DashboardDiagramDto diagram = engine.Diagram(operatorDetails: false);
        DashboardDiagramPeerDto dallas = diagram.Peers.Single(peer => peer.NodeId == "dallas");
        DashboardDiagramPeerDto privatePeer = diagram.Peers.Single(peer => peer.NodeId == "private-node-id");

        Assert.AreEqual("Dallas", dallas.DisplayName);
        Assert.AreEqual(string.Empty, dallas.Endpoint);
        Assert.AreEqual("private-node-id", privatePeer.DisplayName);
        Assert.AreEqual(string.Empty, privatePeer.Endpoint);
        Assert.IsFalse(privatePeer.DisplayName.Contains("192.168", StringComparison.Ordinal));
        Assert.IsTrue(diagram.WorkSet.All(proof =>
            !string.IsNullOrWhiteSpace(proof.ProofId) &&
            !string.IsNullOrWhiteSpace(proof.Address) &&
            proof.Difficulty.HasValue &&
            proof.FirstSeenUtc.HasValue));
        Assert.IsTrue(diagram.Miners.All(miner =>
            string.IsNullOrWhiteSpace(miner.Username) &&
            string.IsNullOrWhiteSpace(miner.Address) &&
            miner.HashrateThs.HasValue));
        Assert.IsTrue(diagram.WorkGenerator.HashrateThs.HasValue);
        Assert.IsTrue(diagram.Grid.HashrateThs.HasValue);
        Assert.IsTrue(diagram.Bitcoin.NetworkDifficulty.HasValue);
    }

    [TestMethod]
    public void InteractiveScenariosExposeAtLeastOneRoutableMiner()
    {
        foreach (ScenarioDefinition scenario in SimulatorScenarios.All.Where(
            item => item.Id != "cold-start"))
        {
            Assert.IsTrue(
                SimulatorScenarios.Create(scenario.Id).Adapters.SelectMany(adapter => adapter.Miners).Any(),
                $"Scenario '{scenario.Id}' did not expose a miner for animation controls.");
        }
    }

    [TestMethod]
    public async Task LivingMinuteProducesExactHonestDiagramEvents()
    {
        SimulatorEngine engine = new(new RecordingBroadcaster());
        await engine.SetTimelineAsync(SimulatorScenarios.LivingMinuteTimeline(77));

        await engine.StepTimelineAsync();
        DashboardDiagramEventDto firstProof = engine.DiagramEvents(0, 20, true)
            .Events.Single(item => item.Kind == DashboardDiagramEventKinds.ProofAdmitted);
        Assert.AreEqual(620, firstProof.Rank);
        Assert.AreEqual("dallas", firstProof.SourceId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstProof.SourceVisualId));

        await engine.StepTimelineAsync();
        DashboardDiagramEventDto peerPulse = engine.DiagramEvents(firstProof.Sequence, 20, true)
            .Events.Single(item => item.Kind == DashboardDiagramEventKinds.PulseAccepted);
        Assert.AreEqual("peer", peerPulse.SourceKind);
        Assert.AreEqual("detroit", peerPulse.SourceId);

        await engine.StepTimelineAsync();
        DashboardDiagramEventDto miner = engine.DiagramEvents(peerPulse.Sequence, 20, true)
            .Events.Single(item => item.Kind == DashboardDiagramEventKinds.LocalMinerActivity);
        Assert.AreEqual("miner-2", miner.SourceId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(miner.SourceVisualId));

        await engine.StepTimelineAsync();
        DashboardDiagramEventDto topProof = engine.DiagramEvents(miner.Sequence, 20, true)
            .Events.Single(item => item.Kind == DashboardDiagramEventKinds.ProofAdmitted);
        Assert.AreEqual(120, topProof.Rank);

        await engine.StepTimelineAsync();
        DashboardDiagramEventDto localProof = engine.DiagramEvents(topProof.Sequence, 20, true)
            .Events.Single(item => item.Kind == DashboardDiagramEventKinds.ProofAdmitted);
        Assert.AreEqual("miner", localProof.SourceKind);
        Assert.AreEqual("miner-1", localProof.SourceId);
        Assert.AreEqual(250, localProof.Rank);

        for (int index = 0; index < 5; index++)
        {
            await engine.StepTimelineAsync();
        }
        DashboardDiagramEventDto boundary = engine.DiagramEvents(localProof.Sequence, 50, true)
            .Events.Single(item => item.Kind == DashboardDiagramEventKinds.BoundaryValidated);
        Assert.AreEqual(300, boundary.LockedProofIds.Count);
        Assert.IsTrue(boundary.LockedProofIds.Contains(topProof.ProofId));

        await engine.StepTimelineAsync();
        DashboardDiagramEventDto blockProof = engine.DiagramEvents(boundary.Sequence, 20, true)
            .Events.Single(item => item.Kind == DashboardDiagramEventKinds.ProofAdmitted);
        Assert.IsTrue(blockProof.BlockQuality);
        Assert.AreEqual("miner-2", blockProof.SourceId);
        Assert.AreEqual(1, blockProof.Rank);
    }

    [TestMethod]
    public void LoopbackGuardRejectsLanAddresses()
    {
        Assert.IsTrue(SimulatorAccess.IsLoopback(IPAddress.Loopback));
        Assert.IsTrue(SimulatorAccess.IsLoopback(IPAddress.IPv6Loopback));
        Assert.IsFalse(SimulatorAccess.IsLoopback(IPAddress.Parse("192.168.1.50")));
        Assert.IsFalse(SimulatorAccess.IsLoopback(null));
    }

    private sealed class RecordingBroadcaster : ISimulatorBroadcaster
    {
        public List<DashboardChangedDto> Changes { get; } = [];
        public Task BroadcastAsync(DashboardChangedDto change)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }
}
