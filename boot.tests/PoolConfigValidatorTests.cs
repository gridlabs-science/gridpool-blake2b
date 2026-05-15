using System.Text;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class PoolConfigValidatorTests
{
    [TestMethod]
    public void DefaultCoinbaseTagIsGridPool()
    {
        var config = new PoolConfig();

        Assert.AreEqual("Grid Pool", config.CoinbaseTag);
        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(config));
    }

    [TestMethod]
    public void EmptyCoinbaseTagIsAllowed()
    {
        var config = new PoolConfig
        {
            CoinbaseTag = string.Empty
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(config));
    }

    [TestMethod]
    public void TooLongCoinbaseTagFailsValidation()
    {
        var config = new PoolConfig
        {
            CoinbaseTag = new string('x', PoolConfigValidator.MaxDatumCoinbaseTagBytes + 1)
        };

        List<string> errors = PoolConfigValidator.Validate(config);

        Assert.IsTrue(errors.Any(error => error.Contains("coinbase_tag", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void MultibyteCoinbaseTagLengthUsesUtf8Bytes()
    {
        var config = new PoolConfig
        {
            CoinbaseTag = string.Concat(Enumerable.Repeat("é", 128))
        };

        Assert.AreEqual(256, Encoding.UTF8.GetByteCount(config.CoinbaseTag));
        List<string> errors = PoolConfigValidator.Validate(config);

        Assert.IsTrue(errors.Any(error => error.Contains("coinbase_tag", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void InvalidPayoutAddressFailsValidation()
    {
        var config = new PoolConfig
        {
            PoolPayoutScript = "not-a-bitcoin-address"
        };

        List<string> errors = PoolConfigValidator.Validate(config);

        Assert.IsTrue(errors.Any(error => error.Contains("pool_payout_script", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BadRateLimitFailsValidation()
    {
        var config = new PoolConfig
        {
            PeerWriteRateLimitPerMinute = 0
        };

        List<string> errors = PoolConfigValidator.Validate(config);

        Assert.IsTrue(errors.Any(error => error.Contains("peer_write_rate_limit_per_minute", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SovereignModeIsAcceptedForInstallerNodes()
    {
        var config = new PoolConfig
        {
            NodeMode = "sovereign",
            PublicBaseUrl = "http://192.168.1.191:5000",
            DatumPublicHost = "192.168.1.191"
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(config));
    }

    [TestMethod]
    public void ProductionRequiresPublicEndpointsAndDisablesTestingReset()
    {
        var config = new PoolConfig
        {
            NodeMode = "production",
            PublicBaseUrl = string.Empty,
            DatumPublicHost = string.Empty,
            TestingRoundResetMode = "block_hash_low_nibble"
        };

        List<string> errors = PoolConfigValidator.Validate(config);

        Assert.IsTrue(errors.Any(error => error.Contains("public_base_url", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("datum_public_host", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("testing_round_reset_mode", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ProductionAcceptsExplicitPublicEndpoints()
    {
        var config = new PoolConfig
        {
            NodeMode = "production",
            PublicBaseUrl = "https://use1.gridlabs.science",
            DatumPublicHost = "datum-use1.gridlabs.science",
            EnableAdminApi = false,
            TestingRoundResetMode = "none"
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(config));
    }

    [TestMethod]
    public void ProductionRejectsWeakAdminKeyWhenAdminApiIsEnabled()
    {
        var config = new PoolConfig
        {
            NodeMode = "production",
            PublicBaseUrl = "https://use1.gridlabs.science",
            DatumPublicHost = "datum-use1.gridlabs.science",
            EnableAdminApi = true,
            AdminApiKey = "change-this-admin-key",
            TestingRoundResetMode = "none"
        };

        List<string> errors = PoolConfigValidator.Validate(config);

        Assert.IsTrue(errors.Any(error => error.Contains("admin_api_key", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ProductionAcceptsStrongAdminKeyWhenAdminApiIsEnabled()
    {
        var config = new PoolConfig
        {
            NodeMode = "production",
            PublicBaseUrl = "https://use1.gridlabs.science",
            DatumPublicHost = "datum-use1.gridlabs.science",
            EnableAdminApi = true,
            AdminApiKey = "0123456789abcdef0123456789abcdef",
            TestingRoundResetMode = "none"
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(config));
    }
}
