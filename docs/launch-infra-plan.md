# Boot Launch Infrastructure Rollout Plan

## Purpose

This document defines the first launch-like infrastructure rollout for Boot / Grid Anti-Pool.

It is designed to be:

- close enough to real launch topology that testing becomes more meaningful
- simple enough to operate from this repo and this Codex session
- explicit about what is manual versus what can be automated here

This is not the final global architecture. It is the first stable public seed/relay cluster.

## Core Infrastructure Decision

Use:

- `3` public cloud nodes for the initial launch-like environment
- `1` home machine kept as `dev/staging`
- `Tailscale` for admin access only
- public DNS hostnames for Boot peer traffic and DATUM miner traffic
- `Cloudflare DNS` for all records
- `Cloudflare proxy` only for the human-facing UI entrypoint, not for DATUM or peer ingress

Do not use:

- Tailscale IPs in peer bootstrap or public miner connection instructions
- home hardware as part of the initial production seed set
- Cloudflare proxy in front of DATUM TCP ingress
- automatic geo-routing of miners between nodes at first launch

## Why This Topology

This plan matches the current product behavior and constraints:

- local hashrate is node-local, team hashrate is network-wide
- if users are silently routed between nodes, the local-vs-team story becomes confusing
- peer and miner debugging is much easier when each node has its own stable public hostname
- Cloudflare only proxies a limited set of HTTP/HTTPS ports by default; arbitrary TCP needs Spectrum
- DATUM currently expects a stable, explicit upstream target, not an opaque traffic manager

## Initial Target Topology

### Node Roles

Initial public cluster:

1. `use1`
   - region: `Ashburn`
   - role: full-stack public node
   - runs:
     - Boot
     - DATUM
     - bitcoind
2. `usw1`
   - region: `Hillsboro`
   - role: full-stack public node
   - runs:
     - Boot
     - DATUM
     - bitcoind
3. `euw1`
   - region: `Falkenstein` or `Helsinki`
   - role: public seed / relay node
   - runs:
     - Boot
     - local bitcoind preferred
     - DATUM optional at first

Current home machine:

4. `dev`
   - role: staging / experimental / recovery
   - not part of the production seed set

Optional phase-2 public node:

5. `ap1`
   - region: `Singapore`
   - role: public full-stack or relay node

### Recommended Provider

Primary recommendation:

- `Hetzner Cloud`

Initial location mapping:

- `use1` -> `ash`
- `usw1` -> `hil`
- `euw1` -> `fsn1` or `hel1`
- `ap1` later -> `sin`

Alternative provider:

- `DigitalOcean`

Alternative location mapping:

- `use1` -> `nyc3`
- `usw1` -> `sfo3`
- `euw1` -> `ams3` or `fra1`
- `ap1` later -> `sgp1`

## Concrete Shopping List

This section converts the topology recommendation into a buying decision.

### Recommended Hetzner Shopping List

For the first public seed-only cluster, buy:

1. `use1`
   - provider: `Hetzner`
   - region: `Ashburn`
   - plan: `CPX21`
   - current price: about `$13.99/mo`
2. `usw1`
   - provider: `Hetzner`
   - region: `Hillsboro`
   - plan: `CPX21`
   - current price: about `$13.99/mo`
3. `euw1`
   - provider: `Hetzner`
   - region: `Falkenstein` or `Helsinki`
   - plan: `CAX21`
   - current price: about `$9.49/mo`
4. `ap1` optional
   - provider: `Hetzner`
   - region: `Singapore`
   - plan: `CPX22`
   - current price: about `$18.49/mo`

Current total:

- `3 nodes` without AP:
  - about `$37.47/mo`
  - about `$449.64/year`
- `4 nodes` with AP:
  - about `$55.96/mo`
  - about `$671.52/year`

### Why These Plans

Reasoning:

- US nodes are more expensive on Hetzner, so `CPX21` is the practical seed-only recommendation there
- EU has a better price/performance option in `CAX21`
- Singapore is materially more expensive, so it stays optional in phase 1
- all of these are enough for:
  - Boot
  - reverse proxy
  - Tailscale
  - logs
  - monitoring
  - light future expansion

### DigitalOcean Comparison

Closest simple comparison:

- `Basic Droplet`
- `4 GiB RAM`
- `80 GiB storage`
- `4,000 GiB bandwidth`
- current price: `$24/mo` per node

Equivalent DigitalOcean totals:

- `3 nodes` without AP:
  - `$72/mo`
  - `$864/year`
- `4 nodes` with AP:
  - `$96/mo`
  - `$1,152/year`

Practical interpretation:

