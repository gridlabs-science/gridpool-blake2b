#!/usr/bin/env node

import fs from "node:fs/promises";

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
  console.error("Usage: node scripts/chain-tip-latency-report.mjs --url <base-url> [--window 24h] [--limit 5000] [--json FILE]");
  process.exit(1);
}

function percentile(values, p) {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
  return sorted[index];
}

function average(values) {
  return values.length === 0 ? null : values.reduce((sum, value) => sum + value, 0) / values.length;
}

function formatMs(value) {
  return Number.isFinite(value) ? `${value.toFixed(1)} ms` : "--";
}

function normalizeHash(value) {
  return String(value || "").trim().toLowerCase();
}

function eventTimestampMs(event) {
  const value = Date.parse(event.timestampUtc || "");
  return Number.isFinite(value) ? value : null;
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

function summarize(items) {
  const leadValues = items.map(item => item.leadMs).filter(Number.isFinite);
  const payloadValues = items.map(item => Number(item.payloadBytes)).filter(Number.isFinite);
  return {
    count: items.length,
    fasterThanLocalCount: leadValues.filter(value => value > 0).length,
    slowerThanLocalCount: leadValues.filter(value => value < 0).length,
    leadAverageMs: average(leadValues),
    leadP50Ms: percentile(leadValues, 50),
    leadP95Ms: percentile(leadValues, 95),
    leadMinMs: leadValues.length > 0 ? Math.min(...leadValues) : null,
    leadMaxMs: leadValues.length > 0 ? Math.max(...leadValues) : null,
    averagePayloadBytes: average(payloadValues)
  };
}

function summarizeDurations(values) {
  const finite = values
    .filter(value => value !== null && value !== undefined && value !== "")
    .map(Number)
    .filter(Number.isFinite);
  return {
    count: finite.length,
    averageMs: average(finite),
    p50Ms: percentile(finite, 50),
    p95Ms: percentile(finite, 95),
    minMs: finite.length > 0 ? Math.min(...finite) : null,
    maxMs: finite.length > 0 ? Math.max(...finite) : null
  };
}

function groupBy(items, selector) {
  const groups = new Map();
  for (const item of items) {
    const key = selector(item) || "unknown";
    const bucket = groups.get(key) || [];
    bucket.push(item);
    groups.set(key, bucket);
  }
  return groups;
}

const args = parseArgs(process.argv.slice(2));
if (!args.url) usage();

const query = {
  window: args.window || "24h",
  limit: args.limit || 5000
};
const [localPayload, mempoolPayload, peerPayload, dispatchPayload, sendPayload] = await Promise.all([
  fetchJson(args.url, "/api/network/events", { ...query, eventType: "local-chain-tip-header" }),
  fetchJson(args.url, "/api/network/events", { ...query, eventType: "mempool-chain-tip-notification" }),
  fetchJson(args.url, "/api/network/events", { ...query, eventType: "peer-chain-tip" }),
  fetchJson(args.url, "/api/network/events", { ...query, eventType: "chain-tip-relay-dispatch" }),
  fetchJson(args.url, "/api/network/events", { ...query, eventType: "chain-tip-send-complete" })
]);

const localEvents = Array.isArray(localPayload.events) ? localPayload.events : [];
const mempoolEvents = Array.isArray(mempoolPayload.events) ? mempoolPayload.events : [];
const peerEvents = Array.isArray(peerPayload.events) ? peerPayload.events : [];
const dispatchEvents = Array.isArray(dispatchPayload.events) ? dispatchPayload.events : [];
const sendEvents = Array.isArray(sendPayload.events) ? sendPayload.events : [];
function earliestByHash(events) {
  const byHash = new Map();
  for (const event of events) {
    const hash = normalizeHash(event.blockHash);
    const timestampMs = eventTimestampMs(event);
    if (!hash || timestampMs === null) continue;
    const existing = byHash.get(hash);
    if (!existing || timestampMs < existing.timestampMs) {
      byHash.set(hash, { event, timestampMs });
    }
  }
  return byHash;
}

function matchPeerEvents(peerObservations, baselineByHash, baselineLabel) {
  const matched = [];
  for (const event of peerObservations) {
    const hash = normalizeHash(event.blockHash);
    const peerTimestampMs = eventTimestampMs(event);
    const baseline = baselineByHash.get(hash);
    if (!hash || peerTimestampMs === null || !baseline) continue;

    matched.push({
      blockHash: hash,
      blockHeight: event.blockHeight ?? baseline.event.blockHeight ?? null,
      transport: event.transport || "unknown",
      source: event.remoteEndpoint || event.remoteNodeId || event.source || "unknown",
      peerReceivedUtc: event.timestampUtc,
      baseline: baselineLabel,
      baselineReceivedUtc: baseline.event.timestampUtc,
      localReceivedUtc: baseline.event.timestampUtc,
      leadMs: baseline.timestampMs - peerTimestampMs,
      payloadBytes: event.payloadBytes || 0
    });
  }
  return matched;
}

const localByHash = earliestByHash(localEvents);
const mempoolByHash = earliestByHash(mempoolEvents);
const matched = matchPeerEvents(peerEvents, localByHash, "local-bitcoin-zmq");
const mempoolMatched = matchPeerEvents(peerEvents, mempoolByHash, "mempool.space");
const confirmedHashes = new Set([...localByHash.keys(), ...mempoolByHash.keys()]);
const unconfirmed = peerEvents.filter(event => !confirmedHashes.has(normalizeHash(event.blockHash)));

const report = {
  generatedUtc: new Date().toISOString(),
  url: args.url.replace(/\/+$/, ""),
  windowSeconds: localPayload.windowSeconds ?? peerPayload.windowSeconds ?? null,
  localHeaderCount: localEvents.length,
  mempoolNotificationCount: mempoolEvents.length,
  peerObservationCount: peerEvents.length,
  matchedObservationCount: matched.length,
  unconfirmedObservationCount: unconfirmed.length,
  uniqueMatchedBlocks: new Set(matched.map(item => item.blockHash)).size,
  overall: summarize(matched),
  transports: Object.fromEntries([...groupBy(matched, item => item.transport)].map(([key, values]) => [key, summarize(values)])),
  sources: Object.fromEntries([...groupBy(matched, item => item.source)].map(([key, values]) => [key, summarize(values)])),
  mempool: {
    matchedObservationCount: mempoolMatched.length,
    uniqueMatchedBlocks: new Set(mempoolMatched.map(item => item.blockHash)).size,
    overall: summarize(mempoolMatched),
    transports: Object.fromEntries([...groupBy(mempoolMatched, item => item.transport)].map(([key, values]) => [key, summarize(values)])),
    sources: Object.fromEntries([...groupBy(mempoolMatched, item => item.source)].map(([key, values]) => [key, summarize(values)])),
    observations: mempoolMatched
  },
  senderDispatch: {
    queueToDispatch: summarizeDurations(dispatchEvents.map(event => event.relayLatencyMs)),
    transports: Object.fromEntries(
      [...groupBy(sendEvents, event => event.transport)].map(([key, values]) => [key, {
        sendDuration: summarizeDurations(values.map(event => event.relayLatencyMs)),
        queueToCompletion: summarizeDurations(values.map(event => {
          const completedMs = eventTimestampMs(event);
          const queuedMs = Date.parse(event.announcedAtUtc || "");
          return completedMs !== null && Number.isFinite(queuedMs) ? completedMs - queuedMs : null;
        }))
      }])
    )
  },
  observations: matched
};

console.log(`Chain-tip header telemetry: localZmq=${report.localHeaderCount} mempool=${report.mempoolNotificationCount} peer=${report.peerObservationCount} zmqMatched=${report.matchedObservationCount} mempoolMatched=${report.mempool.matchedObservationCount} unconfirmed=${report.unconfirmedObservationCount}`);
console.log("Positive lead means the peer transport reached this GridPool node before the named independent notification source.");
for (const [transport, summary] of Object.entries(report.transports)) {
  console.log(`  ${transport}: count=${summary.count} faster=${summary.fasterThanLocalCount} p50=${formatMs(summary.leadP50Ms)} p95=${formatMs(summary.leadP95Ms)} avg=${formatMs(summary.leadAverageMs)} range=${formatMs(summary.leadMinMs)}..${formatMs(summary.leadMaxMs)} payload=${Number.isFinite(summary.averagePayloadBytes) ? summary.averagePayloadBytes.toFixed(1) : "--"} B`);
}
for (const [source, summary] of Object.entries(report.sources)) {
  console.log(`  source ${source}: count=${summary.count} faster=${summary.fasterThanLocalCount} p50=${formatMs(summary.leadP50Ms)} p95=${formatMs(summary.leadP95Ms)}`);
}
for (const [transport, summary] of Object.entries(report.mempool.transports)) {
  console.log(`  vs mempool.space ${transport}: count=${summary.count} faster=${summary.fasterThanLocalCount} p50=${formatMs(summary.leadP50Ms)} p95=${formatMs(summary.leadP95Ms)} avg=${formatMs(summary.leadAverageMs)} range=${formatMs(summary.leadMinMs)}..${formatMs(summary.leadMaxMs)} payload=${Number.isFinite(summary.averagePayloadBytes) ? summary.averagePayloadBytes.toFixed(1) : "--"} B`);
}
for (const [source, summary] of Object.entries(report.mempool.sources)) {
  console.log(`  vs mempool.space source ${source}: count=${summary.count} faster=${summary.fasterThanLocalCount} p50=${formatMs(summary.leadP50Ms)} p95=${formatMs(summary.leadP95Ms)}`);
}
if (report.senderDispatch.queueToDispatch.count > 0) {
  const queue = report.senderDispatch.queueToDispatch;
  console.log(`  sender queue->dispatch: count=${queue.count} p50=${formatMs(queue.p50Ms)} p95=${formatMs(queue.p95Ms)}`);
}
for (const [transport, summary] of Object.entries(report.senderDispatch.transports)) {
  console.log(`  sender ${transport}: send p50=${formatMs(summary.sendDuration.p50Ms)} p95=${formatMs(summary.sendDuration.p95Ms)} queue->complete p50=${formatMs(summary.queueToCompletion.p50Ms)} p95=${formatMs(summary.queueToCompletion.p95Ms)}`);
}

if (args.json && args.json !== "true") {
  await fs.writeFile(args.json, `${JSON.stringify(report, null, 2)}\n`);
  console.log(`Wrote ${args.json}`);
}
