using boot_portal.Models;
using boot_portal.Services;

namespace boot.tests;

[TestClass]
public sealed class DashboardVisualizationJournalTests
{
    [TestMethod]
    public void JournalIsBoundedAndReportsCursorGaps()
    {
        var journal = new DashboardVisualizationJournalService();
        DateTime now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

        for (int index = 0; index < DashboardVisualizationJournalService.MaximumEvents + 12; index++)
        {
            journal.Append(new DashboardDiagramEventDto
            {
                TimestampUtc = now,
                Kind = DashboardDiagramEventKinds.PulseAccepted,
                SourceKind = "local"
            });
        }

        DashboardDiagramEventPageDto page = journal.Read(1, 256, redacted: true, now);

        Assert.IsTrue(page.Gap);
        Assert.AreEqual(13, page.OldestSequence);
        Assert.AreEqual(DashboardVisualizationJournalService.MaximumEvents + 12, page.LatestSequence);
        Assert.AreEqual(256, page.Events.Count);
        Assert.IsTrue(page.HasMore);
    }

    [TestMethod]
    public void PublicProofAdmissionPreservesConsensusEvidenceButRedactsTransportIdentity()
    {
        var journal = new DashboardVisualizationJournalService();
        DateTime now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        journal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = now,
            Kind = DashboardDiagramEventKinds.ProofAdmitted,
            SourceKind = "peer",
            SourceId = "private-peer",
            SourceVisualId = journal.VisualId("peer", "private-peer"),
            Transport = "websocket",
            ProofId = "proof-1",
            Address = "tb1qprivate",
            Difficulty = 1234,
            BlockQuality = true,
            Rank = 12,
            DisplacedProofId = "proof-old",
            LockedProofIds = ["proof-1"]
        });

        DashboardDiagramEventDto publicEvent = journal.Read(0, 10, redacted: true, now).Events.Single();
        DashboardDiagramEventDto operatorEvent = journal.Read(0, 10, redacted: false, now).Events.Single();

        Assert.IsFalse(string.IsNullOrWhiteSpace(publicEvent.VisualId));
        Assert.AreEqual(operatorEvent.SourceVisualId, publicEvent.SourceVisualId);
        Assert.AreEqual(12, publicEvent.Rank);
        Assert.AreEqual(string.Empty, publicEvent.SourceId);
        Assert.AreEqual(string.Empty, publicEvent.Transport);
        Assert.AreEqual("proof-1", publicEvent.ProofId);
        Assert.AreEqual("tb1qprivate", publicEvent.Address);
        Assert.AreEqual(1234d, publicEvent.Difficulty);
        Assert.AreEqual("proof-old", publicEvent.DisplacedProofId);
        Assert.AreEqual(0, publicEvent.LockedProofIds.Count);
        Assert.IsTrue(publicEvent.BlockQuality);
        Assert.AreEqual("private-peer", operatorEvent.SourceId);
        Assert.AreEqual("proof-1", operatorEvent.ProofId);
        Assert.AreEqual("tb1qprivate", operatorEvent.Address);
    }

    [TestMethod]
    public void PublicBoundaryPreservesSharedSnapshotMembership()
    {
        var journal = new DashboardVisualizationJournalService();
        DateTime now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        journal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = now,
            Kind = DashboardDiagramEventKinds.BoundaryValidated,
            SourceKind = "bitcoin",
            BlockHash = "00000000000000000001",
            LockedProofIds = ["proof-1", "proof-2"]
        });

        DashboardDiagramEventDto publicEvent = journal.Read(0, 10, redacted: true, now).Events.Single();

        CollectionAssert.AreEqual(new[] { "proof-1", "proof-2" }, publicEvent.LockedProofIds);
    }

    [TestMethod]
    public void PublicPulseStillRedactsLocalProofEvidence()
    {
        var journal = new DashboardVisualizationJournalService();
        DateTime now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        journal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = now,
            Kind = DashboardDiagramEventKinds.PulseAccepted,
            SourceKind = "local",
            SourceId = "worker-1",
            ProofId = "private-pulse-proof",
            Address = "tb1qlocal",
            Difficulty = 42
        });

        DashboardDiagramEventDto publicEvent = journal.Read(0, 10, redacted: true, now).Events.Single();

        Assert.AreEqual(string.Empty, publicEvent.SourceId);
        Assert.AreEqual(string.Empty, publicEvent.ProofId);
        Assert.AreEqual(string.Empty, publicEvent.Address);
        Assert.IsNull(publicEvent.Difficulty);
    }

    [TestMethod]
    public void SlotZeroRequiresExplicitVerifiedObservationAndResetClearsIt()
    {
        var journal = new DashboardVisualizationJournalService();
        Assert.IsFalse(journal.SlotZero().Verified);

        journal.ObserveVerifiedLocalSlotZero(
            "tb1qverified",
            "proof-verified",
            new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(journal.SlotZero().Verified);
        Assert.AreEqual("tb1qverified", journal.SlotZero().Address);

        journal.Reset();

        Assert.IsFalse(journal.SlotZero().Verified);
        Assert.AreEqual(0, journal.LatestSequence);
    }

    [TestMethod]
    public void CursorAheadOfLatestSequenceReportsResetGap()
    {
        var journal = new DashboardVisualizationJournalService();
        DateTime now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        journal.Append(new DashboardDiagramEventDto
        {
            TimestampUtc = now,
            Kind = DashboardDiagramEventKinds.PulseAccepted,
            SourceKind = "local"
        });

        DashboardDiagramEventPageDto page = journal.Read(42, 10, redacted: true, now);

        Assert.IsTrue(page.Gap);
        Assert.AreEqual(1, page.LatestSequence);
        Assert.AreEqual(0, page.Events.Count);
    }

    [TestMethod]
    public void RejectionStormsCoalesceAndPublicViewDropsExactReason()
    {
        var journal = new DashboardVisualizationJournalService();
        DateTime now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        for (int index = 0; index < 4; index++)
        {
            journal.Append(new DashboardDiagramEventDto
            {
                TimestampUtc = now.AddMilliseconds(index * 100),
                Kind = DashboardDiagramEventKinds.ProofRejected,
                SourceKind = "miner",
                SourceId = "garage",
                SourceVisualId = journal.VisualId("miner", "garage"),
                Category = "below-floor",
                Reason = "exact private diagnostic",
                Count = 1
            });
        }

        DashboardDiagramEventDto publicEvent = journal.Read(0, 10, true, now.AddSeconds(1)).Events.Single();
        DashboardDiagramEventDto operatorEvent = journal.Read(0, 10, false, now.AddSeconds(1)).Events.Single();
        Assert.AreEqual(4, publicEvent.Count);
        Assert.AreEqual("below-floor", publicEvent.Category);
        Assert.AreEqual(string.Empty, publicEvent.Reason);
        Assert.AreEqual("exact private diagnostic", operatorEvent.Reason);
    }

    [TestMethod]
    public void InitialBitcoinPeerObservationSeedsWithoutSyntheticConnectionBurst()
    {
        var journal = new DashboardVisualizationJournalService();
        DateTime now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        var status = new BootNetworkStatusDto();
        status.BitcoinNotification.Network.Peers.Add(new BootBitcoinPeerHealthDto { Id = 7 });

        journal.ObserveSystemHealth(status, now);
        Assert.AreEqual(0, journal.Read(0, 10, false, now).Events.Count);

        status.BitcoinNotification.Network.Peers.Add(new BootBitcoinPeerHealthDto { Id = 8 });
        journal.ObserveSystemHealth(status, now.AddSeconds(15));

        DashboardDiagramEventDto connected = journal.Read(0, 10, false, now.AddSeconds(15)).Events.Single();
        Assert.AreEqual(DashboardDiagramEventKinds.BitcoinPeerConnection, connected.Kind);
        Assert.IsTrue(connected.Connected);
    }
}
