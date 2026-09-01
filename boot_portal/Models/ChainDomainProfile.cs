using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace boot_portal.Models;

public sealed class ChainDomainProfile
{
    public required string ProfileId { get; init; }
    public required string NetworkId { get; init; }
    public required string ChainId { get; init; }
    public required string GenesisHash { get; init; }
    public required int ConsensusVersion { get; init; }
    public required string PowAlgorithmId { get; init; }
    public required string HeaderFormatId { get; init; }
    public required string ActivationRuleId { get; init; }
    public required long ActivationHeight { get; init; }
    public required string TargetRuleId { get; init; }
    public required string WorkScoreRuleId { get; init; }
    public required string ProfileRevision { get; init; }
    public required string PayoutPolicyId { get; init; }

    public string CanonicalTranscript =>
        "gridpool-chain-domain-v1\n" +
        $"network_id={NetworkId}\n" +
        $"chain_id={ChainId}\n" +
        $"genesis_hash={GenesisHash}\n" +
        $"consensus_version={ConsensusVersion}\n" +
        $"pow_algorithm_id={PowAlgorithmId}\n" +
        $"header_format_id={HeaderFormatId}\n" +
        $"activation_rule_id={ActivationRuleId}\n" +
        $"target_rule_id={TargetRuleId}\n" +
        $"work_score_rule_id={WorkScoreRuleId}\n" +
        $"profile_revision={ProfileRevision}\n" +
        $"payout_policy_id={PayoutPolicyId}\n";

    public byte[] FingerprintBytes => SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalTranscript));
    public string Fingerprint => Convert.ToHexStringLower(FingerprintBytes);
}

public static partial class ChainDomainProfiles
{
    public const string LegacySha256dProfileId = "bitcoin-sha256d-header-v1";
    public const string Blake2bTestnet4ProfileId = "knots-rc3-afbe91c-testnet4-v1";
    public const string Blake2bRegtestProfileId = "knots-pr359-fee27ccf-regtest-v1";
    public const string Blake2bMainnetUnassignedProfileId = "knots-blake2b-mainnet-unassigned";
    public const string Blake2bMainnetProfileId = "knots-blake2b-mainnet-rc4-activated";

    public const string Blake2bTestnet4NetworkId = "gridpool-blake2b-testnet4-v1";
    public const string Blake2bRegtestNetworkPrefix = "gridpool-blake2b-regtest-v1:";
    public const string Blake2bMainnetNetworkId = "gridpool-blake2b-mainnet-v1";

    public const string Testnet4GenesisHash = "00000000da84f2bafbbc53dee25a72ae507ff4914b867c565be350b0da8bf043";
    public const string Testnet4ActivationBlockHash = "000000000000007a178eb03e6619f0420d7d38e278e6bb5ee16f15ac5b32cee6";
    public const string Testnet4RequiredNodeSubversion = "/Satoshi:29.4.1/Knots:20260508rc3/";
    public const uint Testnet4ActivationCompactTarget = 0x1a00ffff;
    public const string RegtestGenesisHash = "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206";
    public const string MainnetGenesisHash = "000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f";
    public const string MainnetActivationBlockHash = "0000000000000050c1e5f69672f459293be14f46e5a494e7a8c8541396f18eeb";
    public const string MainnetActivationParentBlockHash = "00000000000000000001bbc439e13f749dca850d32c7a2834165338713027e65";
    public const string MainnetRequiredNodeSubversion = "/Satoshi:29.4.1/Knots:20260508rc4/";
    public const string MainnetActivationHeadline = "8-30 NYPost Deride And Conquer";
    public const uint MainnetActivationCompactTarget = 0x1a008d4f;

    public const string PowAlgorithmId = "knots-blake2b-v2";
    public const string HeaderFormatId = "knots-header-v2-164";
    public const string TargetRuleId = "knots-blake2b-target-shift20-v1";
    public const string MainnetTargetRuleId = "knots-blake2b-target-shift22-v1";
    public const string WorkScoreRuleId = "uint256-complement-v1";
    public const string PayoutPolicyId = "fee-free-299-v1";

