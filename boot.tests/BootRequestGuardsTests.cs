using boot_portal.Utils;
using Microsoft.AspNetCore.Http;

namespace boot.tests;

[TestClass]
public sealed class BootRequestGuardsTests
{
    private static readonly string ValidHeaderHex = new('0', 160);
    private static readonly string ValidCoinbaseHex = new('a', 200);
    private static readonly string ValidMerkleHash = new('b', 64);

    [TestMethod]
    public void ValidateShareRequestAcceptsWellFormedPayload()
    {
        var config = new PoolConfig();
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 1024;

        BootRequestValidationFailure? failure = BootRequestGuards.ValidateShareRequest(
            config,
            context.Request,
            "bc1qtestmineraddress000000000000000000000000000",
            ValidHeaderHex,
            ValidCoinbaseHex,
            [ValidMerkleHash, ValidMerkleHash]);

        Assert.IsFalse(failure.HasValue);
    }

    [TestMethod]
    public void ValidateShareRequestRejectsOversizedPayload()
    {
        var config = new PoolConfig
        {
            MaxShareRequestBytes = 512
        };
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 1024;

        BootRequestValidationFailure? failure = BootRequestGuards.ValidateShareRequest(
            config,
            context.Request,
            "bc1qtestmineraddress000000000000000000000000000",
            ValidHeaderHex,
            ValidCoinbaseHex,
            [ValidMerkleHash]);

        Assert.IsTrue(failure.HasValue);
        Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, failure.Value.StatusCode);
    }

    [TestMethod]
    public void ValidateShareRequestRejectsOversizedMerklePath()
    {
        var config = new PoolConfig
        {
            MaxMerklePathEntries = 2
        };
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 1024;

        BootRequestValidationFailure? failure = BootRequestGuards.ValidateShareRequest(
            config,
            context.Request,
            "bc1qtestmineraddress000000000000000000000000000",
            ValidHeaderHex,
            ValidCoinbaseHex,
            [ValidMerkleHash, ValidMerkleHash, ValidMerkleHash]);

        Assert.IsTrue(failure.HasValue);
        Assert.AreEqual(StatusCodes.Status400BadRequest, failure.Value.StatusCode);
    }
}
