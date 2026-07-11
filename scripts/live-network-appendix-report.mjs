#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";

const DEFAULT_CONFIG = path.join("scripts", "live-network-nodes.sample.json");
const DEFAULT_OUT_DIR = "/home/keegreil/Documents/GitHub/gridpool-simulations/reports/july17/live-telemetry";
const DEFAULT_WINDOW = "12h";
const DEFAULT_LIMIT = 1000;
const DEFAULT_TIMEOUT_MS = 8000;

function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith("--")) continue;
    const key = arg.slice(2);
    const next = argv[i + 1];
    args[key] = next && !next.startsWith("--") ? argv[++i] : "true";
  }
  return args;
}

function usage() {
  console.error(`Usage: node scripts/live-network-appendix-report.mjs [options]

Options:
  --config <file>              Node config JSON (default: ${DEFAULT_CONFIG})
  --out-dir <dir>              Output directory (default: ${DEFAULT_OUT_DIR})
  --window <duration>          API telemetry window, e.g. 12h or 24h (default: ${DEFAULT_WINDOW})
  --limit <n>                  API event/observation limit (default: ${DEFAULT_LIMIT})
  --request-timeout-ms <n>     Per-request timeout (default: ${DEFAULT_TIMEOUT_MS})
  --help                       Show this help
`);
}

function normalizeBaseUrl(url) {
  return String(url || "").replace(/\/+$/, "");
}

function shortId(value) {
  if (!value) return "--";
  const text = String(value);
  return text.length <= 16 ? text : `${text.slice(0, 8)}...${text.slice(-8)}`;
}

function markdownCell(value) {
  if (value === null || value === undefined || value === "") return "--";
  return String(value).replace(/\|/g, "\\|").replace(/\n/g, " ");
}

function formatNumber(value, digits = 2) {
  if (value === null || value === undefined || value === "") return "--";
  const number = Number(value);
  return Number.isFinite(number) ? number.toFixed(digits) : "--";
}

function formatMs(value) {
  if (value === null || value === undefined || value === "") return "--";
  const number = Number(value);
  return Number.isFinite(number) ? `${number.toFixed(1)} ms` : "--";
}

function formatBytes(value) {
  if (value === null || value === undefined || value === "") return "--";
  const number = Number(value);
  return Number.isFinite(number) ? `${Math.round(number)} B` : "--";
}

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

