using boot_portal.Utils;

namespace boot.tests;

// Canonical vectors copied from Bitcoin Knots RC3 commit
// afbe91c299e16519f03902939fdbda8af9bd527d, src/test/data/block_header_v2.json.
[TestClass]
public sealed class Blake2bHeaderProfileTests
{
    private const string Testnet4ActivationHeader =
        "000000a003a5c934b72ab4550d1eeb90db527ece84cf9909bb21774f0000000000000000" +
        "4f6b1bdc586743e6d6bffb3c8ff88cd2719eaf5508cf13ae9e6629a2a1e881d2ea7f906a" +
        "ffff001aa5d7c8fe5017b613ea7f906a00000000b10cf00d010000000000000000000000" +
        "06000000000000000000000000000000000000000b4a0200000000000000000000000000" +
        "0000000000000000000000000000000000000000";

    private static readonly (string Name, int Profile, string Serialized, string BlockHash)[] UpstreamVectors =
    [
        (
            "profile_0_time_offset",
            0,
            "000000a01f1e1d1c1b1a191817161514131211100f0e0d0c0b0a0908070605040302010000112233445566778899aabbccddeeff00102030405060708090a0b0c0d0e0f0a8913577ffff001d0df0ad0b44332211efcdab89ffeeddccbbaa998877665544332211005802000003001c000000000000000000000000000000000040d10c008967452301efcdab8967452301efcdab8967452301efcdab8967452301efcdab",
            "4b495dcf05d70a49785b799b22284fbcd9dd1209237c53c87e4674b15587d704"
        ),
        (
            "profile_1_time_offset_nonzero_key",
            1,
            "000000a01f1e1d1c1b1a191817161514131211100f0e0d0c0b0a0908070605040302010000112233445566778899aabbccddeeff00102030405060708090a0b0c0d0e0f0a8913577ffff001d0df0ad0b44332211efcdab89ffeeddccbbaa998877665544332211005802000001001d00efcdab8967452301efcdab896745230141d10c008967452301efcdab8967452301efcdab8967452301efcdab8967452301efcdab",
            "44b383821dea9af8d7d81ba7741c34ac8c07ab81ab081d8b6bf0575a787a1eef"
        ),
        (
            "profile_2_time_offset_selector_7",
            2,
            "000000a01f1e1d1c1b1a191817161514131211100f0e0d0c0b0a0908070605040302010000112233445566778899aabbccddeeff00102030405060708090a0b0c0d0e0f0a8913577ffff001d0df0ad0bddccbbaaefcdab89ffeeddccbbaa998877665544332211005802000003001e071032547698badcfe1032547698badcfe40d10c008967452301efcdab8967452301efcdab8967452301efcdab8967452301efcdab",
            "06fddae4eaca10b85c87a3c7ed71717fd83998a32fe13f4780722b1f5d882e76"
        ),
        (
            "profile_3_time_offset_selector_8",
            3,
            "000000a01f1e1d1c1b1a191817161514131211100f0e0d0c0b0a0908070605040302010000112233445566778899aabbccddeeff00102030405060708090a0b0c0d0e0f0a8913577ffff001d0df0ad0b44332211040302010000000000000000ffffffffffffffff5802000003001f081032547698badcfe1032547698badcfe40d10c008967452301efcdab8967452301efcdab8967452301efcdab8967452301efcdab",
            "e6304527536f619d3ad71b1c21a22fdef9068498acc561b4100b034373a87058"
        ),
        (
            "profile_0_time_offset_disabled_selector_255",
            0,
            "000000a01f1e1d1c1b1a191817161514131211100f0e0d0c0b0a0908070605040302010000112233445566778899aabbccddeeff00102030405060708090a0b0c0d0e0f000943577ffff001dffffffff44332211efcdab89ffeeddccbbaa9988776655443322110088776655030018ff2222222222222222111111111111111140d10c000000000000000000000000000000000000000000000000000000000000000000",
            "c31b24420d67f86e524f980a24a18e88f36c821046d5288251b5d88998c69f86"
        )
    ];

