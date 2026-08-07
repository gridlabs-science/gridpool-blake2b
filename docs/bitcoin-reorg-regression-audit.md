# Bitcoin Reorganization Regression Audit

Status: partial coverage; launch gate remains open.

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
| Two-block reorganization | **Not complete.** The recovery planner compares only the local tip and RPC tip. It does not locate a common ancestor or replay every replacement block from the fork point. Replacing only the equal-height tip can leave the new snapshot linked to an orphaned predecessor family. |
| Reorganization that orphans a GridPool-paying block | **Not complete.** Payment removes full proofs from the unpaid reserve. The persisted paid lineage retains proof IDs, but not a rollback journal containing the complete removed proofs and pre-payment state. The node therefore cannot safely restore those proofs if the Bitcoin block is orphaned. |

## Required Implementation Before Closing The Launch Gate

1. Add common-ancestor discovery to RPC reconciliation and replay the active
   replacement chain from `ancestor + 1` through the new tip.
2. Add a bounded, persisted transition journal for each Bitcoin boundary. It
   must retain the pre-transition active snapshot, Work Set proofs, snapshot
   families, state IDs, and paid-proof records needed for rollback.
3. Roll back orphaned transitions in reverse height order, then apply replacement
   blocks in forward height order.
4. Preserve paid-once lineage on the active Bitcoin chain: proofs paid only by
   an orphaned block become unpaid again; proofs paid by a surviving block remain
   excluded; replay must be idempotent.
5. Add deterministic one- and two-block fixtures covering:
   - no GridPool payment;
   - a GridPool payment in the orphaned tip;
   - consecutive GridPool payments across the orphaned segment;
   - support fee enabled and disabled;
   - restart between the old-chain transition and reorganization;
   - duplicate ZMQ/RPC observations during rollback and replay.

## Launch Checklist Interpretation

The one-block, no-payment path is regression-tested and behaves
deterministically. The broader checklist item must remain open because two-block
ancestry replay and orphaned-payment restoration are consensus-accounting
requirements, not optional telemetry behavior.
