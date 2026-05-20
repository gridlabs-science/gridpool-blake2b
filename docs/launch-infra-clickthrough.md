# Boot Launch Infra Click-Through Checklist

## Purpose

This is the hands-on companion to [launch-infra-plan.md](docs/launch-infra-plan.md).

It is written for a first-time setup and assumes:

- you will provision the public infrastructure yourself
- once the machines exist and I can reach them, I can handle most of the OS / repo / service work

This guide is optimized for the current recommendation:

- provider: `Hetzner Cloud`
- DNS: `Cloudflare`
- admin access: `Tailscale`
- first cluster:
  - `use1`
  - `usw1`
  - `euw1`
  - optional `ap1` later

## Before You Start

Have these ready:

- access to your `Hetzner` account
- access to your `Cloudflare` account
- access to your `Tailscale` admin account
- your preferred SSH public key on this machine

If you do not already have an SSH public key on this machine:

```bash
ls ~/.ssh/*.pub
```

If needed, generate one:

```bash
ssh-keygen -t ed25519 -C "boot-launch-admin"
```

Then print it:

```bash
cat ~/.ssh/id_ed25519.pub
```

Keep that value available for the Hetzner provisioning step.

## Recommended Initial Buying Decision

Buy these `3` first:

1. `boot-use1`
   - region: `Ashburn`
   - plan: `CPX21`
2. `boot-usw1`
   - region: `Hillsboro`
   - plan: `CPX21`
3. `boot-euw1`
   - region: `Falkenstein` or `Helsinki`
   - plan: `CAX21`

Optional later:

4. `boot-ap1`
   - region: `Singapore`
   - plan: `CPX22`

## Step 1: Create The Hetzner Project

Owner: `YOU`

In the Hetzner Cloud console:

1. Log in.
2. Click `+ New project` if you do not already have a suitable cloud project.
3. Name it something obvious, for example:
   - `boot-public`
4. Open the new project.

Done when:

- you are looking at an empty or mostly empty project dashboard

Hand back to Codex:

- nothing yet

## Step 2: Add Your SSH Key To Hetzner

Owner: `YOU`

In the Hetzner Cloud console:

1. In the left sidebar, open `Security`.
2. Open `SSH Keys`.
3. Click `Add SSH key`.
4. Name it something obvious, for example:
   - `boot-launch-admin`
5. Paste the public key from:

```bash
cat ~/.ssh/id_ed25519.pub
```

6. Save.

Done when:

- your SSH key appears in Hetzner and is selectable during server creation

Hand back to Codex:

- nothing yet

## Step 3: Create A Firewall In Hetzner

Owner: `YOU`

Do this before creating the servers so you can attach it at creation time.

In the Hetzner Cloud console:

1. Open `Firewalls` in the left sidebar.
2. Click `Create Firewall`.
3. Name it:
   - `boot-public-firewall`

Create inbound rules:

1. `TCP` port `80` from `0.0.0.0/0` and `::/0`
2. `TCP` port `443` from `0.0.0.0/0` and `::/0`
3. `TCP` port `3008` from `0.0.0.0/0` and `::/0`

Do **not** open public SSH yet if you plan to use only Tailscale SSH.

If you want a temporary bootstrap SSH hole, add:

4. `TCP` port `22`
   - source: your current home WAN IP only

Leave outbound as default allow unless Hetzner requires explicit entries.

Save the firewall.

Done when:

- the firewall exists
- it contains at least `80`, `443`, and `3008` inbound

Hand back to Codex:

- whether you opened public `22` temporarily or not

## Step 4: Create `boot-use1`

Owner: `YOU`

In the Hetzner Cloud console:

1. Click `Servers`.
2. Click `Create Server`.
3. Set:
   - Location: `Ashburn`
   - Image: `Ubuntu 24.04`
   - Type: `CPX21`
4. Under `SSH keys`, select your key.
5. Under `Firewalls`, attach `boot-public-firewall`.
6. Under `Name`, enter:
   - `boot-use1`
7. Create the server.

Done when:

- the instance is visible in the server list
- it has a public IPv4 address

Record these values in a simple note:

- hostname: `boot-use1`
- public IPv4
- region

## Step 5: Create `boot-usw1`

Owner: `YOU`

Repeat the same process, but set:

- Location: `Hillsboro`
- Type: `CPX21`
- Name: `boot-usw1`

Record:

- hostname: `boot-usw1`
- public IPv4
- region

## Step 6: Create `boot-euw1`

Owner: `YOU`

Repeat again, but set:

- Location: `Falkenstein` or `Helsinki`
- Type: `CAX21`
- Name: `boot-euw1`

Record:

- hostname: `boot-euw1`
- public IPv4
- region

## Step 7: Hand The Provisioning Table Back

Owner: `YOU`

Send me a table like this:

```text
boot-use1  <ip>  ash
boot-usw1  <ip>  hil
boot-euw1  <ip>  fsn1
```

Also tell me:

- whether you temporarily opened public SSH on port `22`
- which SSH key name you attached

At this point I can help with reachability verification.

## Step 8: Add Cloudflare DNS Records

Owner: `YOU`

In Cloudflare:

1. Open your zone:
   - `gridlabs.science`
