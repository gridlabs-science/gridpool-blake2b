# GridPool V2.2 Monotonic Snapshot Reconciliation

Status: **Implemented in the reference tree and scheduled for mainnet-beta
activation at Bitcoin height 959500. Deployment is not claimed by this
document.**

This document specifies the deterministic recovery rule implemented by the
reference node for consensus version 22. V2.2-capable nodes retain V2.1 behavior
and do not perform cross-sibling union reconciliation below the configured
activation height. At or above the height, active consensus version 22 performs
MSR. A zero activation height is reserved for tests and explicit labs.

## Abstract

GridPool V2.1 prevents a late previous-parent proof from retroactively
rewriting a node's active payout snapshot. This closes a stale-work takeover
path, but it can leave honest nodes mining incompatible payout snapshots after
a proof races a Bitcoin-block boundary.

V2.2 defines **Monotonic Snapshot Reconciliation** (MSR). Competing snapshots
that descend from the same predecessor and Bitcoin boundary form a snapshot
family. Once a node validates multiple family members, it does not select a
winner by hashrate, total difficulty, peer count, or first arrival. It computes
a deterministic union of their proven unpaid work, deduplicates by proof ID,
ranks the result by achieved difficulty, and derives a reconciled snapshot.

Omitting a proof cannot remove it from the union. Adding a proof requires valid
proof of work. Branch-specific post-boundary work does not vote for a branch;
it remains tagged with its exact payout context and can be considered for the
future unpaid reserve under separate rules.

The objective is rapid self-healing without reintroducing a heaviest-branch
rule.

## 1. Problem Statement

Let nodes Alice and Bob share predecessor snapshot `S` and unpaid reserve `R`.
A proof `p` races the arrival of Bitcoin block `B`:

- Alice receives and validates `p`, then observes `B` and creates snapshot `A`.
- Bob observes `B`, creates snapshot `B2`, then receives `p` too late for V2.1.
- `A` includes `p`; `B2` does not.
- Shares mined against `A` and `B2` commit different coinbase payout suffixes.

V2.1 correctly refuses to rewrite either local boundary from an untrusted peer
timestamp. It does not, however, give established nodes a deterministic way to
make `A` and `B2` compatible again. A miner must abandon one branch, wait for
the differing proof to leave the relevant state, or rely on operator recovery.
Choosing the branch with more subsequent work would recreate a majority-hash
fork-choice rule.

V2.2 must instead satisfy:

1. Honest sibling snapshots converge after their complete proofs propagate.
2. Subsequent branch hashrate does not choose the result.
3. A peer cannot remove known valid work by omission.
4. A peer cannot add work without valid proof of work and payout context.
5. Proofs and payouts remain paid at most once.
6. Memory, bandwidth, and validation remain bounded.
7. A Bitcoin reorganization cannot silently mix incompatible boundary families.

## 2. Terminology

### 2.1 Predecessor Snapshot

The active payout snapshot immediately before the Bitcoin boundary. Its ID is
`predecessor_snapshot_id`.

### 2.2 Boundary

A locally validated Bitcoin block identified by its block hash, height, and
parent hash. The boundary ID is the Bitcoin block hash.

### 2.3 Boundary Reserve

The bounded unpaid Work Set that a node claims it had accepted when it created
a snapshot at a boundary. It contains complete proof lineage, not only payout
addresses.

### 2.4 Sibling Snapshot

A fully validated snapshot that has the same:

- protocol and network identity;
- predecessor snapshot ID;
- Bitcoin boundary block hash and height;
- payout variant and slot-count rules;
- canonical support-fee behavior.

Sibling snapshots may differ only in the valid unpaid proofs present in their
boundary reserves and in the deterministic payout list resulting from those
proofs.

### 2.5 Snapshot Family

The reconciliation domain identified by:

```text
family_id = H(
    protocol_domain ||
    network_id ||
    predecessor_snapshot_id ||
    boundary_block_hash ||
    boundary_height ||
    payout_variant
)
```

