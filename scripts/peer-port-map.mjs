#!/usr/bin/env node

function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith("--")) continue;
    const key = arg.slice(2);
    const value = argv[i + 1] && !argv[i + 1].startsWith("--") ? argv[++i] : "true";
    args[key] = value;
  }
  return args;
}

function usage() {
  console.error("Usage: GRIDPOOL_ADMIN_KEY=... node scripts/peer-port-map.mjs --url <private-node-url> [--tcp-port 5002] [--udp-port 5001] [--lifetime 3600] [--protocols pcp,nat-pmp]");
  process.exit(1);
}

const args = parseArgs(process.argv.slice(2));
if (!args.url) usage();

const adminKey = process.env.GRIDPOOL_ADMIN_KEY || process.env.BOOT_ADMIN_KEY || "";
if (!adminKey) {
  console.error("Missing GRIDPOOL_ADMIN_KEY or BOOT_ADMIN_KEY.");
  process.exit(1);
}

const endpoint = `${args.url.replace(/\/+$/, "")}/api/network/admin/port-map`;
const body = {
  peerTcpPort: args["tcp-port"] ? Number.parseInt(args["tcp-port"], 10) : undefined,
  peerUdpPort: args["udp-port"] ? Number.parseInt(args["udp-port"], 10) : undefined,
  lifetimeSeconds: args.lifetime ? Number.parseInt(args.lifetime, 10) : 3600,
  protocols: args.protocols ? args.protocols.split(",").map(item => item.trim()).filter(Boolean) : undefined
};

const response = await fetch(endpoint, {
  method: "POST",
  headers: {
    "content-type": "application/json",
    accept: "application/json",
    "X-Boot-Admin-Key": adminKey
  },
  body: JSON.stringify(body)
});

const payload = await response.json().catch(async () => ({ raw: await response.text() }));
if (!response.ok) {
  console.error(`[peer-port-map] failed status=${response.status}`);
  console.error(JSON.stringify(payload, null, 2));
  process.exit(1);
}

console.log(`[peer-port-map] ${payload.summary ?? "ok"}`);
console.log(JSON.stringify(payload, null, 2));
