using boot_portal.Models;
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
    public void ValidateShareRequestAllowsMissingMinerAddressBecauseAttributionComesFromSlotZero()
    {
        var config = new PoolConfig();
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 1024;

        BootRequestValidationFailure? failure = BootRequestGuards.ValidateShareRequest(
            config,
            context.Request,
            string.Empty,
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

    [TestMethod]
    public void ValidateShareRequestRejectsMissingHeader()
    {
        BootRequestValidationFailure? failure = Validate(
            headerHex: string.Empty,
            coinbaseHex: ValidCoinbaseHex,
            merklePath: [ValidMerkleHash]);

        AssertFailure(failure, StatusCodes.Status400BadRequest, "Missing block header");
    }

    [TestMethod]
    public void ValidateShareRequestRejectsBadHeaderLength()
    {
        BootRequestValidationFailure? failure = Validate(
            headerHex: "abcd",
            coinbaseHex: ValidCoinbaseHex,
            merklePath: [ValidMerkleHash]);

        AssertFailure(failure, StatusCodes.Status400BadRequest, "Block header must be 80 bytes of hex");
    }

    [TestMethod]
    public void ValidateShareRequestRejectsNonHexHeader()
    {
        BootRequestValidationFailure? failure = Validate(
            headerHex: new string('z', 160),
            coinbaseHex: ValidCoinbaseHex,
            merklePath: [ValidMerkleHash]);

        AssertFailure(failure, StatusCodes.Status400BadRequest, "Block header must be 80 bytes of hex");
    }

    [TestMethod]
    public void ValidateShareRequestRejectsMissingCoinbase()
    {
        BootRequestValidationFailure? failure = Validate(
            headerHex: ValidHeaderHex,
            coinbaseHex: string.Empty,
            merklePath: [ValidMerkleHash]);

        AssertFailure(failure, StatusCodes.Status400BadRequest, "Missing coinbase transaction");
    }

    [TestMethod]
    public void ValidateShareRequestRejectsOversizedCoinbase()
    {
        BootRequestValidationFailure? failure = Validate(
            config: new PoolConfig { MaxCoinbaseHexChars = 8 },
            headerHex: ValidHeaderHex,
            coinbaseHex: ValidCoinbaseHex,
            merklePath: [ValidMerkleHash]);

        AssertFailure(failure, StatusCodes.Status400BadRequest, "Coinbase transaction exceeds configured size limit");
    }

    [TestMethod]
    public void ValidateShareRequestDefaultAllowsFullGridPoolCoinbase()
    {
        string fullGridPoolCoinbaseHex = new('a', 30000);

        BootRequestValidationFailure? failure = Validate(
            headerHex: ValidHeaderHex,
            coinbaseHex: fullGridPoolCoinbaseHex,
            merklePath: [ValidMerkleHash],
            contentLength: fullGridPoolCoinbaseHex.Length + 1024);

        Assert.IsFalse(failure.HasValue);
    }

    [TestMethod]
    public void ValidateShareRequestRejectsNonHexCoinbase()
    {
        BootRequestValidationFailure? failure = Validate(
            headerHex: ValidHeaderHex,
            coinbaseHex: "xyz123",
            merklePath: [ValidMerkleHash]);

        AssertFailure(failure, StatusCodes.Status400BadRequest, "Coinbase transaction must be hex");
    }

    [TestMethod]
    public void ValidateShareRequestRejectsMalformedMerkleEntry()
    {
        BootRequestValidationFailure? failure = Validate(
            headerHex: ValidHeaderHex,
            coinbaseHex: ValidCoinbaseHex,
            merklePath: [ValidMerkleHash, "not-hex"]);

        AssertFailure(failure, StatusCodes.Status400BadRequest, "Merkle path entries must be 32-byte hex hashes");
    }

    private static BootRequestValidationFailure? Validate(
        string headerHex,
        string coinbaseHex,
        IReadOnlyCollection<string>? merklePath,
        PoolConfig? config = null,
        long contentLength = 1024)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = contentLength;

        return BootRequestGuards.ValidateShareRequest(
            config ?? new PoolConfig(),
            context.Request,
            string.Empty,
            headerHex,
            coinbaseHex,
            merklePath);
    }

    private static void AssertFailure(BootRequestValidationFailure? failure, int expectedStatusCode, string expectedReason)
    {
        Assert.IsTrue(failure.HasValue);
        Assert.AreEqual(expectedStatusCode, failure.Value.StatusCode);
        Assert.AreEqual(expectedReason, failure.Value.Reason);
    }
}
