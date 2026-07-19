using boot_portal.Models;

namespace boot.tests;

[TestClass]
public sealed class SnapshotReconciliationTests
{
    [TestMethod]
    public void FamilyIdHasStableCanonicalVectorAndIsolatesEveryConsensusField()
    {
        string id = BootSnapshotReconciliation.ComputeFamilyId(
            22,
            "testnet",
            "snapshot-a",
            "00000000000000000000000000000000000000000000000000000000000000ab",
            100,
            "fee-free:shared=2:snapshot=2:reserve=6");

        Assert.AreEqual("6127d0d6ee591f1358584ef9d48fe86a9f1b5f672e011a8b926633c5db9f2a86", id);

        string[] isolated =
        [
            BootSnapshotReconciliation.ComputeFamilyId(21, "testnet", "snapshot-a", new string('0', 62) + "ab", 100, "fee-free:shared=2:snapshot=2:reserve=6"),
            BootSnapshotReconciliation.ComputeFamilyId(22, "mainnet", "snapshot-a", new string('0', 62) + "ab", 100, "fee-free:shared=2:snapshot=2:reserve=6"),
            BootSnapshotReconciliation.ComputeFamilyId(22, "testnet", "snapshot-b", new string('0', 62) + "ab", 100, "fee-free:shared=2:snapshot=2:reserve=6"),
            BootSnapshotReconciliation.ComputeFamilyId(22, "testnet", "snapshot-a", new string('0', 62) + "ac", 100, "fee-free:shared=2:snapshot=2:reserve=6"),
            BootSnapshotReconciliation.ComputeFamilyId(22, "testnet", "snapshot-a", new string('0', 62) + "ab", 101, "fee-free:shared=2:snapshot=2:reserve=6"),
            BootSnapshotReconciliation.ComputeFamilyId(22, "testnet", "snapshot-a", new string('0', 62) + "ab", 100, "gridlabs-support-v1:shared=1:snapshot=2:reserve=6")
        ];

        Assert.IsTrue(isolated.All(candidate => candidate != id));
        Assert.AreEqual(isolated.Length, isolated.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void ProofUnionIsIdempotentCommutativeAssociativeAndCanonicallyRanked()
    {
        BootShareProof a = Proof("a", 10);
        BootShareProof b = Proof("b", 20);
        BootShareProof c = Proof("c", 20);
        BootShareProof d = Proof("d", 5);

        List<BootShareProof> ab = BootSnapshotReconciliation.Reconcile([a], [b], [], 4);
        List<BootShareProof> ba = BootSnapshotReconciliation.Reconcile([b], [a], [], 4);
        CollectionAssert.AreEqual(Ids(ab), Ids(ba));

        List<BootShareProof> idem = BootSnapshotReconciliation.Reconcile(ab, ab, [], 4);
        CollectionAssert.AreEqual(Ids(ab), Ids(idem));

        List<BootShareProof> left = BootSnapshotReconciliation.Reconcile(
            BootSnapshotReconciliation.Reconcile([a], [b], [], 4),
            [c, d],
            [],
            4);
        List<BootShareProof> right = BootSnapshotReconciliation.Reconcile(
            [a],
            BootSnapshotReconciliation.Reconcile([b], [c, d], [], 4),
            [],
            4);
        CollectionAssert.AreEqual(Ids(left), Ids(right));
        CollectionAssert.AreEqual(new[] { "b", "c", "a", "d" }, Ids(left));
    }

    [TestMethod]
    public void FamilyMatchRejectsDifferentPredecessorBoundaryNetworkHeightAndPayoutVariant()
    {
        BootSnapshotFamilyMember member = Member("network-a", "predecessor-a", "aa", 100, "variant-a");
        var family = new BootSnapshotFamilyState
        {
            FamilyId = member.FamilyId,
            ConsensusVersion = member.ConsensusVersion,
            NetworkId = member.NetworkId,
            PredecessorSnapshotId = member.PredecessorSnapshotId,
            BoundaryBlockHash = member.BoundaryBlockHash,
            BoundaryBlockHeight = member.BoundaryBlockHeight,
            PayoutVariant = member.PayoutVariant
        };

        Assert.IsTrue(BootSnapshotReconciliation.MatchesFamily(family, member));
        BootSnapshotFamilyMember[] isolated =
        [
            Member("network-b", "predecessor-a", "aa", 100, "variant-a"),
            Member("network-a", "predecessor-b", "aa", 100, "variant-a"),
            Member("network-a", "predecessor-a", "bb", 100, "variant-a"),
            Member("network-a", "predecessor-a", "aa", 101, "variant-a"),
            Member("network-a", "predecessor-a", "aa", 100, "variant-b")
        ];
        Assert.IsTrue(isolated.All(candidate => !BootSnapshotReconciliation.MatchesFamily(family, candidate)));
    }

    [TestMethod]
    public void OmissionCannotRemoveProofsAndPaidIdsAreRemovedExactlyOnce()
    {
        List<BootShareProof> known = [Proof("a", 30), Proof("b", 20), Proof("c", 10)];
        List<BootShareProof> omitted = BootSnapshotReconciliation.Reconcile(known, [Proof("a", 30)], [], 3);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(omitted));

        List<BootShareProof> paid = BootSnapshotReconciliation.Reconcile(known, known, ["b", "b"], 3);
        CollectionAssert.AreEqual(new[] { "a", "c" }, Ids(paid));
    }

    [TestMethod]
    public void ThousandsOfOmissionMembersRetainAtMostSixtyFourIds()
    {
        var ids = new List<string>();
        int dropped = 0;
        for (int index = 0; index < 10_000; index++)
        {
            if (!BootSnapshotReconciliation.TryRetainMemberId(ids, $"snapshot-{index}"))
            {
                dropped++;
            }
        }

        Assert.AreEqual(BootSnapshotReconciliation.MaxRetainedMemberSnapshotIds, ids.Count);
        Assert.AreEqual(10_000 - BootSnapshotReconciliation.MaxRetainedMemberSnapshotIds, dropped);
    }

    private static BootShareProof Proof(string id, double difficulty) => new()
    {
        ShareId = id,
        Difficulty = difficulty
    };

    private static string[] Ids(IEnumerable<BootShareProof> proofs) =>
        proofs.Select(proof => proof.ShareId).ToArray();

    private static BootSnapshotFamilyMember Member(
        string network,
        string predecessor,
        string boundarySuffix,
        long height,
        string variant)
    {
        string boundary = new string('0', 64 - boundarySuffix.Length) + boundarySuffix;
        return new BootSnapshotFamilyMember
        {
            FamilyId = BootSnapshotReconciliation.ComputeFamilyId(22, network, predecessor, boundary, height, variant),
            ConsensusVersion = 22,
            NetworkId = network,
            PredecessorSnapshotId = predecessor,
            BoundaryBlockHash = boundary,
            BoundaryBlockHeight = height,
            PayoutVariant = variant,
            SnapshotId = "snapshot"
        };
    }
}
