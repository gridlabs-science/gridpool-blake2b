# GridPool protocol Current Status and Launch Readiness

## Executive Summary
GridPool protocol is an advanced open-source prototype with live DATUM integration, multi-node peer synchronization, share relay, WebUI tooling, Docker packaging, and active real-world testing across multiple nodes.

The project is best described as being in `late prototype / launch hardening` stage. The core system exists and works. The remaining effort is primarily stabilization, documentation, deployment polish, interoperability completion, and launch operations.

## What Works Today

### Core protocol behavior
- Maintains a shared Winners List and On Deck List
- Accepts and ranks high-difficulty GridPool shares
- Promotes accepted shares into the next round's Winners List
- Supports the rule that a valid Bitcoin block is also a valid GridPool share before round lock

### Mining integration
- Works with DATUM today
- Exposes HTTP endpoints intended for Hydrapool-style integration
- Builds compressed coinbase payout outputs while preserving logical slot accounting

### Node-to-node behavior
- GridPool nodes discover peers and poll each other
- Nodes exchange current state bundles and candidate state bundles
- Accepted shares are relayed across the GridPool network
- Nodes now converge across multiple running instances in active testing

### Validation and operational controls
- Canonical share verification checks header difficulty, merkle path, coinbase payout rules, and accepted parent blocks
- Rate limiting and request guards exist on network and share-ingress endpoints
- Health endpoints and operator-visible peer/network status are implemented
- Docker and systemd deployment paths are both available

### Testing support
- Local manual reset exists as a development tool
- Deterministic test-trigger mode exists for mainnet-shadow testing based on Bitcoin block hashes

## Current Gaps Before Public Launch

### Engineering hardening
- Broader long-duration multi-node testing
- More defensive logging, monitoring, and alerting for public seed-node operation
- Additional edge-case testing around round transitions and network latency
- Continued cleanup of nullability debt and remaining technical rough edges

### Documentation and operator readiness
- End-user install documentation needs to be tightened
- Operator runbook and troubleshooting guide need to be completed
- Public launch instructions and bootstrap guidance need to be finalized

### Interoperability and future work
- Hydrapool support depends in part on upstream acceptance of the integration path
- On-chain GridPool state commitments are planned but not yet embedded, because current miner-side coinbase hooks are limited
- Wider ecosystem compatibility beyond DATUM remains future-scope work

## Launch Readiness Definition
For GridPool protocol to be considered ready for public launch, the following should be true:

- at least several public GridPool nodes can converge on the same Winners List and On Deck state
- DATUM-based miners can join and submit shares reliably
- round rotation works predictably under testing conditions
- Docker and standard Linux deployment are documented and repeatable
- public seed nodes are deployed with monitoring and basic operational support
- the Foundation can join the genesis round using a documented configuration

## Proposed Final Pre-Launch Work

### Phase 1: stabilization
- continue active two-node and multi-node testing
- root out remaining round-transition and stale-share edge cases
- tighten operator logging and diagnostics

### Phase 2: deployment readiness
- finalize Docker and service deployment documentation
- deploy globally distributed seed nodes
- publish a public network status page

### Phase 3: launch support
- prepare genesis Winners List configuration
- assist the 256 Foundation in joining the initial network
- monitor launch and provide immediate post-launch bugfix support

## Risk Assessment

### Reduced risks
- core feasibility risk is low because the prototype is already built and operating
- the project is open source and has already undergone significant real implementation effort
- peer synchronization and live node operation are already functioning

### Remaining risks
- launch stability risk remains until broader testing is completed
- integration and deployment friction may still surface under public use
- privacy and operational posture should be improved before broader adoption

## Funding Use
Additional support would accelerate the final 20% of work that turns a working prototype into dependable public infrastructure:

- part-time engineering time
- seed-node infrastructure
- testing and deployment support
- documentation and launch operations

## Conclusion
GridPool protocol is already a real system. The remaining work is not invention from scratch, but the finishing work required to make it stable, understandable, and launch-ready for sovereign Bitcoin miners and the 256 Foundation.
