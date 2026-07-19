using boot_portal.HostedServices;

namespace boot.tests;

[TestClass]
public sealed class BitcoinZmqNotificationTests
{
    [TestMethod]
    public void DuplicateHashAndRawBlockNotificationsAreAcceptedOnlyOnce()
    {
        var notifications = new RecentBitcoinBlockNotifications(TimeSpan.FromSeconds(30));
        DateTime receivedUtc = DateTime.UtcNow;

        Assert.IsTrue(notifications.TryAccept("block-a", receivedUtc));
        Assert.IsFalse(notifications.TryAccept("block-a", receivedUtc.AddMilliseconds(10)));
    }

    [TestMethod]
    public void InterleavedDuplicateNotificationsDoNotReplayEarlierBlocks()
    {
        var notifications = new RecentBitcoinBlockNotifications(TimeSpan.FromSeconds(30));
        DateTime receivedUtc = DateTime.UtcNow;

        Assert.IsTrue(notifications.TryAccept("block-a", receivedUtc));
        Assert.IsTrue(notifications.TryAccept("block-b", receivedUtc.AddMilliseconds(1)));
        Assert.IsFalse(notifications.TryAccept("block-a", receivedUtc.AddMilliseconds(2)));
        Assert.IsFalse(notifications.TryAccept("block-b", receivedUtc.AddMilliseconds(3)));
    }

    [TestMethod]
    public void HashCanBeAcceptedAgainAfterDuplicateWindow()
    {
        var notifications = new RecentBitcoinBlockNotifications(TimeSpan.FromSeconds(30));
        DateTime receivedUtc = DateTime.UtcNow;

        Assert.IsTrue(notifications.TryAccept("block-a", receivedUtc));
        Assert.IsTrue(notifications.TryAccept("block-a", receivedUtc.AddSeconds(31)));
    }
}