- DigitalOcean is still reasonable, but materially more expensive for this seed-only use case
- Hetzner remains the better default unless you strongly prefer DigitalOcean’s ecosystem

### Payment Method Comparison

Hetzner:

- accepts:
  - credit card
  - SEPA direct debit
  - bank / wire transfer
  - PayPal
- does **not** accept cryptocurrencies

DigitalOcean:

- accepts:
  - credit / debit cards
  - PayPal and other third-party providers
  - crypto wallets using stablecoin payments
  - ACH direct debit for qualifying customers
- does **not** document direct Bitcoin payment support

Current practical answer:

- if you specifically want to pay in BTC, neither provider currently looks like a clean direct-Bitcoin fit
- DigitalOcean does support crypto-wallet payments, but the official docs describe this as stablecoin payments, not Bitcoin

## Domain Shortlist

Checked on `2026-04-19` via WHOIS / DNS lookup:

### Taken

- `gridpool.com`
  - registered
  - currently listed through HugeDomains / NameBright

### Appears Available

- `gridpool.io`
- `gridpool.org`
- `gridpool.net`
- `gridpool.science`
- `gridantipool.com`
- `gridantipool.io`
- `gridantipool.org`
- `gridantipool.net`
- `gridantipool.science`

Notes:

- availability was checked via WHOIS and DNS, but final registrability should still be confirmed in the registrar checkout flow
- `gridpool.com` is the only clearly unavailable option from this first shortlist

### Domain Cost Guidance

Recommended registrar assumption:

- `Cloudflare Registrar`

Reason:

- you already use Cloudflare
- Cloudflare charges registry + ICANN cost with no markup

Current price guidance:

- `.com`
  - about `$10.46/year`
  - based on current Verisign registry fee plus current ICANN fee
- `.net`
  - about `$11.11/year`
  - based on current registry fee plus ICANN fee
- `.org`
  - roughly `$10-11/year`
  - exact current price should be confirmed in Cloudflare search
- `.io`
  - expensive
  - expect roughly `mid-$40s/year`
  - exact current price should be confirmed in Cloudflare search
- `.science`
  - exact current price not verified here
  - likely cheap, but confirm in registrar search

### Naming Recommendation

Best short option from the checked shortlist:

- `gridpool.io`

Best descriptive option from the checked shortlist:

- `gridantipool.com`

Conservative low-cost option:

- `gridpool.org`

Lowest-friction option:

- keep `gridlabs.science` for infrastructure now
- reserve one cleaner public brand domain now
- migrate the landing page first, then node hostnames later if desired

## Naming And DNS Plan

### Public Hostnames

Use explicit, stable per-node hostnames.

Human-facing UI hostnames:

- `boot.gridlabs.science`
- `use1.boot.gridlabs.science`
- `usw1.boot.gridlabs.science`
- `euw1.boot.gridlabs.science`

DATUM hostnames:

- `datum-use1.gridlabs.science`
- `datum-usw1.gridlabs.science`
- `datum-euw1.gridlabs.science`

Optional future AP node:

- `ap1.boot.gridlabs.science`
- `datum-ap1.gridlabs.science`

### DNS / Proxy Policy

Use `Cloudflare DNS` for every record.

Proxy policy:

- `boot.gridlabs.science`
  - may be `proxied`
  - intended for human UI landing page only
- `use1.boot.gridlabs.science`
  - `DNS only`
- `usw1.boot.gridlabs.science`
  - `DNS only`
- `euw1.boot.gridlabs.science`
  - `DNS only`
- all `datum-*` records
  - `DNS only`

Reason:

- Boot peer sync and DATUM mining ingress should hit stable, explicit origin nodes
- the public UI can later be separated or fronted differently without changing miner endpoints

## UI Routing Decision

Do not geo-route users between node UIs yet.

Use this model instead:

- `boot.gridlabs.science` becomes a node directory / landing page
- users explicitly choose a node
- node-specific UIs remain directly accessible

This is a better fit for the current product because:

- local hashrate is node-specific
- local miner shortcuts are node-specific
- a user should know exactly which node their DATUM is connected to

### Planned Future Improvement

After the initial public cluster is stable, build a first-visit node selector:

- list view first
- map view later
- nodes can advertise approximate region, not exact address
- users choose a node explicitly
- selected node can be remembered in local storage

That future work belongs primarily in [ui-modes-plan.md](/home/keegreil/Documents/GitHub/boot-protocol/docs/ui-modes-plan.md), not in the first infra rollout.

## Access And Operations Model

### Management Path

Admin access model:

