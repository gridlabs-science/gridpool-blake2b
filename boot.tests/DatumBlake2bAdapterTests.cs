using System.Buffers.Binary;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class DatumBlake2bAdapterTests
{
    [TestMethod]
    public void Profile0HeaderSerializationMatchesPinnedGatewayVector()
    {
        byte[] previous = Enumerable.Range(0xc0, 32).Select(value => (byte)value).ToArray();
        byte[] merkle = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        byte[] extranonce = Enumerable.Range(0xa0, 12).Select(value => (byte)value).ToArray();
        byte[] nBits = Convert.FromHexString("FFFF7F20");
        byte[] header = Blake2bDatumHeader.BuildProfile0(
            0x20000000,
            previous,
            merkle,
            0x6553412f,
            nBits,
            0x0807060504030201,
            0x1817161501020304,
            extranonce,
            3,
            12345,
            useTimeOffset: true);

        const string expected =
            "000000a0c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf" +
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f2f415365ffff7f20" +
            "01020304050607081516171800000000a0a1a2a3a4a5a6a7a8a9aaab0403020103000400" +
            "0000000000000000000000000000000039300000" +
            "0000000000000000000000000000000000000000000000000000000000000000";
        Assert.AreEqual(expected, Convert.ToHexString(header).ToLowerInvariant());
    }

    [TestMethod]
    public void PowSubmitParsesBoundedBlakeExtensionsAndCoinbaseType()
    {
        byte[] payload = BuildSubmit();
        PowSubmitMessage parsed = PowSubmitMessage.FromBytes(payload);

        Assert.IsTrue(parsed.IsBlake2b);
        Assert.IsTrue(parsed.BlakeUseTimeOffset);
        Assert.AreEqual(0x1817161501020304ul, parsed.NTime64);
        Assert.AreEqual(0x0807060504030201ul, parsed.Nonce64);
        Assert.AreEqual(0x6553412fu, parsed.BlakeTimeOnWire);
        Assert.AreEqual((byte)4, parsed.CoinbaseId);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, parsed.CoinbasePairs[4].Coinb1);
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, parsed.CoinbasePairs[4].Coinb2);
    }

    [TestMethod]
    public void PowSubmitRejectsMissingDuplicateAndTrailingBlakeSections()
    {
        byte[] valid = BuildSubmit();
        int algorithmOffset = Array.IndexOf(valid, (byte)0x03, 34);
        Assert.IsTrue(algorithmOffset > 0);

        byte[] missingAlgorithm = valid.Take(algorithmOffset)
            .Concat(valid.Skip(algorithmOffset + 18))
            .ToArray();
        Assert.ThrowsException<ArgumentException>(() => PowSubmitMessage.FromBytes(missingAlgorithm));

        byte[] duplicateAlgorithm = valid.Take(algorithmOffset + 18)
            .Concat(valid.Skip(algorithmOffset).Take(18))
            .Concat(valid.Skip(algorithmOffset + 18))
            .ToArray();
        Assert.ThrowsException<ArgumentException>(() => PowSubmitMessage.FromBytes(duplicateAlgorithm));

        Assert.ThrowsException<ArgumentException>(() => PowSubmitMessage.FromBytes(valid.Concat(new byte[] { 0 }).ToArray()));
        Assert.ThrowsException<ArgumentException>(() => PowSubmitMessage.FromBytes(valid[..^1]));
    }

    private static byte[] BuildSubmit()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write((byte)1);
        writer.Write((byte)4);
        writer.Write((byte)0x08);
        writer.Write((byte)14);
        writer.Write(0x01020304u);
        writer.Write(0x04030201u);
        writer.Write(0x20000000);
        writer.Write((byte)12);
        writer.Write(Enumerable.Range(0xa0, 12).Select(value => (byte)value).ToArray());
        writer.Write(System.Text.Encoding.UTF8.GetBytes("bcrt1qexample.worker"));
        writer.Write((byte)0);
        writer.Write(new byte[] { 1, 0, 0, 0 });
        writer.Write((byte)0x03);
        writer.Write((byte)1);
        writer.Write(0x1817161501020304ul);
        writer.Write(0x0807060504030201ul);
        writer.Write((byte)0x04);
        writer.Write(0x6553412fu);
        writer.Write((byte)0x01);
        writer.Write(new byte[32]);
        writer.Write((ushort)4);
        writer.Write(Convert.FromHexString("FFFF7F20"));
        writer.Write((byte)7);
        writer.Write(12345u);
        writer.Write(5_000_000_000ul);
        writer.Write(2u);
        writer.Write(800u);
        writer.Write(200u);
        writer.Write(4u);
        writer.Write((byte)0);
        writer.Write((byte)0x02);
        writer.Write((byte)4);
        writer.Write((ushort)3);
        writer.Write((ushort)2);
        writer.Write(new byte[] { 1, 2, 3 });
        writer.Write(new byte[] { 4, 5 });
        writer.Write((byte)0xfe);
        return stream.ToArray();
    }
}