    [TestMethod]
    public void AllFivePinnedRc3VectorsMatchExactPowHashes()
    {
        IChainHeaderProfile profile = ChainProfiles.BitcoinBlake2bHeaderV2;

        foreach ((string name, int asicProfile, string serialized, string expectedHash) in UpstreamVectors)
        {
            ParsedChainHeader header = profile.ParseAndHash(serialized);

            Assert.AreEqual("blake2b", profile.PowAlgorithmId, name);
            Assert.AreEqual("bitcoin-header-v2", profile.HeaderFormatId, name);
            Assert.AreEqual(164, profile.HeaderLengthBytes, name);
            Assert.AreEqual(expectedHash, header.DisplayBlockHash, name);
            Assert.AreEqual(expectedHash, Convert.ToHexString(header.PowHashLittleEndianBytes.Reverse().ToArray()).ToLowerInvariant(), name);
            Assert.AreEqual(asicProfile, header.HeaderBytes[110] & 3, name);
            Assert.AreEqual("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", header.DisplayParentBlockHash, name);
            Assert.AreEqual(0x1d00ffffu, header.CompactTarget, name);
            Assert.IsTrue(header.AchievedWork > 0, name);
            Assert.AreEqual(header.HeaderBytes[110], header.HeaderFlags, name);
        }
    }

    [TestMethod]
    public void HeaderSelectionAndEffectiveTimeAreActivationFormatAware()
    {
        ParsedChainHeader offsetEnabled = ChainProfiles.SelectForHeader(UpstreamVectors[0].Serialized)
            .ParseAndHash(UpstreamVectors[0].Serialized);
        ParsedChainHeader offsetDisabled = ChainProfiles.SelectForHeader(UpstreamVectors[4].Serialized)
            .ParseAndHash(UpstreamVectors[4].Serialized);

        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(2_000_000_000).UtcDateTime, offsetEnabled.HeaderTimeUtc);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(2_000_000_000).UtcDateTime, offsetDisabled.HeaderTimeUtc);
        Assert.AreSame(ChainProfiles.BitcoinBlake2bHeaderV2, ChainProfiles.SelectForHeader(UpstreamVectors[0].Serialized));
    }

    [TestMethod]
    public void ObservedTestnet4ActivationHeaderMatchesPinnedChainEvidence()
    {
        ParsedChainHeader header = ChainProfiles.BitcoinBlake2bHeaderV2.ParseAndHash(Testnet4ActivationHeader);

        Assert.AreEqual("000000000000007a178eb03e6619f0420d7d38e278e6bb5ee16f15ac5b32cee6", header.DisplayBlockHash);
        Assert.AreEqual("00000000000000004f7721bb0999cf84ce7e52db90eb1e0d55b42ab734c9a503", header.DisplayParentBlockHash);
        Assert.AreEqual(0x1a00ffffu, header.CompactTarget);
        Assert.AreEqual(150027, header.DeclaredHeight);
        Assert.AreEqual((ushort)6, header.DeclaredTransactionCount);
        Assert.AreEqual((byte)0, header.HeaderFlags);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_787_854_826).UtcDateTime, header.HeaderTimeUtc);
        Assert.IsTrue(header.PowValue <= header.EncodedTarget);
        Assert.AreEqual(Uint256WorkScore.MaxValue - header.PowValue, header.AchievedWork);
        Assert.AreEqual(64, Uint256WorkScore.Format(header.AchievedWork).Length);
    }

    [TestMethod]
    public void ReservedHighFlagBitsAndMissingV2MarkerAreRejected()
    {
        byte[] highFlags = Convert.FromHexString(UpstreamVectors[0].Serialized);
        highFlags[110] |= 0x40;
        ArgumentException highFlagsError = Assert.ThrowsException<ArgumentException>(
            () => ChainProfiles.BitcoinBlake2bHeaderV2.ParseAndHash(Convert.ToHexString(highFlags)));
        StringAssert.Contains(highFlagsError.Message, "reserved high flag bits");

        byte[] missingMarker = Convert.FromHexString(UpstreamVectors[0].Serialized);
        missingMarker[3] &= 0x7f;
        ArgumentException markerError = Assert.ThrowsException<ArgumentException>(
            () => ChainProfiles.BitcoinBlake2bHeaderV2.ParseAndHash(Convert.ToHexString(missingMarker)));
        StringAssert.Contains(markerError.Message, "header-v2 version flag");
    }
}
