using System.Text.Json;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class Blake2bDeploymentConfigTests
{
    private const string AuthorizedMainnetBootstrapAddress = "bc1qchlyrly5nd6a5fvq46lp8vgs9mf52g4njdwmny";

    [DataTestMethod]
    [DataRow("docker/boot_portal_config.testnet4.sample.json")]
    [DataRow("deploy/blake-vps/boot_portal_config.testnet4.blake2b.json")]
    public void Testnet4BlakeDeploymentConfigIsFeeFreeAndValid(string relativePath)
    {
        string path = FindRepositoryFile(relativePath);
        PoolConfig? config = JsonSerializer.Deserialize<PoolConfig>(File.ReadAllText(path));

        Assert.IsNotNull(config, $"Unable to deserialize {relativePath}.");
        CollectionAssert.AreEqual(Array.Empty<string>(), PoolConfigValidator.Validate(config));
        Assert.AreEqual(ChainDomainProfiles.Blake2bTestnet4ProfileId, config.ChainProfileId);
        Assert.AreEqual(ChainDomainProfiles.Blake2bTestnet4NetworkId, config.BootNetworkId);
        Assert.AreEqual(BootProtocolVersions.BlakeConsensusVersion, config.BootProtocolVersion);
        Assert.IsFalse(config.GridLabsSupportFeeEnabled);
        Assert.IsFalse(config.EnablePeerUdpFastRelay);
        Assert.IsFalse(config.EnablePulseProofs);
        Assert.IsFalse(config.EnableOptimisticShareRelay);
        Assert.AreEqual(BitcoinNotificationModes.AttachedNode, config.BitcoinNotificationMode);
    }

    [DataTestMethod]
    [DataRow("docker/boot_portal_config.sample.json")]
    [DataRow("deploy/blake-vps/boot_portal_config.mainnet.blake2b.json")]
    public void MainnetBlakeDeploymentUsesAuthorizedBootstrapAddress(string relativePath)
    {
        string path = FindRepositoryFile(relativePath);
        PoolConfig? config = JsonSerializer.Deserialize<PoolConfig>(File.ReadAllText(path));

        Assert.IsNotNull(config, $"Unable to deserialize {relativePath}.");
        Assert.AreEqual(AuthorizedMainnetBootstrapAddress, config.PoolPayoutScript);
        Assert.AreEqual(ChainDomainProfiles.Blake2bMainnetProfileId, config.ChainProfileId);
        Assert.AreEqual(ChainDomainProfiles.Blake2bMainnetNetworkId, config.BootNetworkId);
        Assert.IsFalse(config.GridLabsSupportFeeEnabled);
        Assert.AreEqual(AuthorizedMainnetBootstrapAddress, BootProtocolStateService.GenesisFoundationAddress);
        Assert.AreEqual(AuthorizedMainnetBootstrapAddress, BootProtocolStateService.GridLabsSupportAddress);
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            BitcoinScript.AddressToScriptPubKeyHex(AuthorizedMainnetBootstrapAddress, BitcoinScript.Mainnet)));
    }

    private static string FindRepositoryFile(string relativePath)
    {
        foreach (string startingDirectory in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(startingDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find repository file {relativePath}.");
    }
}
