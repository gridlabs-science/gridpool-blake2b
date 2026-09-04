using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public readonly record struct DatumTemplateDecision(
    long Sequence,
    bool UsesSupportAddress,
    string SlotZeroAddress,
    string PolicyId,
    string ParentBlockHash);

public static class DatumTemplateScheduler
{
    public static DatumTemplateDecision Decide(
        DatumListenerPolicy policy,
        ReadOnlySpan<byte> schedulerKey,
        string chainDomainFingerprint,
        string clientIdentity,
        string payoutAddress,
        string parentBlockHash,
        long sequence)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        string normalizedPayout = BitcoinScript.NormalizeAddress(payoutAddress);
        string normalizedSupport = BitcoinScript.NormalizeAddress(policy.SupportAddress);
        int basisPoints = Math.Clamp(policy.SupportTemplateBasisPoints, 0, 10_000);
        bool usesSupport = false;
        if (basisPoints > 0)
        {
            if (schedulerKey.Length < 32)
            {
                throw new InvalidOperationException("DATUM fee scheduler keys must contain at least 32 bytes.");
            }

            byte[] preimage = Encoding.UTF8.GetBytes(string.Join('\0',
                chainDomainFingerprint,
                policy.PolicyId,
                clientIdentity,
                normalizedPayout,
                parentBlockHash.ToLowerInvariant(),
                sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            byte[] digest = HMACSHA256.HashData(schedulerKey, preimage);
            uint draw = BinaryPrimitives.ReadUInt32BigEndian(digest);
            usesSupport = (ulong)draw * 10_000UL < (ulong)basisPoints * (1UL << 32);
        }

        return new DatumTemplateDecision(
            sequence,
            usesSupport,
            usesSupport ? normalizedSupport : normalizedPayout,
            policy.PolicyId,
            parentBlockHash.ToLowerInvariant());
    }
}
