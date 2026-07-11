#!/usr/bin/env node

const [seedBaseUrl, targetBaseUrl, udpPortText, udpHostText] = process.argv.slice(2);

if (!seedBaseUrl || !targetBaseUrl) {
  console.error("Usage: node scripts/peer-reachability-test.mjs <seed-base-url> <target-base-url> [udp-port] [udp-host]");
  process.exit(1);
}

const endpoint = `${seedBaseUrl.replace(/\/+$/, "")}/api/network/reachability-test`;
const udpPort = udpPortText ? Number.parseInt(udpPortText, 10) : 0;
const body = {
  targetBaseUrl,
  includeUdpProbe: Number.isInteger(udpPort) && udpPort > 0,
  udpPort: Number.isInteger(udpPort) && udpPort > 0 ? udpPort : undefined,
  udpHost: udpHostText || undefined
};

const response = await fetch(endpoint, {
  method: "POST",
  headers: {
    "content-type": "application/json",
    accept: "application/json"
  },
  body: JSON.stringify(body)
});

const text = await response.text();
let payload;
try {
  payload = JSON.parse(text);
} catch {
  payload = text;
}

if (!response.ok) {
  console.error(`[peer-reachability-test] failed status=${response.status}`);
  console.error(typeof payload === "string" ? payload : JSON.stringify(payload, null, 2));
  process.exit(1);
}

const udp = payload.udpProbeAttempted
  ? ` udpHost=${payload.udpHost || udpHostText || "(target host)"} udpSent=${payload.udpProbeSent} udpAck=${payload.udpChallengeAcknowledged}`
  : "";
console.log(`[peer-reachability-test] ${payload.summary ?? "ok"}${udp}`);
console.log(JSON.stringify(payload, null, 2));