The version-22 reference serialization is SHA-256 over the following bytes:

1. UTF-8 domain bytes `gridpool-msr-family-v22`;
2. consensus version as signed 32-bit big-endian;
3. network identity (`network_id|bitcoin=<bitcoin-network>`), predecessor
   snapshot ID, boundary hash, and payout variant as lowercase trimmed UTF-8
   strings, each prefixed by a signed 32-bit big-endian byte length;
4. boundary height as signed 64-bit big-endian.

The canonical test vector is maintained in
`boot.tests/SnapshotReconciliationTests.cs`.

### 2.6 Reconciled Reserve And Snapshot

The reconciled reserve is the highest-ranked unique proofs from the union of
all admitted family-member boundary reserves. The reconciled active snapshot is
the canonical payout list derived from that reserve.

### 2.7 Context Proof

A share proof mined against a specific active snapshot. Its validation context
includes that snapshot ID and complete payout construction data. A context
proof never changes which family member wins because V2.2 has no such winner.

## 3. Consensus Invariants

An implementation of this draft MUST preserve these invariants.

### 3.1 Family Isolation

States with different predecessor snapshot IDs, Bitcoin boundary hashes,
Bitcoin heights, networks, or payout variants MUST NOT be unioned.

### 3.2 Complete Validation

Every admitted proof MUST validate:

- header proof of work and achieved difficulty;
- Bitcoin parent and retained boundary context;
- merkle root and complete coinbase construction;
- slot-0 attribution;
- active snapshot payout suffix;
- proof ID and duplicate status;
- network, protocol, and payout variant.

### 3.3 Monotonic Knowledge

For one family, learning a sibling may add valid proof IDs to the known family
union. It MUST NOT remove a proof ID already validated into that union, except
through a separate confirmed paid-once transition or Bitcoin reorganization
rollback.

### 3.4 Deterministic Ranking

All nodes MUST rank the same proof set identically. The version-22 order is:

1. achieved difficulty descending;
2. canonical proof ID ascending as the tie-break.

The reconciled reserve contains the first `reserve_limit` proofs. The active
shared payout snapshot contains the first applicable shared-slot count after
canonical fee handling.

### 3.5 No Branch Voting

The following MUST NOT affect reconciliation:

- accumulated post-boundary difficulty on a sibling;
- number or identity of peers advertising a sibling;
- first-seen order of sibling snapshots;
- endpoint reputation;
- miner or node count.

### 3.6 Paid Once

A confirmed GridPool block removes exactly the proof IDs paid by its actual
coinbase snapshot. A proof paid on any recognized sibling is paid globally and
MUST NOT remain eligible in the reconciled unpaid reserve.

### 3.7 Bounded State

The canonical reconciled reserve remains bounded by `reserve_limit`, currently
897 by default. Implementations MUST additionally bound retained sibling
contexts, reconciliation generations, payload sizes, and processing work.

## 4. Admission Rules

A remote snapshot MAY enter a known family only if:

1. its family ID recomputes from locally validated fields;
2. its predecessor snapshot is known and valid;
3. its Bitcoin boundary is known and locally validated into the applicable
   chain, or is held provisionally under the existing peer-header mechanism;
4. its complete boundary reserve is supplied or trustlessly retrievable;
5. every proof validates against a retained payout context;
6. its payout outputs recompute from its claimed reserve;
7. its reserve and context counts obey protocol bounds;
8. it does not claim already-paid proof IDs as unpaid.

An omission-only sibling is valid but has no power to remove proofs already in
the union. A sibling containing a new proof can affect reconciliation only if
that proof survives deterministic reserve and payout ranking.

## 5. Reconciliation Function

For family `F`, let `Members(F)` be the locally admitted sibling snapshots and
`Paid(F)` the globally paid proof IDs known to the node.

