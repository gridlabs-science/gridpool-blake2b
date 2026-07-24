using boot_portal.HostedServices;
using boot_portal.Utils;

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

    [TestMethod]
    public void RawBlockParserReadsBip34CoinbaseHeight()
    {
        byte[] block = new byte[80 + 1 + 4 + 1 + 36 + 1 + 4 + 4 + 1 + 4];
        int offset = 80;
        block[offset++] = 1; // transaction count
        offset += 4; // transaction version
        block[offset++] = 1; // input count
        offset += 36; // null coinbase prevout
        block[offset++] = 4; // scriptSig length
        block[offset++] = 3; // push the three-byte height
        block[offset++] = 0xd3;
        block[offset++] = 0xa3;
        block[offset++] = 0x0e; // 959443, little-endian script number
        offset += 4; // sequence
        block[offset++] = 0; // output count
        offset += 4; // locktime

        Assert.IsTrue(BitcoinBlockParser.TryReadCoinbaseHeight(block, out long height));
        Assert.AreEqual(959443L, height);
    }

    [TestMethod]
    public void RawBlockParserRejectsMissingCoinbaseHeight()
    {
        Assert.IsFalse(BitcoinBlockParser.TryReadCoinbaseHeight(new byte[81], out _));
    }
}