- install `Tailscale` on every public node
- enable `Tailscale SSH`
- keep public SSH closed
- use Tailscale only for admin access and internal troubleshooting

Public traffic model:

- Boot UI over public DNS
- Boot peer sync over public DNS
- DATUM over public DNS

### Current Automation Available In This Repo

Existing helpers:

- [update_server.sh](/home/keegreil/Documents/GitHub/boot-protocol/update_server.sh)
- [boot-main.sh](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-main.sh)
- [boot-laptop.sh](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-laptop.sh)

These are not yet generalized for a multi-node public fleet, but they are enough to bootstrap the pattern.

## Ownership Legend

- `YOU`: requires console accounts, billing, or secrets you control
- `CODEX`: I can do this directly once the host exists and is reachable
- `BOTH`: you do the account/secret side, I do the machine/repo side

## Rollout Phases

## Phase 0: Decisions And Accounts

Goal:

- lock the initial cluster shape before provisioning

Steps:

1. Choose cloud provider.
   - owner: `YOU`
   - recommendation: `Hetzner Cloud`
   - done when:
     - provider account exists
     - billing is enabled
     - you can create at least `3` Linux VPS instances

2. Confirm initial node set.
   - owner: `YOU`
   - target:
     - `use1`
     - `usw1`
     - `euw1`
   - done when:
     - you commit to `3` nodes for phase 1

3. Decide whether `euw1` includes DATUM immediately.
   - owner: `YOU`
   - recommendation:
     - `Boot + bitcoind` at minimum
     - DATUM optional on first pass
   - done when:
     - role is written down for `euw1`

4. Keep `boot.gridlabs.science` as the human entrypoint only.
   - owner: `YOU`
   - recommendation:
     - yes
   - done when:
     - you accept the landing-page / directory model

## Phase 1: Provision The Public Nodes

Goal:

- create stable, publicly addressable Linux servers

Recommended spec per node:

- Ubuntu `24.04 LTS`
- `4 vCPU`
- `8 GB RAM`
- `160+ GB SSD` for full-stack nodes
- `80+ GB SSD` minimum for seed-only node if bitcoind is separate

Conservative recommendation:

- full-stack nodes:
  - `8 GB RAM`, `160-320 GB SSD`
- relay-only node:
  - `4 GB RAM`, `80-160 GB SSD`

Steps:

1. Create `use1`.
   - owner: `YOU`
   - region:
     - Hetzner `ash`
   - name:
     - `boot-use1`

2. Create `usw1`.
   - owner: `YOU`
   - region:
     - Hetzner `hil`
   - name:
     - `boot-usw1`

3. Create `euw1`.
   - owner: `YOU`
   - region:
     - Hetzner `fsn1` or `hel1`
   - name:
     - `boot-euw1`

4. Record public IPv4, internal provider metadata, and root access method.
   - owner: `YOU`
   - done when:
     - a simple table exists with hostname, region, IP, intended role

## Phase 2: Base OS Hardening

Goal:

- make each node safe enough for persistent public service

Steps:

1. Create a normal admin user with sudo.
   - owner: `BOTH`
   - you create initial login path if needed
   - I can do user setup once I can reach the host

2. Disable password SSH if provider enabled it.
   - owner: `BOTH`

3. Install baseline packages.
   - owner: `CODEX`
   - packages:
     - `git`
     - `curl`
     - `jq`
     - `rsync`
     - `ufw`
     - `tmux`
     - `fail2ban`
     - `dotnet` runtime / SDK as needed

4. Enable automatic security updates.
   - owner: `CODEX`

5. Configure host firewall.
   - owner: `CODEX`
   - allow:
     - Tailscale
     - `80/443` for UI / reverse proxy
     - DATUM port `3008`
     - bitcoind ports only if intentionally public
   - deny:
     - public SSH if Tailscale SSH is in use

6. Verify reboots come back clean.
   - owner: `CODEX`
   - success criteria:
     - node reachable again after reboot
     - firewall still correct

## Phase 3: Tailscale Management Plane

Goal:

- make all nodes reachable to this session without exposing SSH publicly

Steps:

1. Install Tailscale on each node.
   - owner: `CODEX`

2. Join each node to your tailnet.
   - owner: `YOU`
   - because this requires your auth / approval

3. Enable Tailscale SSH on each node.
   - owner: `BOTH`
   - you handle policy if needed
   - I can validate and use it

4. Add stable SSH aliases on this machine.
   - owner: `CODEX`
   - target aliases:
     - `boot-use1`
     - `boot-usw1`
     - `boot-euw1`

