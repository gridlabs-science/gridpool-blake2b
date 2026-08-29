using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public sealed record BitcoinAttachedNodeProfileEvidence(
    string GenesisHash,
    string Subversion,
    long ObservedHeight,
    string ActivationBlockHash = "",
    string ActivationHeaderHex = "",
    string PreActivationHeaderHex = "");

public sealed record BitcoinAttachedNodeProfileAttestationResult(bool IsValid, string Reason)
{
    public static BitcoinAttachedNodeProfileAttestationResult Accept() => new(true, string.Empty);
    public static BitcoinAttachedNodeProfileAttestationResult Reject(string reason) => new(false, reason);
}

public static class BitcoinAttachedNodeProfileAttestation
{
    public static BitcoinAttachedNodeProfileAttestationResult Evaluate(
        ChainDomainProfile profile,
        BitcoinAttachedNodeProfileEvidence evidence)
    {
        if (!CanonicalHashEquals(evidence.GenesisHash, profile.GenesisHash))
        {
            return BitcoinAttachedNodeProfileAttestationResult.Reject(
                $"Attached node genesis does not match chain profile {profile.ProfileId}.");
        }

        if (profile.ProfileId == ChainDomainProfiles.Blake2bTestnet4ProfileId)
        {
            if (!string.Equals(
                    evidence.Subversion,
                    ChainDomainProfiles.Testnet4RequiredNodeSubversion,
                    StringComparison.Ordinal))
            {
                return BitcoinAttachedNodeProfileAttestationResult.Reject(
                    $"Attached node subversion does not match the pinned Testnet4 Knots release for {profile.ProfileId}.");
            }
        }
        else if (!evidence.Subversion.Contains("/Knots:", StringComparison.Ordinal))
        {
            return BitcoinAttachedNodeProfileAttestationResult.Reject(
                $"Attached node does not advertise Bitcoin Knots for Blake2b profile {profile.ProfileId}.");
        }

        if (evidence.ObservedHeight < profile.ActivationHeight)
        {
            return BitcoinAttachedNodeProfileAttestationResult.Accept();
        }

        if (profile.ProfileId == ChainDomainProfiles.Blake2bTestnet4ProfileId &&
            !CanonicalHashEquals(
                evidence.ActivationBlockHash,
                ChainDomainProfiles.Testnet4ActivationBlockHash))
        {
            return BitcoinAttachedNodeProfileAttestationResult.Reject(
                "Attached node Testnet4 activation block does not match the pinned RC3 chain evidence.");
        }

        try
        {
            ParsedChainHeader activation = ChainProfiles.BitcoinBlake2bHeaderV2.ParseAndHash(
                evidence.ActivationHeaderHex);
            ParsedChainHeader predecessor = ChainProfiles.BitcoinSha256dHeaderV1.ParseAndHash(
                evidence.PreActivationHeaderHex);

            if (!CanonicalHashEquals(activation.DisplayBlockHash, evidence.ActivationBlockHash) ||
                !CanonicalHashEquals(activation.DisplayParentBlockHash, predecessor.DisplayBlockHash))
            {
                return BitcoinAttachedNodeProfileAttestationResult.Reject(
                    "Attached node activation headers do not form the pinned 80-to-164-byte chain transition.");
            }

            if (activation.DeclaredHeight != profile.ActivationHeight)
            {
                return BitcoinAttachedNodeProfileAttestationResult.Reject(
                    "Attached node activation header embeds the wrong Blake2b activation height.");
            }

            if (profile.ProfileId == ChainDomainProfiles.Blake2bTestnet4ProfileId &&
                activation.CompactTarget != ChainDomainProfiles.Testnet4ActivationCompactTarget)
            {
                return BitcoinAttachedNodeProfileAttestationResult.Reject(
                    "Attached node Testnet4 activation target does not match the pinned RC3 first-Blake target.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return BitcoinAttachedNodeProfileAttestationResult.Reject(
                $"Attached node activation header evidence is invalid: {ex.Message}");
        }

        return BitcoinAttachedNodeProfileAttestationResult.Accept();
    }

    private static bool CanonicalHashEquals(string actual, string expected) =>
        actual.Length == 64 &&
        actual.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        string.Equals(actual, expected, StringComparison.Ordinal);
}
