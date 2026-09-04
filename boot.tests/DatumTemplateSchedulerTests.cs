using boot_portal.Models;
using boot_portal.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace boot.tests;

[TestClass]
public sealed class DatumTemplateSchedulerTests
{
    private const string PayoutAddress = "bc1qchlyrly5nd6a5fvq46lp8vgs9mf52g4njdwmny";
    private const string SupportAddress = "1FhDPLPpw18X4srecguG3MxJYe4a1JsZnd";
    private const string Parent = "00000000000000000000000000000000000000000000000000000000000000aa";
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [TestMethod]
    public void FivePercentVectorIsStableAndJobBound()
    {
        var policy = new DatumListenerPolicy
        {
            PolicyId = "public-datum",
            SupportTemplateBasisPoints = 500,
            SupportAddress = SupportAddress
        };

        DatumTemplateDecision miner = DatumTemplateScheduler.Decide(
            policy, Key, "fingerprint-v1", "client-a", PayoutAddress, Parent, 0);
        DatumTemplateDecision support = DatumTemplateScheduler.Decide(
            policy, Key, "fingerprint-v1", "client-a", PayoutAddress, Parent, 11);

        Assert.IsFalse(miner.UsesSupportAddress);
        Assert.AreEqual(PayoutAddress, miner.SlotZeroAddress);
        Assert.IsTrue(support.UsesSupportAddress);
        Assert.AreEqual(SupportAddress, support.SlotZeroAddress);
        Assert.IsFalse(DatumTemplateScheduler.Decide(
            policy, Key, "fingerprint-v1", "client-b", PayoutAddress, Parent, 11).UsesSupportAddress);
    }

    [DataTestMethod]
    [DataRow(500, 400, 600)]
    [DataRow(5_000, 4_800, 5_200)]
    public void DistributionTracksConfiguredBasisPoints(int basisPoints, int minimum, int maximum)
    {
        var policy = new DatumListenerPolicy
        {
            PolicyId = $"policy-{basisPoints}",
            SupportTemplateBasisPoints = basisPoints,
            SupportAddress = SupportAddress
        };

        int supportCount = Enumerable.Range(0, 10_000)
            .Count(sequence => DatumTemplateScheduler.Decide(
                policy, Key, "fingerprint-v1", "client-a", PayoutAddress, Parent, sequence).UsesSupportAddress);

        Assert.IsTrue(supportCount >= minimum && supportCount <= maximum, $"Observed {supportCount} support decisions.");
    }

    [TestMethod]
    public void FeePolicyRequiresAFullStrengthKey()
    {
        var policy = new DatumListenerPolicy
        {
            PolicyId = "public-datum",
            SupportTemplateBasisPoints = 500,
            SupportAddress = SupportAddress
        };

        Assert.ThrowsException<InvalidOperationException>(() => DatumTemplateScheduler.Decide(
            policy, new byte[31], "fingerprint-v1", "client-a", PayoutAddress, Parent, 0));
    }
}
