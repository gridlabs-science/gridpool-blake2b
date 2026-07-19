using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace boot_portal.Models;

public sealed class BootSnapshotFamilyMember
{
    public string FamilyId { get; set; } = string.Empty;
    public int ConsensusVersion { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public string PredecessorSnapshotId { get; set; } = string.Empty;
    public string BoundaryBlockHash { get; set; } = string.Empty;
    public long BoundaryBlockHeight { get; set; }
    public string PayoutVariant { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
    public List<BootShareProof> BoundaryReserveProofs { get; set; } = [];
}

public sealed class BootSnapshotFamilyState
{
    public string FamilyId { get; set; } = string.Empty;
    public int ConsensusVersion { get; set; }
    public string NetworkId { get; set; } = string.Empty;
    public string PredecessorSnapshotId { get; set; } = string.Empty;
    public string BoundaryBlockHash { get; set; } = string.Empty;
    public long BoundaryBlockHeight { get; set; }
    public string PayoutVariant { get; set; } = string.Empty;
    public bool IsOpen { get; set; } = true;
    public bool BoundaryOnActiveChain { get; set; } = true;
    public List<string> MemberSnapshotIds { get; set; } = [];
    public List<BootShareProof> ReconciledProofs { get; set; } = [];
    public List<string> PaidProofIds { get; set; } = [];
    public long SiblingAdmissions { get; set; }
    public long UnionAdditions { get; set; }
    public long NoOpAdmissions { get; set; }
    public long DroppedNoOpMembers { get; set; }
    public long PayoutChanges { get; set; }
    public long ConvergenceCount { get; set; }
}

public sealed class BootSnapshotReconciliationCounters
{
    public long SiblingAdmissions { get; set; }
    public long UnionAdditions { get; set; }
    public long NoOpAdmissions { get; set; }
    public long DroppedNoOpMembers { get; set; }
    public long PayoutChanges { get; set; }
    public long ConvergenceCount { get; set; }
    public long FamilyMismatchRejections { get; set; }
}

public static class BootSnapshotReconciliation
{
    public const int MaxRetainedMemberSnapshotIds = 64;
    private static readonly byte[] FamilyDomain = Encoding.UTF8.GetBytes("gridpool-msr-family-v22");

    public static string ComputeFamilyId(
        int consensusVersion,
        string networkId,
        string predecessorSnapshotId,
        string boundaryBlockHash,
        long boundaryBlockHeight,
        string payoutVariant)
    {
        using var stream = new MemoryStream();
        stream.Write(FamilyDomain);
        WriteInt32(stream, consensusVersion);
        WriteString(stream, NormalizeIdentity(networkId));
        WriteString(stream, NormalizeIdentity(predecessorSnapshotId));
        WriteString(stream, NormalizeIdentity(boundaryBlockHash));
        WriteInt64(stream, boundaryBlockHeight);
        WriteString(stream, NormalizeIdentity(payoutVariant));
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static bool MatchesFamily(BootSnapshotFamilyState family, BootSnapshotFamilyMember member)
    {
        return family.ConsensusVersion == member.ConsensusVersion &&
               family.BoundaryBlockHeight == member.BoundaryBlockHeight &&
               EqualsIdentity(family.NetworkId, member.NetworkId) &&
               EqualsIdentity(family.PredecessorSnapshotId, member.PredecessorSnapshotId) &&
               EqualsIdentity(family.BoundaryBlockHash, member.BoundaryBlockHash) &&
               EqualsIdentity(family.PayoutVariant, member.PayoutVariant) &&
               EqualsIdentity(family.FamilyId, member.FamilyId) &&
               EqualsIdentity(member.FamilyId, ComputeFamilyId(
                   member.ConsensusVersion,
                   member.NetworkId,
                   member.PredecessorSnapshotId,
                   member.BoundaryBlockHash,
                   member.BoundaryBlockHeight,
                   member.PayoutVariant));
    }

    public static List<BootShareProof> Reconcile(
        IEnumerable<BootShareProof> knownProofs,
        IEnumerable<BootShareProof> incomingProofs,
        IEnumerable<string> paidProofIds,
        int reserveLimit)
    {
        HashSet<string> paid = paidProofIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return knownProofs
            .Concat(incomingProofs)
            .Where(proof => !string.IsNullOrWhiteSpace(proof.ShareId) && !paid.Contains(proof.ShareId))
            .GroupBy(proof => proof.ShareId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(proof => proof.Difficulty)
                .ThenBy(proof => proof.ShareId, StringComparer.Ordinal)
                .First())
            .OrderByDescending(proof => proof.Difficulty)
            .ThenBy(proof => proof.ShareId, StringComparer.Ordinal)
            .Take(Math.Max(0, reserveLimit))
            .Select(CloneProof)
            .ToList();
    }

    public static bool SameProofIds(IEnumerable<BootShareProof> left, IEnumerable<BootShareProof> right)
    {
        return left.Select(proof => proof.ShareId).SequenceEqual(
            right.Select(proof => proof.ShareId),
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryRetainMemberId(IList<string> memberSnapshotIds, string snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId) ||
            memberSnapshotIds.Contains(snapshotId, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (memberSnapshotIds.Count >= MaxRetainedMemberSnapshotIds)
        {
            return false;
        }

        memberSnapshotIds.Add(snapshotId);
        return true;
    }

    private static string NormalizeIdentity(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static bool EqualsIdentity(string? left, string? right) =>
        string.Equals(NormalizeIdentity(left), NormalizeIdentity(right), StringComparison.Ordinal);

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static BootShareProof CloneProof(BootShareProof proof) => new()
    {
        ShareId = proof.ShareId,
        MinerAddress = proof.MinerAddress,
        Username = proof.Username,
        ScriptPubKeyHex = proof.ScriptPubKeyHex,
        HeaderHex = proof.HeaderHex,
        CoinbaseHex = proof.CoinbaseHex,
        MerklePath = proof.MerklePath.ToList(),
        PayoutSnapshotId = proof.PayoutSnapshotId,
        PrevBlockHash = proof.PrevBlockHash,
        Difficulty = proof.Difficulty,
        DiffString = proof.DiffString,
        Source = proof.Source,
        Timestamp = proof.Timestamp,
        ProofClass = proof.ProofClass,
        RelayStage = proof.RelayStage,
        RelayTtl = proof.RelayTtl,
        TransportReceivedUtc = proof.TransportReceivedUtc,
        StateServiceReceivedUtc = proof.StateServiceReceivedUtc,
        DifficultyCheckedUtc = proof.DifficultyCheckedUtc,
        ValidationCompletedUtc = proof.ValidationCompletedUtc,
        StateMutationCompletedUtc = proof.StateMutationCompletedUtc
    };
}
