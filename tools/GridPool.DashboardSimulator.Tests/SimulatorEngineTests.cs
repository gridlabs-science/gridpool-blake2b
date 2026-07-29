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
        Assert.AreEqual(before.Reserve.Count - paid.Count, afterFirst.Reserve.Count);
        Assert.IsTrue(afterSecond.Chain.PaidProofRemovals >= afterFirst.Chain.PaidProofRemovals);
        Assert.IsFalse(afterSecond.Reserve.Any(proof => paid.Contains(proof.Id)));
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
        Assert.AreEqual(240, summary.WorkRate.RetainedOrderStatisticCount);
        Assert.AreEqual(
            100 / Math.Sqrt(240),
            summary.WorkRate.RelativeStandardErrorPercent!.Value,
            0.0001);
        Assert.AreEqual("medium", summary.WorkRate.Confidence);
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
