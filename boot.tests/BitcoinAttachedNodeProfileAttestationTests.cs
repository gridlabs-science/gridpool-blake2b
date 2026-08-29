using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class BitcoinAttachedNodeProfileAttestationTests
{
    private const string ActivationHeader =
        "000000a003a5c934b72ab4550d1eeb90db527ece84cf9909bb21774f0000000000000000" +
        "4f6b1bdc586743e6d6bffb3c8ff88cd2719eaf5508cf13ae9e6629a2a1e881d2ea7f906a" +
        "ffff001aa5d7c8fe5017b613ea7f906a00000000b10cf00d010000000000000000000000" +
        "06000000000000000000000000000000000000000b4a0200000000000000000000000000" +
        "0000000000000000000000000000000000000000";

    private const string PreActivationHeader =
        "0000662306f5573e6ac3494b2dfe4ae7304e0033e65546407c6545973612600000000000" +
        "a0566a9b0c969961d959ef491e6c6f38fa5fea1b692997f23c68ae85741c7f8ce97f906a" +
        "cb95021912c444cf";

    [TestMethod]
    public void PinnedTestnet4BoundaryPassesCompleteAttachedNodeAttestation()
    {
        ChainDomainProfile profile = ResolveProfile();
        BitcoinAttachedNodeProfileAttestationResult result =
            BitcoinAttachedNodeProfileAttestation.Evaluate(
                profile,
                Evidence());

        Assert.IsTrue(result.IsValid, result.Reason);
    }

    [TestMethod]
    public void WrongGenesisSubversionOrBoundaryFailsClosed()
    {
        ChainDomainProfile profile = ResolveProfile();

        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile,
            Evidence(genesisHash: new string('0', 64))).IsValid);
        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile,
            Evidence(genesisHash: BitcoinHashes.ReverseHexByteOrder(ChainDomainProfiles.Testnet4GenesisHash))).IsValid);
        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile,
            Evidence(subversion: "/Satoshi:29.4.1/")).IsValid);
        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile,
            Evidence(activationBlockHash: new string('0', 64))).IsValid);

        byte[] disconnected = Convert.FromHexString(PreActivationHeader);
        disconnected[4] ^= 1;
        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile,
            Evidence(preActivationHeaderHex: Convert.ToHexStringLower(disconnected))).IsValid);
    }

    [TestMethod]
    public void PreActivationNodeStillRequiresPinnedGenesisAndKnotsBuild()
    {
        ChainDomainProfile profile = ResolveProfile();
        BitcoinAttachedNodeProfileAttestationResult accepted =
            BitcoinAttachedNodeProfileAttestation.Evaluate(
                profile,
                Evidence(observedHeight: profile.ActivationHeight - 1));
        Assert.IsTrue(accepted.IsValid, accepted.Reason);

        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile,
            Evidence(
                observedHeight: profile.ActivationHeight - 1,
                subversion: "/Satoshi:29.4.1/")).IsValid);
    }

    [TestMethod]
    public void BlakeMiningHealthRequiresSuccessfulProfileAttestation()
    {
        PoolConfig config = CreateConfig();
        config.BitcoinNotificationMode = BitcoinNotificationModes.AttachedNode;
        config.BitcoinRpcUrl = "http://127.0.0.1:48332";
        var health = new BitcoinNotificationHealth(config);

        Assert.IsFalse(health.IsMiningSafe(DateTime.UtcNow, out string pendingReason));
        StringAssert.Contains(pendingReason, "chain-profile attestation");

        health.RecordChainProfileAttestation(
            true,
            ChainDomainProfiles.Testnet4GenesisHash,
            ChainDomainProfiles.Testnet4RequiredNodeSubversion,
            string.Empty,
            DateTime.UtcNow);
        health.RecordRpcSuccess(150_027, 150_027, ChainDomainProfiles.Testnet4ActivationBlockHash, false, 1, DateTime.UtcNow);

        Assert.IsTrue(health.IsMiningSafe(DateTime.UtcNow, out string acceptedReason), acceptedReason);
        BootBitcoinRpcHealthDto snapshot = health.Snapshot(DateTime.UtcNow).Rpc;
        Assert.IsTrue(snapshot.ChainProfileAttestationRequired);
        Assert.IsTrue(snapshot.ChainProfileAttested);
        Assert.AreEqual(ChainDomainProfiles.Blake2bTestnet4ProfileId, snapshot.ChainProfileId);

        health.RecordRpcAuthorityFailure("attached chain identity mismatch", DateTime.UtcNow);
        Assert.IsFalse(health.IsMiningSafe(DateTime.UtcNow, out string rejectedReason));
        StringAssert.Contains(rejectedReason, "chain identity mismatch");
    }

    private static BitcoinAttachedNodeProfileEvidence Evidence(
        string? genesisHash = null,
        string? subversion = null,
        long observedHeight = 150_245,
        string? activationBlockHash = null,
        string? activationHeaderHex = null,
        string? preActivationHeaderHex = null) => new(
            genesisHash ?? ChainDomainProfiles.Testnet4GenesisHash,
            subversion ?? ChainDomainProfiles.Testnet4RequiredNodeSubversion,
            observedHeight,
            activationBlockHash ?? ChainDomainProfiles.Testnet4ActivationBlockHash,
            activationHeaderHex ?? ActivationHeader,
            preActivationHeaderHex ?? PreActivationHeader);

    private static ChainDomainProfile ResolveProfile()
    {
        PoolConfig config = CreateConfig();
        Assert.IsTrue(ChainDomainProfiles.TryResolve(config, out ChainDomainProfile? profile, out string? error), error);
        return profile!;
    }

    private static PoolConfig CreateConfig() => new()
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
