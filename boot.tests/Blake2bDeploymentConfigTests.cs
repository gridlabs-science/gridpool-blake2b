using System.Text.Json;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class Blake2bDeploymentConfigTests
{
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