5. Verify direct admin access from this session.
   - owner: `CODEX`
   - success criteria:
     - `ssh boot-use1 hostname`
     - `ssh boot-usw1 hostname`
     - `ssh boot-euw1 hostname`
     - all succeed

## Phase 4: Cloudflare DNS Setup

Goal:

- publish stable node-specific public endpoints

Steps:

1. Create `A` records for node-specific Boot UI / peer endpoints.
   - owner: `YOU`
   - records:
     - `use1.boot.gridlabs.science`
     - `usw1.boot.gridlabs.science`
     - `euw1.boot.gridlabs.science`
   - mode:
     - `DNS only`

2. Create `A` records for DATUM ingress.
   - owner: `YOU`
   - records:
     - `datum-use1.gridlabs.science`
     - `datum-usw1.gridlabs.science`
     - `datum-euw1.gridlabs.science`
   - mode:
     - `DNS only`

3. Decide what `boot.gridlabs.science` points to during phase 1.
   - owner: `YOU`
   - recommendation:
     - point it to one chosen public node or a simple landing page host
   - mode:
     - `proxied` if this is UI only

4. Verify public DNS resolution.
   - owner: `CODEX`
   - success criteria:
     - all new records resolve correctly

## Phase 5: Reverse Proxy And Public Web Entry

Goal:

- expose the WebUI cleanly on `443`

Steps:

1. Install `Caddy` or `nginx` on each node.
   - owner: `CODEX`
   - recommendation:
     - `Caddy` for simpler TLS and config

2. Put Boot WebUI behind the reverse proxy.
   - owner: `CODEX`
   - proxy target:
     - local Boot HTTP port `5000`

3. Ensure node-specific UIs are public over `443`.
   - owner: `CODEX`
   - success criteria:
     - `https://use1.boot.gridlabs.science`
     - `https://usw1.boot.gridlabs.science`
     - `https://euw1.boot.gridlabs.science`
     - all load

4. Keep DATUM on direct TCP, not behind Cloudflare proxy.
   - owner: `BOTH`
   - no special web routing needed

## Phase 6: Repo Checkout And Deploy Standardization

Goal:

- make all public nodes manageable from the repo, not by manual shell drift

Steps:

1. Clone the repo onto each node.
   - owner: `CODEX`

2. Put runtime secrets in `boot_portal_config.local.json`.
   - owner: `BOTH`
   - you provide real secrets
   - I can install them once provided

3. Standardize deployment path.
   - owner: `CODEX`
   - recommendation:
     - systemd publish layout on Linux nodes
   - target:
     - replace ad hoc per-host differences

4. Generalize deploy helpers.
   - owner: `CODEX`
   - desired future scripts:
     - `scripts/boot-node.sh <alias> status`
     - `scripts/boot-node.sh <alias> logs`
     - `scripts/boot-node.sh <alias> update`

5. Verify update flow on all nodes.
   - owner: `CODEX`
   - success criteria:
     - one command path to pull, publish, restart, and verify

## Phase 7: Bitcoin Node Placement

Goal:

- remove the current fragile dependence on home-machine-only infrastructure

Recommended minimum for phase 1:

- `use1`: local bitcoind
- `usw1`: local bitcoind
- `euw1`: local bitcoind preferred, but can start as relay-only if needed

Steps:

1. Install and sync bitcoind on `use1`.
   - owner: `BOTH`
   - you may need to confirm disk sizing
   - I can handle install/config if host access is ready

2. Install and sync bitcoind on `usw1`.
   - owner: `BOTH`

3. Install and sync bitcoind on `euw1` if resources permit.
   - owner: `BOTH`

4. Prefer local ZMQ notifications over mempool.space on public full-stack nodes.
   - owner: `CODEX`
   - reason:
     - closer to launch behavior
     - removes a known external dependency from the hot path

5. Verify node sync and ZMQ visibility.
   - owner: `CODEX`
   - success criteria:
     - bitcoind fully synced
     - Boot sees local block notifications

## Phase 8: Boot Node Deployment

Goal:

- create the first stable public Boot peer network

Configuration model:

- each node has its own `public_base_url`
- each node has its own `datum_public_host`
- `bootstrap_peers` contains the other public nodes
- peer sync enabled on all public nodes

Suggested mapping:

- `use1`
  - `public_base_url = https://use1.boot.gridlabs.science`
  - `datum_public_host = datum-use1.gridlabs.science`
- `usw1`
  - `public_base_url = https://usw1.boot.gridlabs.science`
  - `datum_public_host = datum-usw1.gridlabs.science`
- `euw1`
  - `public_base_url = https://euw1.boot.gridlabs.science`
  - `datum_public_host = datum-euw1.gridlabs.science`

