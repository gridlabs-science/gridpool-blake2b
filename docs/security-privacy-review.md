# GridPool Security And Privacy Review

Status: first-party audit complete; external review and deployment hardening
remain open. Opened 22 July 2026, updated 28 July 2026.

This review covers prototype-era information disclosure, secret handling, API
exposure, and private-node behavior. It complements consensus and abuse testing;
it is not a claim that an independent security audit has been completed.

## Immediate Finding: Private Key Logging

The reference node previously printed its long-term Ed25519 and X25519 private
keys to standard output at startup. That behavior is removed in the current
working tree. The combined DATUM **public** key remains safe and necessary to
display.

Operational consequences:

- Treat identities printed into retained system, container, support-bundle, or
  VPS-provider logs as potentially exposed.
- Do not rotate a live node casually: these keys define peer identity, and the
  state file detects unexpected identity changes. Write and test an explicit
  identity-rotation/migration procedure first.
- Limit and purge old logs where operationally appropriate, recognizing that a
  cloud provider may retain copies outside the guest.
- Verify generated key/config files use owner-only permissions and are excluded
  from images, backups shared for support, crash reports, and Git.

## Privacy Model

GridPool must distinguish three kinds of information:

1. **Public node data:** an operator intentionally advertises a dialable DNS
   endpoint and peer capability. This may be shown as a hostname.
2. **Private node data:** an outbound-only node advertises a node ID and
   capability over its encrypted session, but no dialable endpoint, observed IP,
   LAN address, or socket address is published or gossiped.
3. **Operator-only diagnostics:** raw endpoints, observed source IPs, NAT mapping
   details, miner addresses, session failures, and detailed logs may be visible
   to the local authenticated operator but not the unauthenticated public UI.

The living diagram follows the same boundary: the Work Set and Slot 0 expose
their proof IDs, payout addresses, difficulty, and timestamps as public shared
consensus evidence. Peers expose protocol node IDs; only the exact advertised
Dallas, Detroit, Oregon, and `evomining.farted.net` DNS hosts receive public names. All other peer
endpoints, IP addresses, and inferred locations remain operator-only. Public
round-trip latency may control the length of an already-visible anonymous peer
link; it is not accompanied by endpoint or location data.

Bitcoin peer telemetry is anonymous by construction: RPC peer IDs are replaced
with stable process-local salted visual IDs before projection. The UI may expose
peer counts, direction/type, latency, liveness, and network hashrate, but never
Bitcoin peer addresses, bind addresses, user agents, or inferred geography.
Validated local Slot-0 proof history may expose shared proof evidence publicly;
worker names, miner endpoints, source transports, and exact rejection detail do
not cross the operator boundary.

Publicly hosting the node UI must not silently convert operator diagnostics into
a network directory. Private mode should be the package default.

## Review Checklist

### Keys And Secrets

- [x] Stop printing long-term Ed25519/X25519 private keys at startup.
- [x] Complete the first-party source scan of startup, DATUM, peer, UDP, adapter,
  and error paths for private keys, shared secrets, nonces, tokens, passwords,
  cookies, RPC URLs, and config dumps. Keep the automated guard in CI.
- [x] Add an automated CI source/config regression check that fails on known
  secret-key logging patterns and non-empty tracked identity/admin secret fields.
- [x] Enforce owner-only permissions on the config file that stores generated or
  loaded node identity keys on Unix.
- [ ] Document and test identity-key backup, restore, and deliberate rotation.
- [ ] Decide whether existing public operators should rotate identities after
  the migration procedure exists.

### Public UI And API

- [x] Inventory every unauthenticated endpoint and classify fields as public,
  peer-protocol, or operator-only.
- [x] Add safe default public-summary redaction. Raw operator diagnostics require
  an admin key unless the development-only
  `public_operator_diagnostics_enabled` escape hatch is explicitly enabled.
- [x] In public views, show intentionally advertised DNS names only; redact raw
  IP literals and never show observed/LAN/socket addresses.
- [x] Keep outbound-only peers endpoint-free in UI, API, address gossip, state
  bundles, telemetry exports, and incident reports.
- [x] Move sensitive peer/session/NAT diagnostics behind local/admin access or
  return a redacted public DTO.
- [ ] Review CORS, forwarded-header trust, reverse-proxy assumptions, WebSocket
  origin/authentication, admin-key handling, and endpoint rate limits.
- [ ] Verify the compatibility and mining APIs disclose no local filesystem,
  RPC credential, private endpoint, or miner identity beyond their purpose.

### Logs And Diagnostics

- [ ] Classify log events by normal, debug, sensitive-operator, and forbidden.
- [ ] Redact remote miner IPs and payout addresses from default logs where they
  are not required for action.
