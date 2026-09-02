using boot_portal.Models;
using boot_portal.Utils;
using boot_portal.Services;
using NSec.Cryptography;

namespace boot.tests;

[TestClass]
public sealed class ChainDomainProfileTests
{
    [TestMethod]
    public void BlakeForkDefaultsOnlyToBlakeMainnetSeed()
    {
        Assert.AreEqual("https://blake.gridpool.net", Program.DefaultPublicSeedEndpoint);
        CollectionAssert.AreEqual(
            new[] { "https://blake.gridpool.net" },
            Program.DefaultPublicSeedEndpoints);
        Assert.IsFalse(Program.DefaultPublicSeedEndpoints.Any(seed =>
            seed.Contains("main.gridpool.net", StringComparison.OrdinalIgnoreCase) ||
            seed.Contains("dallas.gridpool.net", StringComparison.OrdinalIgnoreCase) ||
            seed.Contains("detroit.gridpool.net", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Testnet4ProfileHasCanonicalOwnerAssignedFingerprint()
    {
        PoolConfig config = CreateTestnet4Config();

        Assert.IsTrue(ChainDomainProfiles.TryResolve(config, out ChainDomainProfile? profile, out string? error), error);
        Assert.IsNotNull(profile);
        Assert.AreEqual(ChainDomainProfiles.Blake2bTestnet4NetworkId, profile.NetworkId);
        Assert.AreEqual(ChainDomainProfiles.Testnet4GenesisHash, profile.GenesisHash);
        Assert.AreEqual(150_027, profile.ActivationHeight);
        Assert.AreEqual("height-150027-headline-v1", profile.ActivationRuleId);
        Assert.AreEqual("knots-rc3-afbe91c-v1", profile.ProfileRevision);
        Assert.AreEqual("2ad111b42ae7bd90e41e385d838853455cacc54aefe5f61cbc094c01ee6908d0", profile.Fingerprint);
        Assert.AreEqual(32, profile.FingerprintBytes.Length);
        Assert.IsTrue(profile.CanonicalTranscript.EndsWith('\n'));
        Assert.AreEqual(472, System.Text.Encoding.UTF8.GetByteCount(profile.CanonicalTranscript));
    }

    [TestMethod]
    public void BlakeConfigRequiresExactNetworkVersionAndFeeFreePolicy()
    {
        PoolConfig valid = CreateTestnet4Config();
        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(valid));

        valid.BitcoinNetwork = BitcoinScript.Mainnet;
        valid.BootNetworkId = "testnet4-beta";
        valid.BootProtocolVersion = BootProtocolVersions.ConsensusVersion;
        valid.WinnersListSize = 300;
        valid.GridLabsSupportFeeEnabled = true;

        List<string> errors = PoolConfigValidator.Validate(valid);
        Assert.IsTrue(errors.Any(error => error.Contains("bitcoin_network testnet4", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("boot_network_id", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("boot_protocol_version 23", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("winners_list_size 299", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("grid_labs_support_fee_enabled false", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RegtestRequiresSharedTwelveHexLabIdAndMainnetIsAssignedToActivatedRc4Chain()
    {
        var regtest = new PoolConfig
        {
            ChainProfileId = ChainDomainProfiles.Blake2bRegtestProfileId,
            BitcoinNetwork = BitcoinScript.Regtest,
            BootNetworkId = "gridpool-blake2b-regtest-v1:0123abcdef89",
            BootProtocolVersion = BootProtocolVersions.BlakeConsensusVersion,
            WinnersListSize = 299,
            GridLabsSupportFeeEnabled = false,
            EnablePeerUdpFastRelay = false,
            EnablePulseProofs = false,
            EnableOptimisticShareRelay = false
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(regtest));
        Assert.IsTrue(ChainDomainProfiles.TryResolve(regtest, out ChainDomainProfile? profile, out _));
        Assert.AreEqual(110, profile!.ActivationHeight);

        regtest.BootNetworkId = "gridpool-blake2b-regtest-v1:NOT-HEX";
        Assert.IsTrue(PoolConfigValidator.Validate(regtest).Any(error =>
            error.Contains("12 lowercase hex", StringComparison.Ordinal)));

        var unassignedMainnet = new PoolConfig
        {
            ChainProfileId = ChainDomainProfiles.Blake2bMainnetUnassignedProfileId,
            BitcoinNetwork = BitcoinScript.Mainnet,
            BootNetworkId = ChainDomainProfiles.Blake2bMainnetNetworkId,
            BootProtocolVersion = BootProtocolVersions.BlakeConsensusVersion,
            GridLabsSupportFeeEnabled = false
        };
        Assert.IsTrue(PoolConfigValidator.Validate(unassignedMainnet).Any(error =>
            error.Contains("unassigned", StringComparison.Ordinal)));

        var mainnet = new PoolConfig
        {
            ChainProfileId = ChainDomainProfiles.Blake2bMainnetProfileId,
            BitcoinNetwork = BitcoinScript.Mainnet,
            BootNetworkId = ChainDomainProfiles.Blake2bMainnetNetworkId,
            BootProtocolVersion = BootProtocolVersions.BlakeConsensusVersion,
            WinnersListSize = 299,
            GridLabsSupportFeeEnabled = false,
            EnablePeerUdpFastRelay = false,
            EnablePulseProofs = false,
            EnableOptimisticShareRelay = false
        };
        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(mainnet));
        Assert.IsTrue(ChainDomainProfiles.TryResolve(mainnet, out ChainDomainProfile? mainnetProfile, out string? error), error);
        Assert.AreEqual(961_640, mainnetProfile!.ActivationHeight);
        Assert.AreEqual("height-961640-headline-v1", mainnetProfile.ActivationRuleId);
        Assert.AreEqual("knots-blake2b-target-shift22-v1", mainnetProfile.TargetRuleId);
        Assert.AreEqual("knots-rc4-dc82be77-activated-v1", mainnetProfile.ProfileRevision);
        Assert.AreEqual("8d19554cd57c217c6fb0680e506cd9356eb60e6dfd7c050385477f07895aef2c", mainnetProfile.Fingerprint);
    }

    [TestMethod]
    public void Version23ActivatesOnlyAtTrustedBlakeBoundary()
    {
        PoolConfig config = CreateTestnet4Config();

        Assert.AreEqual(22, BootProtocolVersions.GetActiveConsensusVersion(config, 150_026));
        Assert.AreEqual(22, BootProtocolVersions.GetActiveConsensusVersion(config, null));
        Assert.AreEqual(23, BootProtocolVersions.GetActiveConsensusVersion(config, 150_027));

        BootNodeVersionInfo before = BootProtocolVersions.Local(config, 22);
        Assert.AreEqual(3, before.StateBundleSchemaVersion);
        Assert.AreEqual(1, before.HttpApiVersion);
        Assert.AreEqual(2, before.PeerTransportVersion);
        Assert.AreEqual(5, before.UdpRelayVersion);

        BootNodeVersionInfo active = BootProtocolVersions.Local(config, 23);
        Assert.AreEqual(4, active.StateBundleSchemaVersion);
        Assert.AreEqual(2, active.HttpApiVersion);
        Assert.AreEqual(3, active.PeerTransportVersion);
        Assert.AreEqual(6, active.UdpRelayVersion);
    }

    [TestMethod]
    public void LocalDatumAdapterBindsSharesToConfiguredChainDomain()
    {
        PoolConfig config = CreateTestnet4Config();
        Assert.IsTrue(ChainDomainProfiles.TryResolve(config, out ChainDomainProfile? profile, out _));

        Assert.AreEqual(
            profile!.Fingerprint,
            ClientHandler.ResolveTrustedLocalChainDomainFingerprint(config));

        config.ChainProfileId = "unknown-profile";
        Assert.AreEqual(string.Empty, ClientHandler.ResolveTrustedLocalChainDomainFingerprint(config));
    }

    [TestMethod]
    public void Version23CompatibilityRejectsMissingOrWrongDomain()
    {
        PoolConfig config = CreateTestnet4Config();
        BootNodeVersionInfo local = BootProtocolVersions.Local(config, 23);
        BootNodeVersionInfo remote = BootProtocolVersions.Local(config, 23);

        BootVersionCompatibilityDto compatible = BootProtocolVersions.Evaluate(
            local,
            remote,
            config.BootNetworkId,
            config.BootNetworkId,
            requireStateBundleSchema: true);
        Assert.IsTrue(compatible.CanSyncState);
        Assert.IsTrue(compatible.ChainDomainCompatible);

        remote.ChainDomainFingerprint = string.Empty;
        BootVersionCompatibilityDto missing = BootProtocolVersions.Evaluate(
            local,
            remote,
            config.BootNetworkId,
            config.BootNetworkId,
            requireStateBundleSchema: true);
        Assert.IsFalse(missing.CanSyncState);
        StringAssert.Contains(missing.Reason, "chain domain fingerprint");

        remote.ChainDomainFingerprint = new string('0', 64);
        Assert.IsFalse(BootProtocolVersions.Evaluate(
            local,
            remote,
            config.BootNetworkId,
            config.BootNetworkId,
            requireStateBundleSchema: true).CanSyncState);
    }

    [TestMethod]
    public void PeerTransportV3HelloSignsAndValidatesDomainFingerprint()
    {
        PoolConfig config = CreateTestnet4Config();
        BootNodeVersionInfo version = BootProtocolVersions.Local(config, 23);
        var identity = new BootPeerIdentity(
            Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }),
            Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }));

        BootPeerSessionHello hello = identity.CreateHello(config, version, "https://testnet4.blake.gridpool.net");
        Assert.AreEqual(version.ChainDomainFingerprint, hello.ChainDomainFingerprint);
        Assert.IsTrue(identity.ValidateHello(hello, config, version, out string acceptedReason), acceptedReason);

        hello.ChainDomainFingerprint = new string('0', 64);
        Assert.IsFalse(identity.ValidateHello(hello, config, version, out string rejectionReason));
        StringAssert.Contains(rejectionReason, "chain domain fingerprint");
    }

    private static PoolConfig CreateTestnet4Config() => new()
    {
        ChainProfileId = ChainDomainProfiles.Blake2bTestnet4ProfileId,
        BitcoinNetwork = BitcoinScript.Testnet4,
        BootNetworkId = ChainDomainProfiles.Blake2bTestnet4NetworkId,
        BootProtocolVersion = BootProtocolVersions.BlakeConsensusVersion,
        WinnersListSize = 299,
        GridLabsSupportFeeEnabled = false,
        EnablePeerUdpFastRelay = false,
        EnablePulseProofs = false,
        EnableOptimisticShareRelay = false
    };
}