function writeJson(file, payload) {
  fs.writeFileSync(file, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
}

function csvEscape(value) {
  if (value === null || value === undefined) return "";
  const text = String(value);
  return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function writeCsv(file, rows) {
  if (rows.length === 0) {
    fs.writeFileSync(file, "", "utf8");
    return;
  }
  const headers = Object.keys(rows[0]);
  const lines = [
    headers.join(","),
    ...rows.map(row => headers.map(header => csvEscape(row[header])).join(","))
  ];
  fs.writeFileSync(file, `${lines.join("\n")}\n`, "utf8");
}

async function fetchJson(baseUrl, route, query, timeoutMs) {
  const url = new URL(`${normalizeBaseUrl(baseUrl)}${route}`);
  for (const [key, value] of Object.entries(query || {})) {
    if (value !== undefined && value !== null && value !== "") {
      url.searchParams.set(key, String(value));
    }
  }

  const started = Date.now();
  const response = await fetch(url, {
    headers: { accept: "application/json" },
    signal: AbortSignal.timeout(timeoutMs)
  });
  const latencyMs = Date.now() - started;
  if (!response.ok) {
    throw new Error(`${url.toString()} returned HTTP ${response.status}`);
  }
  return { payload: await response.json(), latencyMs };
}

async function fetchOptional(baseUrl, route, query, timeoutMs) {
  try {
    return await fetchJson(baseUrl, route, query, timeoutMs);
  } catch (error) {
    return { error: String(error.message || error) };
  }
}

async function collectNode(node, options) {
  const now = new Date().toISOString();
  const result = {
    config: node,
    label: node.label,
    region: node.region,
    role: node.role,
    operator: node.operator,
    url: node.url,
    enabled: node.enabled !== false,
    primary: node.primary === true,
    queriedAtUtc: now,
    reachable: false,
    notes: [],
    errors: []
  };

  if (node.enabled === false || !node.url) {
    result.notes.push(node.notes || "Node disabled or endpoint not configured.");
    return result;
  }

  const summary = await fetchOptional(node.url, "/api/network/summary", {}, options.timeoutMs);
  if (summary.error) {
    result.errors.push(`summary: ${summary.error}`);
    return result;
  }

  result.reachable = true;
  result.summaryLatencyMs = summary.latencyMs;
  result.summary = summary.payload;

  const [peers, relay, events] = await Promise.all([
    fetchOptional(node.url, "/api/network/peer-addresses", { limit: options.peerLimit }, options.timeoutMs),
    fetchOptional(node.url, "/api/network/peer-relay-latency", { window: options.window, limit: options.limit }, options.timeoutMs),
    fetchOptional(node.url, "/api/network/events", { window: options.window, limit: options.limit }, options.timeoutMs)
  ]);

  if (peers.error) result.errors.push(`peer-addresses: ${peers.error}`);
  else result.peers = peers.payload;

  if (relay.error) result.errors.push(`peer-relay-latency: ${relay.error}`);
  else result.relay = relay.payload;

  if (events.error) result.errors.push(`events: ${events.error}`);
  else result.events = events.payload;

  annotateNode(result);
  return result;
}

function annotateNode(node) {
  const peers = Array.isArray(node.peers?.peers) ? node.peers.peers : Array.isArray(node.summary?.peers) ? node.summary.peers : [];
  const endpointCounts = new Map();
  for (const peer of peers) {
    const endpoint = normalizePeerEndpoint(peer.endpoint || peer.publicEndpoint || peer.selfEndpoint || "");
    if (!endpoint) continue;
    endpointCounts.set(endpoint, (endpointCounts.get(endpoint) || 0) + 1);
    const status = String(peer.status || "").toLowerCase();
    if (status.includes("stale") || status.includes("error") || status.includes("fail")) {
      node.notes.push(`Peer ${endpoint} reports status=${peer.status}.`);
    }
    if (peer.lastSuccessUtc === null || peer.lastSuccessUtc === "") {
      node.notes.push(`Peer ${endpoint} has no recorded lastSuccessUtc.`);
    }
  }
  for (const [endpoint, count] of endpointCounts) {
    if (count > 1) {
      node.notes.push(`Duplicate peer entry observed for ${endpoint}.`);
    }
  }

  const transports = Array.isArray(node.relay?.transports) ? node.relay.transports : [];
  if (!transports.some(item => String(item.transport || "").toLowerCase() === "udp")) {
    node.notes.push("No UDP relay observations in selected window.");
  }
}

function normalizePeerEndpoint(endpoint) {
  return String(endpoint || "")
    .trim()
    .replace(/\/+$/, "")
    .replace(/^https?:\/\//i, "")
    .replace(/:5000$/i, "");
}

function buildStateAgreement(nodes) {
  const reachable = nodes.filter(node => node.reachable);
  const primary = reachable.filter(node => node.primary);
  const primaryCurrent = unique(primary.map(node => node.summary?.currentStateId).filter(Boolean));
  const primaryCandidate = unique(primary.map(node => node.summary?.candidateStateId).filter(Boolean));
  const allCurrent = unique(reachable.map(node => node.summary?.currentStateId).filter(Boolean));
  const allCandidate = unique(reachable.map(node => node.summary?.candidateStateId).filter(Boolean));
  return {
    primaryCompared: primary.map(node => node.label),
    primaryCurrentAgreement: primary.length > 0 && primaryCurrent.length === 1,
    primaryCandidateAgreement: primary.length > 0 && primaryCandidate.length === 1,
    allReachableCurrentAgreement: reachable.length > 0 && allCurrent.length === 1,
    allReachableCandidateAgreement: reachable.length > 0 && allCandidate.length === 1,
    primaryCurrentIds: primaryCurrent,
    primaryCandidateIds: primaryCandidate,
    allCurrentIds: allCurrent,
    allCandidateIds: allCandidate,
    mismatches: findMismatches(reachable, primaryCurrent[0], primaryCandidate[0])
  };
}

function unique(values) {
  return [...new Set(values.map(String))];
}

function findMismatches(nodes, expectedCurrent, expectedCandidate) {
  return nodes
    .filter(node => {
      if (!node.summary) return false;
      const currentMismatch = expectedCurrent && node.summary.currentStateId !== expectedCurrent;
      const candidateMismatch = expectedCandidate && node.summary.candidateStateId !== expectedCandidate;
      return currentMismatch || candidateMismatch;
    })
    .map(node => ({
      label: node.label,
      currentStateId: node.summary?.currentStateId,
      candidateStateId: node.summary?.candidateStateId
    }));
}

function eventCounts(eventsPayload) {
  const counts = {};
  const events = Array.isArray(eventsPayload?.events) ? eventsPayload.events : [];
  for (const event of events) {
    const key = event.eventType || "unknown";
    counts[key] = (counts[key] || 0) + 1;
  }
  return counts;
}

function v21SignalSummary(node) {
  const counts = eventCounts(node.events);
  const events = Array.isArray(node.events?.events) ? node.events.events : [];
  const relayObservations = Array.isArray(node.relay?.observations) ? node.relay.observations : [];
  return {
    payoutSnapshotCount: counts["payout-snapshot"] || 0,
    snapshotPaidCount: counts["snapshot-paid"] || 0,
    gridpoolBlockFoundCount: counts["gridpool-block-found"] || 0,
    sessionStateImportedCount: counts["session-state-imported"] || 0,
    sessionStateRejectedCount: counts["session-state-rejected"] || 0,
    chainTipStaleCount: counts["chain-tip-stale"] || 0,
    peerPrunedCount: counts["peer-pruned"] || 0,
    freshParentLearnedCount: counts["fresh-parent-learned"] || 0,
    relayRejectedCount: relayObservations.filter(item => item.accepted === false).length,
    relayAcceptedCount: relayObservations.filter(item => item.accepted === true).length,
    eventTypes: counts,
    missingExplicitCounters: [
      "snapshot-boundary disagreement counter",
      "late previous-parent proof quarantine/rejection counter",
      "current-parent merge-forward counter",
      "state-bundle import/rejection reason counters",
      "paid proof removal counter separate from event text"
    ]
  };
}

function relayRows(nodes) {
  const rows = [];
  for (const node of nodes) {
    const transports = Array.isArray(node.relay?.transports) ? node.relay.transports : [];
    if (transports.length === 0) {
      rows.push({
        node: node.label,
        transport: "--",
        observations: 0,
        firstArrivals: 0,
        accepted: 0,
        duplicates: 0,
        rejectedOrStale: 0,
        medianDeltaMs: null,
        p95DeltaMs: null,
        averagePayloadBytes: null,
        udpPresent: false
      });
      continue;
    }
    const udpPresent = transports.some(item => String(item.transport || "").toLowerCase() === "udp");
    for (const transport of transports) {
      rows.push({
        node: node.label,
        transport: transport.transport || "unknown",
        observations: transport.arrivalCount ?? 0,
        firstArrivals: transport.firstArrivalCount ?? 0,
        accepted: transport.acceptedCount ?? 0,
        duplicates: transport.duplicateCount ?? 0,
        rejectedOrStale: transport.rejectedCount ?? 0,
        medianDeltaMs: transport.medianDeltaFromFirstMs,
        p95DeltaMs: transport.p95DeltaFromFirstMs,
        averagePayloadBytes: transport.averagePayloadBytes,
        udpPresent
      });
    }
  }
  return rows;
}

function nodeRows(nodes) {
  return nodes.map(node => ({
    node: node.label,
    region: node.region || "",
    role: node.role || "",
    operator: node.operator || "",
    url: node.url || "",
    reachable: node.reachable ? "yes" : node.enabled ? "no" : "disabled",
    version: node.summary?.releaseVersion || node.summary?.versionInfo?.releaseVersion || "",
    consensusVersion: node.summary?.consensusVersion ?? node.summary?.protocolVersion ?? "",
    peerTransportVersion: node.summary?.peerTransportVersion ?? "",
    udpRelayVersion: node.summary?.udpRelayVersion ?? "",
    udpEnabled: node.summary?.enablePeerUdpFastRelay,
    udpPort: node.summary?.peerUdpPort ?? "",
    udpMaxDatagramBytes: node.summary?.peerUdpMaxDatagramBytes ?? "",
    probeAllTransports: node.summary?.peerRelayLatencyProbeAllTransports,
    currentStateId: node.summary?.currentStateId || "",
    candidateStateId: node.summary?.candidateStateId || "",
    currentTipHeight: node.summary?.currentTipBlockHeight ?? "",
    peerCount: node.summary?.peerCount ?? "",
    advertisedEndpoint: node.summary?.selfEndpoint || "",
    notes: [...(node.notes || []), ...(node.errors || [])].join(" ")
  }));
}

function eventRows(nodes) {
  const rows = [];
  for (const node of nodes) {
    const signal = v21SignalSummary(node);
    for (const [eventType, count] of Object.entries(signal.eventTypes || {})) {
      rows.push({ node: node.label, eventType, count });
    }
  }
  return rows;
}

function writeMarkdown(file, report) {
  const lines = [];
  lines.push("# Live Network Appendix");
  lines.push("");
  lines.push("Status: generated by `scripts/live-network-appendix-report.mjs`.");
  lines.push("");
  lines.push("> Field sanity check, not statistical proof.");
  lines.push("");
  lines.push("This report checks whether the current public GridPool V2.1 network is observable, whether reachable primary nodes agree on state, and whether peer relay telemetry is available. It should not be read as proof that UDP or compact relay is globally field-proven.");
  lines.push("");
  lines.push("## Run Window");
  lines.push("");
  lines.push("| Field | Value |");
  lines.push("| --- | --- |");
  lines.push(`| Started UTC | ${markdownCell(report.startedAtUtc)} |`);
  lines.push(`| Ended UTC | ${markdownCell(report.endedAtUtc)} |`);
  lines.push(`| Duration | ${markdownCell(formatNumber(report.durationSeconds, 1))} seconds |`);
  lines.push(`| API telemetry window | ${markdownCell(report.window)} |`);
  lines.push(`| API sample limit | ${markdownCell(report.limit)} |`);
  lines.push(`| Request timeout | ${markdownCell(report.requestTimeoutMs)} ms |`);
  lines.push("");

  lines.push("## Node Summary");
  lines.push("");
  lines.push("| Node | Region | Role | Operator | URL | Reachable | Version | Current State | Candidate State | UDP Config | Notes |");
  lines.push("| --- | --- | --- | --- | --- | ---: | --- | --- | --- | --- | --- |");
  for (const row of nodeRows(report.nodes)) {
    const udpConfig = row.reachable === "yes"
      ? `enabled=${row.udpEnabled ?? "--"}, version=${row.udpRelayVersion || "--"}, port=${row.udpPort || "--"}, max=${row.udpMaxDatagramBytes || "--"}, probeAll=${row.probeAllTransports ?? "--"}`
      : "--";
    lines.push(`| ${markdownCell(row.node)} | ${markdownCell(row.region)} | ${markdownCell(row.role)} | ${markdownCell(row.operator)} | ${markdownCell(row.url)} | ${markdownCell(row.reachable)} | ${markdownCell(row.version || row.consensusVersion)} | ${markdownCell(shortId(row.currentStateId))} | ${markdownCell(shortId(row.candidateStateId))} | ${markdownCell(udpConfig)} | ${markdownCell(row.notes)} |`);
  }
  lines.push("");

  lines.push("## State Agreement");
  lines.push("");
  lines.push("| Comparison | Status | Details |");
  lines.push("| --- | --- | --- |");
  lines.push(`| Primary nodes compared | ${markdownCell(report.stateAgreement.primaryCompared.join(", ") || "--")} | Main/Dallas/Detroit only; disabled or unreachable nodes excluded. |`);
  lines.push(`| Primary currentStateId | ${report.stateAgreement.primaryCurrentAgreement ? "agree" : "mismatch"} | ${markdownCell(report.stateAgreement.primaryCurrentIds.map(shortId).join(", ") || "--")} |`);
  lines.push(`| Primary candidateStateId | ${report.stateAgreement.primaryCandidateAgreement ? "agree" : "mismatch"} | ${markdownCell(report.stateAgreement.primaryCandidateIds.map(shortId).join(", ") || "--")} |`);
  lines.push(`| All reachable currentStateId | ${report.stateAgreement.allReachableCurrentAgreement ? "agree" : "mismatch"} | ${markdownCell(report.stateAgreement.allCurrentIds.map(shortId).join(", ") || "--")} |`);
  lines.push(`| All reachable candidateStateId | ${report.stateAgreement.allReachableCandidateAgreement ? "agree" : "mismatch"} | ${markdownCell(report.stateAgreement.allCandidateIds.map(shortId).join(", ") || "--")} |`);
  const mismatchText = report.stateAgreement.mismatches.length === 0
    ? "No reachable-node state mismatches observed."
    : report.stateAgreement.mismatches.map(item => `${item.label}: current=${shortId(item.currentStateId)} candidate=${shortId(item.candidateStateId)}`).join("; ");
  lines.push(`| Mismatches | ${report.stateAgreement.mismatches.length === 0 ? "none" : "present"} | ${markdownCell(mismatchText)} |`);
  lines.push("");

  lines.push("## Relay Telemetry");
  lines.push("");
  lines.push("| Node | Transport | Observations | First Arrivals | Accepted | Duplicates | Rejected/Stale | Median Delta | P95 Delta | Avg Payload | UDP Present |");
  lines.push("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
  for (const row of relayRows(report.nodes)) {
    lines.push(`| ${markdownCell(row.node)} | ${markdownCell(row.transport)} | ${row.observations} | ${row.firstArrivals} | ${row.accepted} | ${row.duplicates} | ${row.rejectedOrStale} | ${markdownCell(formatMs(row.medianDeltaMs))} | ${markdownCell(formatMs(row.p95DeltaMs))} | ${markdownCell(formatBytes(row.averagePayloadBytes))} | ${row.udpPresent ? "yes" : "no"} |`);
  }
  lines.push("");

  lines.push("## V2.1-Specific Signals");
  lines.push("");
  lines.push("| Node | Payout Snapshots | Snapshot Paid | GridPool Blocks | State Imports | State Rejections | Stale Chain Tips | Fresh Parents | Relay Accepted | Relay Rejected |");
  lines.push("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
  for (const node of report.nodes) {
    const signal = node.v21Signals || v21SignalSummary(node);
    lines.push(`| ${markdownCell(node.label)} | ${signal.payoutSnapshotCount} | ${signal.snapshotPaidCount} | ${signal.gridpoolBlockFoundCount} | ${signal.sessionStateImportedCount} | ${signal.sessionStateRejectedCount} | ${signal.chainTipStaleCount} | ${signal.freshParentLearnedCount} | ${signal.relayAcceptedCount} | ${signal.relayRejectedCount} |`);
  }
  lines.push("");
  lines.push("Missing explicit runtime counters:");
  lines.push("");
  lines.push("- snapshot-boundary disagreement count;");
  lines.push("- late previous-parent proof rejection/quarantine count;");
  lines.push("- current-parent merge-forward count;");
  lines.push("- state-bundle import/rejection reason counters;");
  lines.push("- paid proof removal counter separate from event text.");
  lines.push("");
  lines.push("Smallest useful runtime/API additions: add a `/api/network/v21-consensus-telemetry` endpoint or extend `/api/network/summary` with cumulative counters for those five items, plus reset-free process start time for interpreting windows.");
  lines.push("");

  lines.push("## Data Quality Notes");
  lines.push("");
  const notes = buildDataQualityNotes(report);
  for (const note of notes) {
    lines.push(`- ${note}`);
  }
  lines.push("");

  lines.push("## Paper-Ready Interpretation");
  lines.push("");
  lines.push("This run confirms that the public GridPool nodes expose state, peer, event, and relay telemetry, and that state agreement can be checked across independently hosted nodes. In this sample, reachable primary nodes can be compared directly and external operator nodes can be flagged if they diverge. The sample is too small and too operationally dependent to prove global latency behavior or UDP effectiveness; it is a field sanity check for observability and state health.");
  lines.push("");

  fs.writeFileSync(file, `${lines.join("\n")}\n`, "utf8");
}

function buildDataQualityNotes(report) {
  const notes = [];
  for (const node of report.nodes) {
    if (!node.enabled) {
      notes.push(`${node.label}: endpoint not configured/enabled.`);
      continue;
    }
    if (!node.reachable) {
      notes.push(`${node.label}: unreachable (${(node.errors || []).join("; ") || "unknown error"}).`);
      continue;
    }
    for (const note of node.notes || []) {
      notes.push(`${node.label}: ${note}`);
    }
    if (!node.summary?.releaseVersion) {
      notes.push(`${node.label}: releaseVersion missing or not exposed.`);
    }
  }
  if (notes.length === 0) {
    notes.push("No major data quality issues detected in this run.");
  }
  notes.push("Manual node restarts/interventions are not detectable from the public API unless they produce network events; add process uptime/start time for cleaner interpretation.");
  return notes;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help === "true") {
    usage();
    return 0;
  }

  const configPath = args.config || DEFAULT_CONFIG;
  const outDir = args["out-dir"] || DEFAULT_OUT_DIR;
  const window = args.window || DEFAULT_WINDOW;
  const limit = Number.parseInt(args.limit || DEFAULT_LIMIT, 10);
  const timeoutMs = Number.parseInt(args["request-timeout-ms"] || DEFAULT_TIMEOUT_MS, 10);
  const peerLimit = Number.parseInt(args["peer-limit"] || 128, 10);
  const startedAtUtc = new Date().toISOString();
  const startedMs = Date.now();

  const config = JSON.parse(fs.readFileSync(configPath, "utf8"));
  const options = { window, limit, timeoutMs, peerLimit };
  const nodes = [];
  for (const node of config.nodes || []) {
    nodes.push(await collectNode(node, options));
  }
  for (const node of nodes) {
    node.v21Signals = v21SignalSummary(node);
  }
  const endedAtUtc = new Date().toISOString();
  const report = {
    reportName: config.reportName || "GridPool live network appendix",
    configPath,
    startedAtUtc,
    endedAtUtc,
    durationSeconds: (Date.now() - startedMs) / 1000,
    window,
    limit,
    requestTimeoutMs: timeoutMs,
    nodes,
    stateAgreement: buildStateAgreement(nodes)
  };

  ensureDir(outDir);
  writeJson(path.join(outDir, "live-network-appendix.json"), report);
  writeCsv(path.join(outDir, "node-summary.csv"), nodeRows(nodes));
  writeCsv(path.join(outDir, "relay-telemetry.csv"), relayRows(nodes));
  writeCsv(path.join(outDir, "event-counts.csv"), eventRows(nodes));
  writeMarkdown(path.join(outDir, "live-network-appendix.md"), report);
  console.log(`Wrote live network appendix to ${path.join(outDir, "live-network-appendix.md")}`);
}

main().catch(error => {
  console.error(error.stack || error.message || String(error));
  process.exit(1);
});