Steps:

1. Generate unique Boot keys per node.
   - owner: `CODEX`
   - success criteria:
     - no key reuse across nodes

2. Install node-local configs.
   - owner: `BOTH`
   - you approve secrets / DNS values
   - I write and validate config files

3. Start Boot on `use1`.
   - owner: `CODEX`

4. Start Boot on `usw1`.
   - owner: `CODEX`

5. Start Boot on `euw1`.
   - owner: `CODEX`

6. Verify peer convergence.
   - owner: `CODEX`
   - tools:
     - [boot-g2-monitor.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-g2-monitor.mjs)
     - [boot-history-check.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-history-check.mjs)
     - [boot-soak-report.mjs](/home/keegreil/Documents/GitHub/boot-protocol/scripts/boot-soak-report.mjs)

## Phase 9: DATUM Deployment

Goal:

- move miner testing to public nodes in a launch-like shape

Recommended phase-1 DATUM placement:

- DATUM on `use1`
- DATUM on `usw1`
- DATUM optional on `euw1`

Steps:

1. Install DATUM on `use1`.
   - owner: `BOTH`

2. Install DATUM on `usw1`.
   - owner: `BOTH`

3. Apply current preferred settings.
   - owner: `CODEX`
   - target values:
     - `pooled_mining_only = false`
     - `work_update_seconds = 5`
     - `vardiff_target_shares_min = 150`
     - `vardiff_min` set coherently with Boot `min_diff`

4. Point DATUM at the local Boot node on each host.
   - owner: `CODEX`

5. Test real miner connectivity.
   - owner: `YOU`
   - because you control the physical miners

6. Verify acceptance and post-rotation behavior.
   - owner: `CODEX`
   - success criteria:
     - sustained high accept rate
     - only short normal reject bursts around real tip changes / round rotation

## Phase 10: Public UI Entry Point

Goal:

- expose a user-friendly front door without hiding node identity

Phase-1 recommendation:

- `boot.gridlabs.science` should not be an automatic node router
- it should be a directory / landing page

Landing page should contain:

- short product intro
- node list
- region labels
- direct links to each node UI
- DATUM connection hostnames
- copy/paste snippets

Future phase:

- add map-based node selection
- allow publicly accessible nodes to advertise approximate region
- let users save a preferred node

Owner:

- `BOTH`
- you decide product/branding direction
- I can implement the page and node metadata plumbing

## Phase 11: Cutover From Home-Centric Testing

Goal:

- stop depending on the current home-network peer topology

Steps:

1. Move peer bootstrap lists to public DNS nodes.
   - owner: `CODEX`

2. Remove Tailscale IPs from peer config.
   - owner: `CODEX`

3. Move soak testing to public nodes.
   - owner: `CODEX`

4. Keep home dev machine as staging.
   - owner: `YOU`

5. Run `G2` again in the new environment.
   - owner: `CODEX`

## Phase 12: Launch Readiness Verification

Goal:

- declare the new cluster good enough for beta-style external use

Checklist:

1. All public nodes reachable by stable public DNS.
   - owner: `CODEX`

2. All public nodes reachable by Tailscale SSH for admin.
   - owner: `CODEX`

3. Boot peer sync stable with no Tailscale dependency.
   - owner: `CODEX`

4. Coinbaser hot path healthy on all public DATUM nodes.
   - owner: `CODEX`

5. `G2` soak passes on public cluster.
   - owner: `CODEX`

6. Node-specific UI and DATUM endpoints documented.
   - owner: `BOTH`

7. `boot.gridlabs.science` clearly communicates node selection.
   - owner: `BOTH`

## What You Can Hand Off To Me Quickly

Once the VPS instances exist and I have Tailscale SSH or another stable SSH path, I can handle:

- base Linux package install
- firewall setup
- reverse proxy setup
- repo checkout
- Boot deploy
- config validation
- service setup
- DNS verification
- peer bootstrap config
- monitoring and soak runs
- log inspection and restarts
- generalized deploy/helper script work

What still requires you:

- cloud account creation and billing
- Cloudflare dashboard actions
- Tailscale approvals / policy when needed
- physical miner retargeting
- final UI / branding decisions

## Recommended Immediate Next Moves

Do these next, in order:

1. Provision `3` public VPS nodes.
2. Put Tailscale on them and let me verify SSH access.
3. Create the explicit `use1/usw1/euw1` DNS records.
4. Let me standardize the Linux deploy path on those nodes.
5. Move Boot peer traffic to public DNS hostnames.
6. Re-run `G2` in that environment before doing more protocol surgery.
