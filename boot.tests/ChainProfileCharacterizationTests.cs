using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class ChainProfileCharacterizationTests
{
    private const string SampleHeaderHex = "00804f274316faab82b814ce8aaf929ace20c0d52a8dc9479d02020000000000000000002e0c639c7934a697d14a314cea5da30f0c45660248d534db3cfb2036b5ac0d8a65a6e3696913021778491e84";
    private const string SampleCoinbaseHex = "01000000010000000000000000000000000000000000000000000000000000000000000000ffffffff2003e16d0e13426f6f742070726f746f636f6c0f626f6f74000709921015000000ffffffff06128e120000000000160014c64b1b9283ba1ea86bb9e7b696b0c8f68dad040004cc041000000000160014c64b1b9283ba1ea86bb9e7b696b0c8f68dad04000000000000000000106a0e9113b1ccf00d0000000000b9bb1952ad8b02000000001600141ba063a60ffe85ee2034c3044d7ef087a5f20f910000000000000000036a01000000000000000000266a24aa21a9edcddc611f6111ea75c5a265fba065e8eccb3d1ec8f954c738ea4586b3fffab1ce00000000";
    private const string SampleBlockHash = "00000000000000efcd79259abd08b006e110f9f3544b3e0e5b449851c19aa326";
    private const string SampleParentHash = "00000000000000000002029d47c98d2ad5c020ce9a92af8ace14b882abfa1643";
    private const string SampleShareId = "4a2eecc90729efcb450f963f25f1cd438a15998af3904e1d4155dd2c7185372c";
    private const long SampleDifficultyBits = 0x417114988e06478b;
    private const string RecentHeaderHex = "00a07b2daf1515873d86d8fba7a098689bcd958e6d2df870abe10100000000000000000077f88aefba92a3f434513218d7476aabaa35b9200cd339c6caea3db663ea1bfc9d355c6a9d36021724d435ae";
    private const string Blake2bTestnet4HeaderHex = "000000a07f94ff7f28e2dfc249d6cce5d5b778b7607edccb0c81fa2994000000000000007f56860f7bbfcb04f080602ea79d9b156d9992652c913a3a213b2923af771207bb94936affff001af14c41b004456d27bb94936a00000000b10cf00d0300000000000000000000000200000000000000000000000000000000000000ef4b02000000000000000000000000000000000000000000000000000000000000000000";
    private const string Blake2bTestnet4BlockHash = "00000000000000eee98f04f5539e13d6e83f3a5cd8e6b9ece675cc37f10bebcc";

    [TestMethod]
    public void ShaHeaderHashingIsExactDoubleSha256WithBitcoinDisplayByteOrder()
    {
        Assert.AreEqual(SampleBlockHash, BitcoinHashes.ComputeBlockHashFromHeader(SampleHeaderHex));

        ArgumentException exception = Assert.ThrowsException<ArgumentException>(
            () => BitcoinHashes.ComputeBlockHashFromHeader(SampleHeaderHex[..^2]));
        Assert.AreEqual("Bitcoin block header must be exactly 80 or 164 bytes. (Parameter 'headerHex')", exception.Message);
    }

    [TestMethod]
    public void ShaProfileExposesExplicitFormatAndNumericByteOrder()
    {
        IChainHeaderProfile profile = ChainProfiles.BitcoinSha256dHeaderV1;

        ParsedChainHeader header = profile.ParseAndHash(SampleHeaderHex);

        Assert.AreEqual("sha256d", profile.PowAlgorithmId);
        Assert.AreEqual("bitcoin-header-v1", profile.HeaderFormatId);
        Assert.AreEqual(80, profile.HeaderLengthBytes);
        Assert.AreEqual(SampleBlockHash, header.DisplayBlockHash);
        Assert.AreEqual("26a39ac15198445b0e3e4b54f3f910e106b008bd9a2579cdef00000000000000", Convert.ToHexString(header.PowHashLittleEndianBytes).ToLowerInvariant());
        Assert.AreEqual(SampleParentHash, header.DisplayParentBlockHash);
        Assert.AreEqual("2e0c639c7934a697d14a314cea5da30f0c45660248d534db3cfb2036b5ac0d8a", Convert.ToHexString(header.MerkleRootLittleEndianBytes).ToLowerInvariant());
        Assert.AreEqual(0x17021369u, header.CompactTarget);
        Assert.AreEqual(SampleDifficultyBits, BitConverter.DoubleToInt64Bits(header.AchievedDifficulty));
    }

    [TestMethod]
    public void Blake2bHeaderHashingMatchesThePinnedTestnet4Node()
    {
        Assert.AreEqual(Blake2bTestnet4BlockHash, BitcoinHashes.ComputeBlockHashFromHeader(Blake2bTestnet4HeaderHex));

        BitcoinHeaderEvaluation evaluation = BitcoinHashes.EvaluateHeader(
            Blake2bTestnet4HeaderHex,
            new DateTime(2026, 8, 30, 2, 0, 0, DateTimeKind.Utc),
            BitcoinScript.Testnet);

        Assert.IsTrue(evaluation.IsValid, evaluation.RejectionReason);
        Assert.AreEqual(Blake2bTestnet4BlockHash, evaluation.BlockHash);
    }

    [TestMethod]
    public void ShaHeaderEvaluationUsesTheCurrentFixedOffsetsAndEncodedTargetPolicy()
    {
        DateTime receivedUtc = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

        BitcoinHeaderEvaluation evaluation = BitcoinHashes.EvaluateHeader(RecentHeaderHex, receivedUtc);

        Assert.IsTrue(evaluation.IsValid, evaluation.RejectionReason);
        Assert.AreEqual("00000000000000000002122154787256060976bce119846233eee04fa0ac0fe2", evaluation.BlockHash);
        Assert.AreEqual("00000000000000000001e1ab70f82d6d8e95cd9b6898a0a7fbd8863d871515af", evaluation.ParentBlockHash);
        Assert.AreEqual(0x1702369du, evaluation.CompactTarget);
        Assert.AreEqual(new DateTime(2026, 7, 19, 2, 25, 33, DateTimeKind.Utc), evaluation.HeaderTimeUtc);
        Assert.AreEqual(receivedUtc, evaluation.ReceivedUtc);
    }

    [TestMethod]
    public void ShaShareEvaluationPreservesHeaderHashDifficultyAndShareIdentity()
    {
        BootShareHeaderEvaluationResult evaluation = new BootShareVerifier().EvaluateHeaderDifficulty(
            new RecordedShareSubmission
            {
                HeaderHex = SampleHeaderHex,
                CoinbaseHex = SampleCoinbaseHex,
                PrevBlockHash = SampleParentHash
            });

        Assert.IsTrue(evaluation.IsValid, evaluation.RejectionReason);
        Assert.AreEqual(SampleHeaderHex, evaluation.HeaderHex);
        Assert.AreEqual(SampleCoinbaseHex, evaluation.CoinbaseHex);
        Assert.AreEqual(SampleBlockHash, evaluation.BlockHash);
        Assert.AreEqual(SampleParentHash, evaluation.PrevBlockHash);
        Assert.AreEqual(SampleShareId, evaluation.ShareId);
        Assert.AreEqual(SampleDifficultyBits, BitConverter.DoubleToInt64Bits(evaluation.Difficulty));
        Assert.IsFalse(evaluation.IsBlock);
    }
}
