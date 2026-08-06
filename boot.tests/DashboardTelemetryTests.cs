using boot_portal.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace boot.tests;

[TestClass]
public sealed class DashboardTelemetryTests
{
    [TestMethod]
    public void CompleteWindowUsesTheBoundaryOrderStatistic()
    {
        DateTime now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
        using var temporary = new TemporaryTelemetryPath();
        var service = new DashboardTelemetryService(
            NullLogger<DashboardTelemetryService>.Instance,
            temporary.Path,
            now.AddHours(-25));
        service.ObserveAdmissionFloor(1d, now.AddHours(-25));
        for (int index = 0; index < 100; index++)
        {
            service.ObserveWorkProof(
                $"share-{index}",
                "peer",
                1_000d + index,
                1d,
                now.AddHours(-23).AddMinutes(index));
        }

        var estimate = service.GetEstimate("24h", now);
        double expected = 100d * 1_000d * 4_294_967_296d / 86_400d / 1_000_000_000_000d;

        Assert.IsTrue(estimate.CompleteWindow);
        Assert.IsFalse(estimate.Warmup);
        Assert.AreEqual(100, estimate.RetainedOrderStatisticCount);
        Assert.AreEqual(1_000d, estimate.OrderStatisticDifficulty);
        Assert.AreEqual(expected, estimate.EstimateThs!.Value, expected * 1e-12);
        Assert.AreEqual(10d, estimate.RelativeStandardErrorPercent!.Value, 1e-12);
        Assert.AreEqual("medium", estimate.Confidence);
    }

    [TestMethod]
    public void MaximumAdmissionFloorMakesTheWindowSampleComplete()
    {
        DateTime now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
        using var temporary = new TemporaryTelemetryPath();
        var service = new DashboardTelemetryService(
            NullLogger<DashboardTelemetryService>.Instance,
            temporary.Path,
            now.AddHours(-25));
        service.ObserveAdmissionFloor(1d, now.AddHours(-25));
        service.ObserveWorkProof("below-later-floor", "peer", 400d, 1d, now.AddHours(-20));
        service.ObserveAdmissionFloor(500d, now.AddHours(-12));
        service.ObserveWorkProof("above-floor-1", "peer", 600d, 500d, now.AddHours(-10));
        service.ObserveWorkProof("above-floor-2", "peer", 700d, 500d, now.AddHours(-9));

        var estimate = service.GetEstimate("24h", now);

        Assert.AreEqual(500d, estimate.EffectiveAdmissionFloorDifficulty);
        Assert.AreEqual(2, estimate.ObservationCount);
        Assert.AreEqual(600d, estimate.OrderStatisticDifficulty);
    }

    [TestMethod]
    public void DuplicateProofsAreCountedOnceAndTelemetrySurvivesRestart()
    {
        DateTime now = DateTime.UtcNow;
        using var temporary = new TemporaryTelemetryPath();
        var service = new DashboardTelemetryService(
            NullLogger<DashboardTelemetryService>.Instance,
            temporary.Path,
            now.AddHours(-25));
        service.ObserveAdmissionFloor(1d, now.AddHours(-25));
        service.ObserveWorkProof("same-share", "peer", 100d, 1d, now.AddHours(-1));
        service.ObserveWorkProof("same-share", "http", 100d, 1d, now.AddMinutes(-30));
        service.FlushForTests();

        var restored = new DashboardTelemetryService(
            NullLogger<DashboardTelemetryService>.Instance,
            temporary.Path,
            null);
        var estimate = restored.GetEstimate("24h", now);

        Assert.AreEqual(1, estimate.ObservationCount);
        Assert.AreEqual(1, estimate.RetainedOrderStatisticCount);
    }

    [TestMethod]
    public void PartialWindowIsReportedAsWarmup()
    {
        DateTime now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
        using var temporary = new TemporaryTelemetryPath();
        var service = new DashboardTelemetryService(
            NullLogger<DashboardTelemetryService>.Instance,
            temporary.Path,
            now.AddHours(-1));
        service.ObserveAdmissionFloor(1d, now.AddHours(-1));
        service.ObserveWorkProof("share", "peer", 100d, 1d, now.AddMinutes(-30));

        var estimate = service.GetEstimate("24h", now);

        Assert.IsTrue(estimate.Warmup);
        Assert.IsFalse(estimate.CompleteWindow);
        Assert.AreEqual("collecting", estimate.Confidence);
    }

    [TestMethod]
    public void DiagramHistoryIncludesValidatedLocalWorkAndPulseWithoutPublicWorkerIdentity()
    {
        DateTime now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        using var temporary = new TemporaryTelemetryPath();
        var service = new DashboardTelemetryService(
            NullLogger<DashboardTelemetryService>.Instance,
            temporary.Path,
            now.AddDays(-2));
        service.ObserveWorkProof(
            "work-1", "sv2", 12_000, 10_000, now.AddHours(-2),
            "tb1qslotzero", "garage", "miner", true, false);
        service.ObservePulse(
            "pulse-1", "sv2", now.AddHours(-1),
            "tb1qslotzero", "garage", "miner", 8_000, false);

        var publicHistory = service.GetDiagramHistory("tb1qslotzero", "24h", 256, false, now);
        var operatorHistory = service.GetDiagramHistory("tb1qslotzero", "24h", 256, true, now);

        Assert.AreEqual(2, publicHistory.Proofs.Count);
        Assert.AreEqual("work-1", publicHistory.Proofs[0].ProofId);
        Assert.AreEqual(string.Empty, publicHistory.Proofs[0].Source);
        Assert.AreEqual(string.Empty, publicHistory.Proofs[0].Username);
        Assert.AreEqual("sv2", operatorHistory.Proofs[0].Source);
        Assert.AreEqual("garage", operatorHistory.Proofs[0].Username);
        Assert.IsFalse(publicHistory.Proofs.Single(item => item.ProofId == "pulse-1").EnteredWorkSet);
    }

    [TestMethod]
    public void SchemaOneTelemetryMigratesWithoutInventingAddressAttribution()
    {
        DateTime now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        using var temporary = new TemporaryTelemetryPath();
        File.WriteAllText(temporary.Path, $$"""
            {"SchemaVersion":1,"TrackingStartedUtc":"{{now.AddDays(-2):O}}","WorkProofs":[{"ShareId":"legacy","Source":"peer","Difficulty":1000,"AdmissionFloorDifficulty":1,"ReceivedUtc":"{{now.AddHours(-1):O}}"}],"AdmissionFloors":[],"Pulses":[]}
            """);

        var service = new DashboardTelemetryService(
            NullLogger<DashboardTelemetryService>.Instance,
            temporary.Path,
            null);

        Assert.AreEqual(1, service.GetEstimate("24h", now).ObservationCount);
        Assert.AreEqual(0, service.GetDiagramHistory("tb1qslotzero", "24h", 256, false, now).Proofs.Count);
        service.FlushForTests();
        using JsonDocument migrated = JsonDocument.Parse(File.ReadAllText(temporary.Path));
        Assert.AreEqual(2, migrated.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    private sealed class TemporaryTelemetryPath : IDisposable
    {
        public TemporaryTelemetryPath()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"gridpool-dashboard-{Guid.NewGuid():N}.json");
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
            File.Delete($"{Path}.tmp");
        }
    }
}