```text
all_proofs = unique_by_proof_id(
    concatenate(member.boundary_reserve for member in Members(F))
)

unpaid = all_proofs - Paid(F)

reconciled_reserve = take(
    reserve_limit,
    sort(unpaid, difficulty DESC, proof_id ASC)
)

reconciled_snapshot = BuildPayoutSnapshot(
    reconciled_reserve,
    payout_variant,
    support_fee_rule
)
```

Recomputation MUST be idempotent, commutative, and independent of member
arrival order:

```text
reconcile(A, B) = reconcile(B, A)
reconcile(A, A) = reconcile(A)
reconcile(reconcile(A, B), C) = reconcile(A, reconcile(B, C))
```

These properties apply to the proven set union. Trimming is performed only
after union and canonical sorting.

## 6. Formal State Machine

### 6.1 Node State

```text
NodeState = {
    chain_tip,
    predecessor_snapshot,
    active_family,
    family_members,
    family_union,
    reconciled_reserve,
    active_snapshot,
    context_proofs,
    paid_proof_ids,
    provisional_boundaries,
    quarantined_inputs
}
```

### 6.2 States

#### STABLE

Exactly one admitted family member is known and the node mines its active
snapshot.

#### SPLIT_DETECTED

A second valid sibling has been admitted. Mining work already issued against
the old member remains attributable to that context, but the node begins
reconciliation immediately.

#### RECONCILING

The node computes the monotonic family union, derives the reconciled reserve
and snapshot, and advertises a reconciliation bundle.

#### RECONCILED

The node mines the deterministic reconciled snapshot. Additional valid family
members may extend the union and trigger another idempotent recomputation while
the family remains open.

#### PAID

A locally validated GridPool block pays one recognized family snapshot. Its
paid proof IDs are removed globally exactly once. The remaining unpaid proofs
seed the next applicable state.

#### REORG_PENDING

The Bitcoin boundary is no longer on the local active chain. New template
activation pauses while the node rolls back or replays boundary-dependent
state.

### 6.3 Events And Transitions

| Current State | Event | Guard | Action | Next State |
| --- | --- | --- | --- | --- |
| `STABLE` | `SiblingReceived` | Same family; complete validation passes | Add member; union; recompute | `RECONCILING` |
| `STABLE` | `SiblingReceived` | Family differs or validation fails | Reject or quarantine | `STABLE` |
| `RECONCILING` | `RecomputeComplete` | Derived state ID is internally valid | Advertise bundle; issue reconciled work | `RECONCILED` |
| `RECONCILED` | `SiblingReceived` | Same family; adds valid IDs | Monotonic union; recompute | `RECONCILING` |
| `RECONCILED` | `SiblingReceived` | Adds no IDs | Record duplicate/no-op | `RECONCILED` |
| Any active state | `ContextProofReceived` | Exact snapshot context validates | Store as unpaid context proof; do not vote | Same |
| Any active state | `GridPoolBlockValidated` | Coinbase pays recognized family snapshot | Mark paid IDs once; preserve all other valid unpaid IDs | `PAID` |
| Any active state | `BitcoinReorgDetected` | Boundary removed from active chain | Stop activation; restore retained predecessor state | `REORG_PENDING` |
| `REORG_PENDING` | `ReplacementChainValidated` | Deterministic replay succeeds | Create/recover applicable family | `STABLE` or `RECONCILED` |

### 6.4 Transition Pseudocode

```text
on_sibling_received(bundle):
    member = validate_complete_member(bundle)
    if member.family_id != active_family.id:
        quarantine("different family")
        return

    new_ids = member.proof_ids - active_family.union_proof_ids
    active_family.members.add(member.snapshot_id)

    if new_ids is empty:
        record_noop_member(member.snapshot_id)
        return

    for proof in member.boundary_reserve:
        active_family.proofs.put_if_absent(proof.id, proof)

    active_family.proofs.remove_all(paid_proof_ids)
    reconciled_reserve = canonical_top(active_family.proofs, reserve_limit)
    reconciled_snapshot = build_snapshot(reconciled_reserve)
    active_snapshot = reconciled_snapshot
    relay_reconciliation_bundle()
```

