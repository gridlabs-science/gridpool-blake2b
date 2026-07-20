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