- [ ] Ensure exception messages cannot echo secret-bearing URLs or config.
- [ ] Sanitize health-monitor incident bundles before sharing or uploading.
- [ ] Document retention and deletion expectations for bare metal, Docker,
  Umbrel, Start9, and VPS deployments.

### Deployment Surface

- [ ] Bind UI/admin services privately by default; expose peer-only and mining
  listeners separately.
- [ ] Verify container users, filesystem permissions, read-only mounts where
  practical, dropped capabilities, and no host Docker socket access.
- [ ] Verify TLS termination and trusted-proxy configuration do not permit
  spoofed client IP or authentication bypass.
- [ ] Produce a minimal port/exposure matrix for public seeds, private home
  nodes, DATUM, SV2, and hosted Stratum gateways.

## Exit Criteria

- No secret or session key material appears in normal/debug startup logs.
- A default Umbrel/Start9 node reveals no public IP, LAN IP, observed source IP,
  or miner identity through unauthenticated UI/API responses.
- Public seed operators can intentionally publish hostnames without exposing
  unrelated management or local-network details.
- Sensitive endpoints are authenticated, local-only, or redacted and have
  bounded request cost.
- Backup/restore and identity rotation are documented and tested.
- A second developer reviews the resulting threat inventory and high-risk code
  paths before package launch.

## Endpoint Classification

| Route group | Classification | Public behavior |
| --- | --- | --- |
| `/health`, `/health/live`, `/health/ready` | public health | Aggregate liveness/readiness only. |
| `/api/network/summary` | public status plus operator extension | Public response preserves protocol, state, tip, aggregate hashrate, and health counters. It removes raw peers, miner lists, internal listener/ZMQ endpoints, detailed faults, RPC errors, and the last block miner address. An admin key returns the full local DTO. |
| `/api/network/peer-addresses`, `/api/peer/*` | peer protocol | Contains only intentionally dialable peer endpoints and protocol-required identity/state data. Endpointless peers are not gossiped. |
| `/api/network/state/*`, `/api/network/history*`, `/api/mining/payouts`, `/api/mining/work-plan`, `/api/mining/share-advice` | consensus/mining protocol | Public by design. Payout addresses and proofs are consensus data and cannot be treated as private operator metadata. |
| `/api/mining/share` | public mining ingress | Rate limited and request-size guarded. |
| `/api/mining/local/*` | local adapter protocol | Requires the local adapter token. |
| `/api/network/local-miners`, `share-diagnostics`, `events`, `peer-relay-latency`, `coinbaser-diagnostics`, `datum-share-responses`, `datum-sessions`, `datum-protocol-events` | operator-only | Returns 404 without an admin key, except in explicitly enabled development diagnostics mode. |
| `/api/network/reachability-test` | privileged operator action | Requires an admin key. This closes the unauthenticated server-side request-forgery/probing surface. |
| `/api/network/admin/*` | privileged operator action | Disabled unless configured and requires a strong admin key in production. |
| `/api/network/reachability-ack` | peer protocol | Accepts only a short-lived registered challenge and exposes no requester address. |
| `/api/compat/summary`, `/compat` | explicit test-lab UI | Disabled by deployment policy outside the firmware compatibility test node; telemetry is intentionally public on that test endpoint. |

## First-Party Findings

### Closed In This Tranche

- **High:** unauthenticated `reachability-test` could make the node issue HTTP
  requests to arbitrary operator-supplied targets. It now requires admin
  authorization.
- **High:** raw DATUM sessions, protocol events, miner summaries, peer relay
  observations, and share diagnostics were unauthenticated. They are now
  operator-only by default.
- **High:** the full network summary exposed raw peer endpoints, local miner
  identities, internal ZMQ/listener endpoints, and detailed fault/error text.
  The default response is now redacted while retaining aggregate health.
- **Medium:** admin-key comparison used ordinary string equality. It now uses a
  fixed-time byte comparison.
- **Medium:** the source regression scan did not cover tracked Bitcoin RPC
  passwords or common credential variables in log calls. Both checks were added.

### Still Open Before One-Click Launch

- Review and constrain WebSocket/SignalR origin and authentication behavior.
- Configure per-node admin keys in the live monitor before deploying diagnostics
  lockdown; the monitor now supports `adminKeyEnv` without storing keys in JSON.
- Audit default log levels and pseudonymize raw remote IPs in peer/UDP logs.
- Complete container/package capability, mount, TLS-proxy, and support-bundle
  review.
- Document and test identity-key backup, restore, and deliberate rotation.
- Obtain second-developer review.