```text
on_gridpool_block_validated(block):
    paid_snapshot = validate_recognized_paid_snapshot(block.coinbase)
    newly_paid = paid_snapshot.proof_ids - paid_proof_ids
    paid_proof_ids.add_all(newly_paid)
    active_family.proofs.remove_all(newly_paid)
    context_proofs.remove_all(newly_paid)
    record_paid_lineage(block.hash, paid_snapshot.id, newly_paid)
```

## 7. Honest Boundary Example

1. Alice and Bob begin with proofs `{1..897}`.
2. Proof `898` outranks the payout floor.
3. Alice receives `898` before Bitcoin block `B`; Bob receives it afterward.
4. Alice advertises sibling `A={2..898}`. Bob advertises `B2={1..897}`.
5. Alice validates `B2`; union remains `{1..898}`.
6. Bob validates `A`; union becomes `{1..898}`.
7. Both sort identically, retain the same top 897, and derive the same payout
   snapshot.
8. Neither sibling's subsequent hashrate is consulted.

## 8. Adversarial Cases

### 8.1 Selective Omission

An attacker advertises a sibling missing honest proof `x`. Honest nodes already
know `x`, so monotonic union retains it. The attacker's omission cannot debit
the honest state.

### 8.2 Fabricated Work

A fabricated proof fails proof-of-work, parent, merkle, coinbase, payout, or
lineage validation and is rejected.

### 8.3 Intentional Stale-Proof Insertion

An attacker mines the previous Bitcoin parent after learning the boundary and
claims the resulting proof belonged to its boundary reserve. Peer clocks cannot
disprove this claim.

If the proof is valid and affects the payout ranking, this draft's union rule
would admit it. The attacker performed real work but forfeited its opportunity
to find a current-chain block, slot 0, and transaction fees while mining stale
work. Honest current-parent mining gives the same longer-term reserve
competition without that cost. The possible advantage is only accelerated
inclusion in the already-active snapshot before the next Bitcoin block.

This was the primary economic question for the conditional simulation gate.
The modeled stale-attack expected value was non-positive in the tested cells,
but that result is evidence rather than a protocol guarantee. Valid stale work
admitted through a complete sibling reserve remains a residual risk.

### 8.4 Template Churn

An attacker may reveal valid stale proofs one at a time to force repeated active
snapshot recomputation. Omissions are no-ops, but sufficiently strong additions
can change the payout suffix and invalidate outstanding miner work.

Version 22 reissues a miner template only when the ordered payout proof IDs
change. Reserve-only additions do not activate a template, omission-only
siblings are no-ops, families close on payment or reorg, and retained member IDs
are capped at 64. Sparse high-latency graphs may still churn when successive
valid additions repeatedly cross the payout floor.

### 8.5 Branch Spam

An attacker can create many omission-only variants cheaply at the data layer.
Because they add no proof IDs, they MUST be processed as bounded no-ops and MUST
NOT consume unbounded retained-member storage.

### 8.6 Majority Hashrate

An attacker with more than half of GridPool's hashrate can generate more future
proofs, but those proofs do not select a sibling. They compete normally in the
merged reserve. Majority hashrate alone cannot remove an already-known honest
proof or make an omission-only snapshot canonical.

## 9. Bitcoin Reorganizations

Snapshot families are bound to a Bitcoin block hash, not height alone. If that
block leaves the active Bitcoin chain:

1. stop issuing newly activated work tied to the removed boundary;
2. retain the family and context proofs for audit and possible replay;
3. restore the last snapshot whose boundary remains on the active chain;
4. apply paid-once rollback rules only where Bitcoin confirmation lineage
   requires it;
5. construct a distinct family for the replacement block hash;
6. never union families across the competing Bitcoin blocks.

The reference tree covers one-block replacement-family restoration without
double payment. Two-block replay and confirmation-aware paid-lineage rollback
still need dedicated state vectors.

## 10. Network And API Integration

The reference implementation includes:

- explicit family ID and predecessor snapshot ID;
- explicit complete boundary-reserve proof payloads;
- retained sibling payout contexts;
- reconciliation bundle relay over canonical HTTP/WebSocket;
- counters for sibling admission, union additions, no-op omissions, active
  payout changes, convergence time, and rejected family mismatches;
- hard bounds on family members, proof contexts, and family lifetime.

Compact UDP remains an announcement/fast-proof path and does not activate a
snapshot family. Missing family context uses the complete HTTP/WebSocket state
bundle path. A family-specific compact notice can be added later without making
UDP validation canonical.

## 11. Version-22 Product Decisions

1. Reconciliation may repeat within a Bitcoin interval. Miner templates are
   reissued only when the ordered active payout proof IDs change; reserve-only
   changes do not activate a template.
2. A family remains open until a validated GridPool block pays the boundary or
   its Bitcoin boundary leaves the active chain. At most 64 distinct member
   snapshot IDs are retained. Excess omission-only members are counted and
   discarded.
3. A complete validated sibling boundary reserve may add a post-boundary stale
   proof to the union. Direct ingress of a newly received previous-parent proof
   after local finalization cannot add it to the canonical unpaid reserve.
   Already-known pre-boundary lineage remains valid.
4. Omission-only siblings are bounded no-ops. Telemetry counts sibling
   admissions, union additions, no-ops, dropped no-op members, payout changes,
   convergence, and family mismatches.
5. Post-reconciliation context proofs stay attributable to their exact payout
   snapshot. They can compete in the future unpaid reserve but do not vote for
   a sibling.
6. Paid-once removal uses the payout snapshot proven by the winning block's
   validated coinbase context, not the receiver's current active snapshot.
7. Families are keyed by Bitcoin block hash. The reference node includes a
   one-block replacement-boundary rollback vector. Two-block replay and paid
   confirmation rollback remain follow-up test-vector work; families are never
   unioned across competing hashes.
8. Active consensus version 22 activates MSR. Version 21 keeps V2.1 boundary
   behavior. Mainnet-beta changes active version at Bitcoin height 959500; the
   trusted local tip height controls the gate, and an unknown height fails
   closed to version 21. Version gates reject incompatible peers rather than
   soft-merging their state.

## 12. Required Implementation Tests

- Union is commutative, associative, and idempotent.
- Sibling arrival order produces identical reserve, snapshot, and state IDs.
- Omission-only siblings cannot remove known proofs.
- Duplicate proof IDs count once.
- Equal-difficulty proofs use the canonical ID tie-break.
- Paid IDs are removed exactly once across all siblings.
- Different predecessor, boundary hash, network, or payout variant cannot
  merge.
- Branch-specific current-parent work cannot vote for a sibling.
- Reconciliation converges after an honest one-proof boundary race.
- Reconciliation remains bounded under thousands of omission-only variants.
- Stale-proof insertion and incremental reveal obey the selected churn rule.
- A GridPool block on any recognized sibling preserves unpaid proofs elsewhere.
- One- and two-block Bitcoin reorg vectors replay deterministically.
- State bundle import cannot smuggle a proof from another family.

## 13. Simulation Decision Gates

The accompanying adversarial model should report:

- V2.1 split persistence versus V2.2 convergence time;
- number of active payout-template changes;
- work issued on superseded templates;
- omission-only attack effect;
- valid stale-proof insertion frequency and slot effect;
- stale-work opportunity cost versus accelerated payout value;
- sensitivity to nodes, peer degree, latency, attacker share, pool network
  share, and stale-mining duration;
- branch/member and proof-data bounds.

V2.2 should proceed to runtime test vectors only if honest reconciliation is
fast, omission is harmless, state remains bounded, and stale insertion is not a
positive expected-value strategy under the intended beta operating range.

The conditional simulation gate used for implementation reported convergence
across the modeled variants, omission invariance, and non-positive modeled
stale-attack expected value. Those results are evidence, not a protocol
guarantee. Residual risks include valid stale proofs entering a complete
sibling reserve and template churn in high-latency sparse graphs.