    public static bool IsLegacySha256d(string? profileId) =>
        string.Equals(profileId?.Trim(), LegacySha256dProfileId, StringComparison.Ordinal);

    public static bool IsBlake2b(string? profileId) =>
        !IsLegacySha256d(profileId) &&
        profileId?.Trim().StartsWith("knots-", StringComparison.Ordinal) == true;

    public static bool TryResolve(PoolConfig config, out ChainDomainProfile? profile, out string? error)
    {
        string profileId = config.ChainProfileId?.Trim() ?? string.Empty;
        profile = null;
        error = null;

        if (IsLegacySha256d(profileId))
        {
            return true;
        }

        if (profileId == Blake2bMainnetUnassignedProfileId)
        {
            error = "chain_profile_id selects Blake2b mainnet, but its activation rule and profile revision are unassigned";
            return false;
        }

        if (profileId == Blake2bMainnetProfileId)
        {
            profile = new ChainDomainProfile
            {
                ProfileId = Blake2bMainnetProfileId,
                NetworkId = Blake2bMainnetNetworkId,
                ChainId = "bip110-blake2b-mainnet",
                GenesisHash = MainnetGenesisHash,
                ConsensusVersion = BootProtocolVersions.BlakeConsensusVersion,
                PowAlgorithmId = PowAlgorithmId,
                HeaderFormatId = HeaderFormatId,
                ActivationRuleId = "height-961640-headline-v1",
                ActivationHeight = 961_640,
                TargetRuleId = MainnetTargetRuleId,
                WorkScoreRuleId = WorkScoreRuleId,
                ProfileRevision = "knots-rc4-dc82be77-activated-v1",
                PayoutPolicyId = PayoutPolicyId
            };
            return true;
        }

        if (profileId == Blake2bTestnet4ProfileId)
        {
            profile = new ChainDomainProfile
            {
                ProfileId = Blake2bTestnet4ProfileId,
                NetworkId = Blake2bTestnet4NetworkId,
                ChainId = "bip110-blake2b-testnet4",
                GenesisHash = Testnet4GenesisHash,
                ConsensusVersion = BootProtocolVersions.BlakeConsensusVersion,
                PowAlgorithmId = PowAlgorithmId,
                HeaderFormatId = HeaderFormatId,
                ActivationRuleId = "height-150027-headline-v1",
                ActivationHeight = 150_027,
                TargetRuleId = TargetRuleId,
                WorkScoreRuleId = WorkScoreRuleId,
                ProfileRevision = "knots-rc3-afbe91c-v1",
                PayoutPolicyId = PayoutPolicyId
            };
            return true;
        }

        if (profileId == Blake2bRegtestProfileId)
        {
            string networkId = config.BootNetworkId?.Trim() ?? string.Empty;
            if (!RegtestNetworkIdRegex().IsMatch(networkId))
            {
                error = $"boot_network_id for Blake2b regtest must be {Blake2bRegtestNetworkPrefix}<12 lowercase hex lab id>";
                return false;
            }

            profile = new ChainDomainProfile
            {
                ProfileId = Blake2bRegtestProfileId,
                NetworkId = networkId,
                ChainId = "bip110-blake2b-regtest",
                GenesisHash = RegtestGenesisHash,
                ConsensusVersion = BootProtocolVersions.BlakeConsensusVersion,
                PowAlgorithmId = PowAlgorithmId,
                HeaderFormatId = HeaderFormatId,
                ActivationRuleId = "height-110-headline-v1",
                ActivationHeight = 110,
                TargetRuleId = TargetRuleId,
                WorkScoreRuleId = WorkScoreRuleId,
                ProfileRevision = "knots-pr359-fee27ccf-v1",
                PayoutPolicyId = PayoutPolicyId
            };
            return true;
        }

        error = $"chain_profile_id is unknown: {profileId}";
        return false;
    }

    [GeneratedRegex("^gridpool-blake2b-regtest-v1:[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegtestNetworkIdRegex();
}
