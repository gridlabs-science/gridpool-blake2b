using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class BitcoinAttachedNodeProfileAttestationTests
{
    private const string MainnetActivationHeader =
        "000000a0657e02138733654183a2c7320d85ca9d743fe139c4bb01000000000000000000" +
        "c137a8515a0f6b3aaf6049cc7611787c022ad523d51094be0a0363d0dc0bc7684dca936a" +
        "4f8d001a5671798c84daeb494dca936a00000000b1ccf00d030000000000000000000000" +
        "1e0300000000000000000000000000000000000068ac0e00000000000000000000000000" +
        "0000000000000000000000000000000000000000";

    private const string MainnetPreActivationHeader =
        "10000a205fca17a6566978303e989d163e1aa9dc6715eef5542e00000000000000000000" +
        "80fe52c98f1c1f8484213dff5a88315f7c334d0705f7d79579b289781868c0dff5c1916a" +
        "3d350217510c87ed";

    private const string MainnetActivationCoinbaseScriptSig =
        "0368ac0e2a53696c656e74576176650f382d3330204e59506f73742044657269646520416e6420436f6e71756572" +
        "0003ff92100eb12e000000000000000000000000";

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
    public void PinnedActivatedMainnetBoundaryPassesCompleteAttachedNodeAttestation()
    {
        PoolConfig config = new()
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
        Assert.IsTrue(ChainDomainProfiles.TryResolve(config, out ChainDomainProfile? profile, out string? error), error);

        BitcoinAttachedNodeProfileAttestationResult accepted =
            BitcoinAttachedNodeProfileAttestation.Evaluate(
                profile!,
                new BitcoinAttachedNodeProfileEvidence(
                    ChainDomainProfiles.MainnetGenesisHash,
                    ChainDomainProfiles.MainnetRequiredNodeSubversion,
                    962_733,
                    ChainDomainProfiles.MainnetActivationBlockHash,
                    MainnetActivationHeader,
                    MainnetPreActivationHeader));
        Assert.IsTrue(accepted.IsValid, accepted.Reason);

        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile!,
            new BitcoinAttachedNodeProfileEvidence(
                ChainDomainProfiles.MainnetGenesisHash,
                "/Satoshi:29.4.1/Knots:20260508rc3/",
                962_733,
                ChainDomainProfiles.MainnetActivationBlockHash,
                MainnetActivationHeader,
                MainnetPreActivationHeader)).IsValid);

        byte[] scriptSig = Convert.FromHexString(MainnetActivationCoinbaseScriptSig);
        byte[] headline = System.Text.Encoding.ASCII.GetBytes(ChainDomainProfiles.MainnetActivationHeadline);
        Assert.IsTrue(scriptSig.AsSpan().IndexOf(headline) >= 0);
        Assert.IsFalse(BitcoinAttachedNodeProfileAttestation.Evaluate(
            profile!,
            new BitcoinAttachedNodeProfileEvidence(
                ChainDomainProfiles.MainnetGenesisHash,
                ChainDomainProfiles.MainnetRequiredNodeSubversion,
                962_733,
                new string('0', 64),
                MainnetActivationHeader,
                MainnetPreActivationHeader)).IsValid);
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
