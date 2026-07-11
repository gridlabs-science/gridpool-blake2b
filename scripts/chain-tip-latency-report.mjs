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
  console.error("Usage: node scripts/chain-tip-latency-report.mjs --url <base-url> [--window 24h] [--limit 1000]");
  process.exit(1);
}

function percentile(values, p) {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
  return sorted[index];
}

function formatMs(value) {
  return Number.isFinite(value) ? `${value.toFixed(1)} ms` : "--";
}

async function fetchJson(baseUrl, path, query) {
  const url = new URL(`${baseUrl.replace(/\/+$/, "")}${path}`);
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== "") {
      url.searchParams.set(key, String(value));
    }
  }

  const response = await fetch(url, { headers: { accept: "application/json" } });
  if (!response.ok) {
    throw new Error(`${url.toString()} returned ${response.status}`);
  }
  return response.json();
}

const args = parseArgs(process.argv.slice(2));
if (!args.url) usage();

const payload = await fetchJson(args.url, "/api/network/events", {
  window: args.window || "24h",
  limit: args.limit || 1000,
  eventType: "peer-chain-tip"
});

const events = Array.isArray(payload.events) ? payload.events : [];
const latencies = events
  .map(event => Number(event.relayLatencyMs))
  .filter(Number.isFinite);
const bySource = new Map();
for (const event of events) {
  const key = event.remoteEndpoint || event.remoteNodeId || event.source || "unknown";
  const bucket = bySource.get(key) || [];
  if (Number.isFinite(Number(event.relayLatencyMs))) {
    bucket.push(Number(event.relayLatencyMs));
  }
  bySource.set(key, bucket);
}

console.log(`Peer chain-tip latency: ${events.length} event(s), measured=${latencies.length}, window=${payload.windowSeconds ?? "--"}s`);
console.log(`  p50/p95/p99: ${formatMs(percentile(latencies, 50))} / ${formatMs(percentile(latencies, 95))} / ${formatMs(percentile(latencies, 99))}`);
for (const [source, values] of bySource.entries()) {
  console.log(`  ${source}: count=${values.length} p50=${formatMs(percentile(values, 50))} p95=${formatMs(percentile(values, 95))}`);
}
