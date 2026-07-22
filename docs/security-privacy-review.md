# GridPool Security And Privacy Review

Status: active pre-package launch gate, opened 22 July 2026.

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

Publicly hosting the node UI must not silently convert operator diagnostics into
a network directory. Private mode should be the package default.

## Review Checklist

### Keys And Secrets

- [x] Stop printing long-term Ed25519/X25519 private keys at startup.
- [ ] Scan startup, DATUM, peer, UDP, adapter, and error paths for private keys,
  shared secrets, nonces, tokens, passwords, cookies, RPC URLs, and config dumps.
- [x] Add an automated CI source/config regression check that fails on known
  secret-key logging patterns and non-empty tracked identity/admin secret fields.
- [x] Enforce owner-only permissions on the config file that stores generated or
  loaded node identity keys on Unix.
- [ ] Document and test identity-key backup, restore, and deliberate rotation.
- [ ] Decide whether existing public operators should rotate identities after
  the migration procedure exists.

### Public UI And API

- [ ] Inventory every unauthenticated endpoint and classify fields as public,
  peer-protocol, or operator-only.
- [ ] Add an explicit privacy mode with safe package defaults.
- [ ] In public views, show intentionally advertised DNS names only; redact raw
  IP literals and never show observed/LAN/socket addresses.
- [ ] Keep outbound-only peers endpoint-free in UI, API, address gossip, state
  bundles, telemetry exports, and incident reports.
- [ ] Move sensitive peer/session/NAT diagnostics behind local/admin access or
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
