using boot_portal.Services;

namespace boot.tests;

[TestClass]
public sealed class PeerLoopHealthTests
{
    [TestMethod]
    public void PeerPollBecomesStaleAfterConfiguredThreshold()
    {
        var health = new BootPeerLoopHealth();

        Assert.IsFalse(health.IsPeerPollStale(health.StartedUtc.AddSeconds(599), 600));
        Assert.IsTrue(health.IsPeerPollStale(health.StartedUtc.AddSeconds(601), 600));

        health.RecordPeerPollCompleted();
        Assert.IsFalse(health.IsPeerPollStale(DateTime.UtcNow, 600));
    }

    [TestMethod]
    public void OutboundRelayHealthIsIndependentFromPeerPoll()
    {
        var health = new BootPeerLoopHealth();
        health.RecordPeerPollCompleted();

        Assert.IsTrue(health.IsOutboundRelayStale(health.StartedUtc.AddSeconds(301), 300));
        Assert.IsFalse(health.IsPeerPollStale(health.StartedUtc.AddSeconds(301), 600));
    }

    [TestMethod]
    public void RecordsTransportAndDatumLifecycleTimestampsSeparately()
    {
        var health = new BootPeerLoopHealth();
        DateTime datumShareUtc = DateTime.UtcNow.AddSeconds(-3);
        DateTime coinbaserUtc = DateTime.UtcNow.AddSeconds(-2);
        DateTime closedUtc = DateTime.UtcNow.AddSeconds(-1);

        health.RecordShareQueued();
        health.RecordUdpShareRelay();
        health.RecordWebSocketShareRelay();
        health.RecordHttpShareRelay();
        health.RecordValidLocalDatumShare(datumShareUtc);
        health.RecordSuccessfulDatumCoinbaserResponse(coinbaserUtc);
        health.RecordDatumSessionClosed("test close", closedUtc);

        Assert.IsNotNull(health.LastShareRelayQueuedUtc);
        Assert.IsNotNull(health.LastUdpShareRelayUtc);
        Assert.IsNotNull(health.LastWebSocketShareRelayUtc);
        Assert.IsNotNull(health.LastHttpShareRelayUtc);
        Assert.AreEqual(datumShareUtc, health.LastValidLocalDatumShareUtc);
        Assert.AreEqual(coinbaserUtc, health.LastSuccessfulDatumCoinbaserResponseUtc);
        Assert.AreEqual(closedUtc, health.LastDatumSessionClosedUtc);
        Assert.AreEqual("test close", health.LastDatumSessionCloseReason);
    }

    [TestMethod]
    public async Task BlockedWebSocketSendLockDoesNotBlockPeerPollHealthAsync()
    {
        using var sendLock = new SemaphoreSlim(0, 1);
        var health = new BootPeerLoopHealth();

        Task<bool> blockedSend = BootPeerSessionManager.WaitForSendLockAsync(
            sendLock,
            TimeSpan.FromMilliseconds(50));
        health.RecordPeerPollCompleted();

        Assert.IsFalse(await blockedSend);
        Assert.IsNotNull(health.LastPeerPollCompletedUtc);
    }
}
