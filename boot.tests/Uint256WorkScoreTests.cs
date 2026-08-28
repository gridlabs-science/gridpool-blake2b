using System.Numerics;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class Uint256WorkScoreTests
{
    [TestMethod]
    public void ComplementRankingMakesLowerPowValueRankHigher()
    {
        BigInteger strongerPow = new(7);
        BigInteger weakerPow = new(11);

        BigInteger strongerScore = Uint256WorkScore.FromPowValue(strongerPow);
        BigInteger weakerScore = Uint256WorkScore.FromPowValue(weakerPow);

        Assert.IsTrue(strongerScore > weakerScore);
        Assert.AreEqual(Uint256WorkScore.MaxValue - strongerPow, strongerScore);
    }

    [TestMethod]
    public void JsonAndBinaryFormsAreExactUnsignedBigEndianUint256()
    {
        BigInteger value = BigInteger.Parse("123456789012345678901234567890");
        string encoded = Uint256WorkScore.Format(value);

        Assert.AreEqual(64, encoded.Length);
        Assert.AreEqual(value, Uint256WorkScore.Parse(encoded));
        CollectionAssert.AreEqual(Convert.FromHexString(encoded), Uint256WorkScore.ToBigEndianBytes(value));
        Assert.ThrowsException<FormatException>(() => Uint256WorkScore.Parse(encoded.ToUpperInvariant()));
        Assert.ThrowsException<FormatException>(() => Uint256WorkScore.Parse(encoded[1..]));
    }

    [TestMethod]
    public void AdmissionTargetUsesExactIntegerDivisionAndRejectsInvalidInputs()
    {
        Assert.AreEqual(new BigInteger(33), Uint256WorkScore.AdmissionTarget(new BigInteger(100), 3));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Uint256WorkScore.AdmissionTarget(new BigInteger(100), 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Uint256WorkScore.FromPowValue(BigInteger.One << 256));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Uint256WorkScore.Format(-BigInteger.One));
    }
}