2. Open `DNS`.
3. Add these records as `A` records:

Boot hostnames:

- `use1.boot` -> `boot-use1 public IPv4`
- `usw1.boot` -> `boot-usw1 public IPv4`
- `euw1.boot` -> `boot-euw1 public IPv4`

DATUM hostnames:

- `datum-use1` -> `boot-use1 public IPv4`
- `datum-usw1` -> `boot-usw1 public IPv4`
- `datum-euw1` -> `boot-euw1 public IPv4`

Set `Proxy status`:

- for all node-specific `boot` hostnames:
  - `DNS only`
- for all `datum-*` hostnames:
  - `DNS only`

Do **not** proxy those records.

Optional:

- leave `gridpool.net` alone for now until the landing-page cutover is ready

Done when:

- all `6` node-specific DNS records exist
- all `6` are `DNS only`

Hand back to Codex:

- nothing yet, unless you want me to verify resolution immediately

## Step 9: Optional Domain Reservation

Owner: `YOU`

This is optional. Do not let it block infrastructure rollout.

Current shortlist that looked available when checked:

- `gridpool.io`
- `gridpool.org`
- `gridpool.net`
- `gridpool.science`
- `gridantipool.com`
- `gridantipool.io`
- `gridantipool.org`
- `gridantipool.net`
- `gridantipool.science`

Recommendation:

- if you want a clean public-facing brand now, reserve it now
- do **not** migrate the infrastructure hostnames yet

If you use Cloudflare Registrar:

- expect `.com/.org/.net` to be straightforward
- `.io` and `.science` should be confirmed in the checkout flow

My practical recommendation:

- keep using `gridlabs.science` for infrastructure
- reserve the future public brand domain now if you want it

## Step 10: Add The New Servers To Tailscale

Owner: `BOTH`

You can do this manually in-console if you prefer, but the most efficient split is:

- you give me temporary SSH reachability
- I install Tailscale
- you complete the auth step in the browser

### Option A: You Want Me To Install Tailscale

Give me one of these:

- temporary public SSH access to each server
- or Hetzner console shell access instructions

Then I will:

- install Tailscale
- run `tailscale up`
- hand you the auth URLs or auth prompts

You will:

- approve each node into your tailnet

### Option B: You Want To Do It Yourself

On each server:

```bash
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up
sudo tailscale set --ssh
```

Then in the Tailscale admin console:

1. Open `Machines`.
2. Confirm all three nodes appear.
3. Disable key expiry for these long-lived infrastructure nodes if you accept that tradeoff.

Done when:

- all three nodes appear in Tailscale
- Tailscale SSH is enabled on all three

Hand back to Codex:

- the three Tailscale hostnames or IPs
- confirmation that Tailscale SSH is enabled

## Step 11: Verify SSH Reachability From This Machine

Owner: `CODEX`

Once you tell me the nodes are in Tailscale, I can:

- test SSH
- add aliases
- take over most of the machine-side rollout

Useful SSH config pattern for you if needed:

```sshconfig
Host boot-use1
  HostName <tailscale-ip-or-magicdns-name>
  User <your-admin-user>

Host boot-usw1
  HostName <tailscale-ip-or-magicdns-name>
  User <your-admin-user>

Host boot-euw1
  HostName <tailscale-ip-or-magicdns-name>
  User <your-admin-user>
```

Done when:

- `ssh boot-use1 hostname`
- `ssh boot-usw1 hostname`
- `ssh boot-euw1 hostname`

all work from this machine

## Step 12: What To Send Me Once You Reach This Point

Owner: `YOU`

When you are done with the provider-side work, send me:

1. The provisioning table:

```text
boot-use1  <public-ip>  <tailscale-name-or-ip>
boot-usw1  <public-ip>  <tailscale-name-or-ip>
boot-euw1  <public-ip>  <tailscale-name-or-ip>
```

2. Whether public SSH `22` is still open or already closed
3. Whether you want `gridpool.net` left alone for now
4. Whether you bought a new public brand domain

After that, I can take over:

- OS package install
- firewall cleanup
- reverse proxy setup
- repo checkout
- Boot deployment
- config generation
- service install
- DNS verification
- peer bootstrap wiring

## Suggested First Execution Session

The fastest first session is:

1. You create the `3` Hetzner servers.
2. You create the `6` Cloudflare DNS records.
3. You either:
   - install Tailscale yourself, or
   - give me temporary SSH access so I can do it
4. You send me the host table.
5. I take over the Linux-side rollout.

## Troubleshooting Notes

### If Hetzner asks about IPv6

Keep the default unless you have a strong reason to customize it.

### If you are unsure whether to choose Falkenstein or Helsinki

Choose `Falkenstein` unless you have a reason to prefer `Helsinki`.

### If you are unsure whether to buy a new domain first

Do not block infra rollout on branding.

Use:

- `gridlabs.science` for infra now
- reserve the better public brand separately if desired

### If Tailscale SSH feels confusing

Do the simplest thing:

1. temporarily allow your home IP to SSH on port `22`
2. let me install Tailscale
3. once Tailscale SSH works, we close public SSH again

That is cleaner than getting stuck on access controls before the nodes even exist.

