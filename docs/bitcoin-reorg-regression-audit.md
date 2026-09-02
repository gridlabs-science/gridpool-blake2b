# Bitcoin Reorganization Regression Audit

Status: bounded rollback coverage complete; private-soak gate closed.

This audit covers ordinary Bitcoin-chain reorganizations. It does not change
GridPool consensus rules. The attached Bitcoin node remains authoritative, and
peer-header notifications cannot activate snapshots or payment transitions.

## Current Results

| Scenario | Result | Regression coverage |
| --- | --- | --- |
| Same-height, one-block replacement before a GridPool payment | Supported. The removed boundary family is closed, its predecessor snapshot is restored, the replacement family is isolated, unpaid reserve proofs remain present, and state/candidate IDs are rebuilt deterministically. | `V22OneBlockReorgCreatesIsolatedReplacementFamilyAndRestoresLineageAsync` |
| Repeated one-block replacement (`A -> B -> A`) | Supported. Snapshot-family, active snapshot, current state, candidate state, round number, and reserve proof membership return to their original `A` values. | `V22RepeatedOneBlockReorgRoundTripsSnapshotAndCandidateIdsWithoutLosingReserveAsync` |
| RPC temporarily reports a lower height | Safe pause only. Reconciliation waits for the replacement chain to catch up rather than applying a lower stale tip. | `LowerRpcHeightPausesUntilReplacementChainCatchesUp` |
| Same-height RPC replacement | Detected and sent through the one-block reorganization path. | `SameHeightReplacementUsesReorganizationPath` |
| Two-block reorganization | Supported within the retained 12-boundary journal. RPC reconciliation locates the common ancestor, restores its exact checkpoint, and replays each replacement boundary in height order. | `V22TwoBlockReorgRollsBackToCommonAncestorAndReplaysExactlyOnceAsync`, `RpcReconciliationFindsCommonAncestorAndReplaysEveryReplacementBlockAsync` |
| Reorganization that orphans GridPool-paying blocks | Supported within the retained journal. Complete pre-payment proofs, snapshot lineage, state IDs, and paid records are restored before replacement replay. Consecutive orphaned payments are covered with support fees enabled and disabled. | `V22OrphanedConsecutivePaymentsRestoreProofsAndPaidLineageAsync` |
| Restart before reorganization | Supported. The bounded transition journal is part of the durable core-state snapshot and restores an orphaned payment after service reconstruction. | `V22BoundaryTransitionJournalSurvivesRestartAndCanRestoreOrphanedPaymentAsync` |
| Reorganization deeper than the retained journal | Fail closed. RPC health records a tip mismatch and mining remains paused for explicit operator recovery. | Bounded-journal safety behavior in `BitcoinRpcReconciliationService` |

## Implemented Safety Contract

1. Common-ancestor discovery in RPC reconciliation replays the active
   replacement chain from `ancestor + 1` through the new tip.
2. A bounded, persisted transition journal for each Bitcoin boundary
   retains the pre-transition active snapshot, Work Set proofs, snapshot
   families, state IDs, and paid-proof records needed for rollback.
3. Orphaned transitions are rolled back to an exact pre-boundary checkpoint,
   then replacement blocks are applied in forward height order.
4. Paid-once lineage is preserved on the active Bitcoin chain: proofs paid only by
   an orphaned block become unpaid again; proofs paid by a surviving block remain
   excluded; replay must be idempotent.
5. Deterministic one- and two-block fixtures cover:
   - no GridPool payment;
   - a GridPool payment in the orphaned tip;
   - consecutive GridPool payments across the orphaned segment;
   - support fee enabled and disabled;
   - restart between the old-chain transition and reorganization;
   - duplicate ZMQ/RPC observations during rollback and replay.

## Launch Checklist Interpretation

The bounded reorganization gate is closed for private soak. Public mining still
requires the separate listener-policy and payout-session isolation gates. A fork
deeper than 12 retained Bitcoin boundaries is deliberately not guessed through:
the node pauses mining and requires operator recovery from an authoritative
checkpoint.
