using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class SegwitShareValidationTests
{
    private const string MinerAddress = "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y";
    private const string PreviousBlockHash = "000000000000000000017aedb62d18a964ee5bc8b94fb87efca6df9d6f99431a";
    private const string HeaderHex = "10609d221a43996f9ddfa6fc7eb84fb9c85bee64a9182db6ed7a0100000000000000000080fed2efc47069fb944a0b2afab1d6ee5a0cf4ab60ba497769b6007d7e1f6ae692eb686ad43a0217b084bb42";
    private const string CoinbaseHex = "020000000001010000000000000000000000000000000000000000000000000000000000000000ffffffff380305a60e1e2f47726964506f6f6c2053746172744f53204e6174697665205356322f2f140100000000000000000000000000000000000000ffffffff0544921d00000000001600141ba063a60ffe85ee2034c3044d7ef087a5f20f9104ca1f00000000001600141ba063a60ffe85ee2034c3044d7ef087a5f20f91eaa943070000000016001409602a9c642ec02e3612e77483bc7f12fbdf4f7968052d0b00000000225120c4f639fd27fc38c962b6978567b033dcf29fe8a6560394f606dbb31388e8dfec0000000000000000266a24aa21a9ed4cb0c10afc84c1916199ed3f494388b387687f98162b1ef86a5185e4b6d3923c0120000000000000000000000000000000000000000000000000000000000000000000000000";
    private static readonly List<string> MerklePath =
    [
        "3d44246b8dae5aa7f58885e0fa5f8d9c380e3b966709a37534358df60d92b5f6",
        "f7c6287eacd0aaa7e5a31b75049e42ea9910c052379b1764190bac57d4e05506",
        "e704c044a4d66ad039965196ddf22f3b00b684a9d24e389fc8c3c89a6904cd5b",
        "7381ec6ab87735b1a86863391bd4be7bda97a6001f4b374558efbb18e0b961c5",
        "61b4fda886ef8c92d96b537577594f72fb14959ba4b96e9fda104b88e8be40cf",
        "bbdb696951dab31990638a9e48f857b6fe7173af9f1f3cf6de24b10770ae031c",
        "dcef65458236793dba92336da9ce9733673700808ab72a7513ae0cfa6ca815f6",
        "170704c0a93457f41f18a88a815ebbb68ecba53b4641bb264a7b33410047fb2f",
        "9906e26a997cdbec64d8d8f78bad95b1ff9a1a2fafeaa92ddee11cd8bd8ad940",
        "92e92ba799098ebefcd622a4efd27fa7d2f84761c7f00b5820909a0c8bd25d20",
        "0ac5a345ab38334802a095abc1ce30cd9d52fd997520634100298df085275804"
    ];

    [TestMethod]
    public void ValidateShareUsesTransactionIdForSegwitCoinbaseMerkleRoot()
    {
        byte[] transactionIdHash =
            BitcoinTransactionParser.ComputeTransactionIdHash(Convert.FromHexString(CoinbaseHex));
        Assert.AreEqual(
            "24a257e0ec6145333d963f90e5d34d24748a009cce6d315ea0fe229828c01f6b",
            Convert.ToHexString(transactionIdHash).ToLowerInvariant());

        IReadOnlyList<PayoutInfo> expectedWinners = BitcoinTransactionParser
            .ParseOutputs(Convert.FromHexString(CoinbaseHex))
            .Skip(1)
            .Select(output => new PayoutInfo
            {
                Value = output.Value,
                Address = BitcoinScript.ScriptToAddress(output.ScriptPubKey)
            })
            .Where(output => !string.Equals(
                output.Address,
                "UNKNOWN",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var submission = new RecordedShareSubmission
        {
            MinerAddress = MinerAddress,
            Username = $"{MinerAddress}.sv2test",
            HeaderHex = HeaderHex,
            CoinbaseHex = CoinbaseHex,
            MerklePath = MerklePath.ToList(),
            PrevBlockHash = PreviousBlockHash,
            Source = "sv2"
        };

        BootShareValidationResult result = new BootShareVerifier().ValidateShare(
            submission,
            expectedWinners,
            PreviousBlockHash);

        Assert.IsTrue(result.IsValid, result.RejectionReason);
        Assert.AreEqual(MinerAddress, result.MinerAddress);
    }
}
