#!/usr/bin/env node

import fs from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

const SCRIPT_VERSION = 6;
const DEFAULT_STATE_DIR = path.join(os.homedir(), ".local", "state", "gridpool-monitor");
const DEFAULT_CONFIG_PATHS = [
    path.join(os.homedir(), ".config", "gridpool-health-monitor", "config.json"),
    path.join(process.cwd(), "scripts", "gridpool-health-monitor.local.json")
];

const FOUNDATION_ADDRESS = "bc1qchlyrly5nd6a5fvq46lp8vgs9mf52g4njdwmny";

const DEFAULT_CONFIG = {
    monitorName: "gridpool-mainnet-beta",
    timezone: "America/New_York",
    requestTimeoutMs: 8000,
    alertCooldownMinutes: 60,
    codexCooldownMinutes: 360,
    morningDigest: {
        enabled: true,
        hourLocal: 7
    },
    telegram: {
        enabled: true,
        botTokenEnv: "TELEGRAM_BOT_TOKEN",
        allowedChatIdsEnv: "TELEGRAM_ALLOWED_CHAT_IDS",
        commandChatIdsEnv: "TELEGRAM_COMMAND_CHAT_IDS"
    },
    codex: {
        enabled: false,
        repoDir: process.cwd(),
        timeoutSeconds: 600,
        model: "",
        resumeArgs: ["exec", "-C", process.cwd(), "--sandbox", "read-only", "--ask-for-approval", "never", "resume", "--last"]
    },
    incidentCapture: {
        enabled: true,
        window: "24h",
        sessionLimit: 1000,
        eventLimit: 2000,
        relayLimit: 2000
    },
    thresholds: {
        endpointFailureConsecutive: 2,
        consensusDivergenceConsecutive: 2,
        candidateDivergenceConsecutive: 3,
        candidateDivergenceMinimumMinutes: 10,
        datumRejectRateMax: 0.10,
        datumRejectRateMinSubmissions: 25,
        hashrateDropFraction: 0.35,
        hashrateSpikeMultiplier: 2.0,
        hashrateSamplesForTrend: 3,
        minimumHashrateThsForTrend: 1,
        localMiningFreshnessMinutes: 20,
        activeHydrapoolWorkerMaxAgeMinutes: 60,
        outboundRelayStaleMinutes: 10,
        peerOutboundAttemptStaleMinutes: 10
    },
    nodes: [
        {
            name: "main",
            baseUrl: "http://127.0.0.1:5000",
            critical: true,
            consensusGroup: "mainnet-beta",
            minimumPeerCount: 0
        }
    ],
    hydrapools: [
        {
            name: "main-hydrapool",
            baseUrl: "http://127.0.0.1:46884",
            critical: false,
            usernameEnv: "HYDRAPOOL_API_USER",
            passwordEnv: "HYDRAPOOL_API_PASSWORD",
            defaultUsername: "hydrapool",
            defaultPassword: "hydrapool"
        }
    ],
    tcpEndpoints: [],
    services: [
        { name: "bootserverapp.service", critical: true },
        { name: "hydrapool-gridpool.service", critical: false },
        { name: "cloudflared.service", critical: true },
        { name: "docker.service", critical: false }
    ],
    knownAddresses: [
        { address: FOUNDATION_ADDRESS, label: "256 Foundation genesis/default payout" }
    ]
};

function parseArgs(argv) {
    const args = {};
    for (let i = 0; i < argv.length; i += 1) {
        const arg = argv[i];
        if (!arg.startsWith("--")) {
            continue;
        }

        const key = arg.slice(2);
        const next = argv[i + 1];
        args[key] = next && !next.startsWith("--") ? argv[++i] : "true";
    }
    return args;
}

function usage() {
    console.log(`Usage: node scripts/gridpool-health-monitor.mjs [options]

Options:
  --config <file>             JSON config path
  --state-dir <dir>           Runtime state directory
  --telegram-disabled         Do not call Telegram APIs
  --codex-disabled            Do not launch Codex investigations
  --force-digest              Send the morning digest now
  --test-telegram             Send a test Telegram message and exit
  --print-summary             Print compact JSON summary
  --self-test                 Run pure monitor behavior tests
  --help                      Show this help
`);
}

function expandHome(value) {
    if (!value) return value;
    if (value === "~") return os.homedir();
    if (value.startsWith("~/")) return path.join(os.homedir(), value.slice(2));
    return value;
}

function readJsonIfExists(filePath) {
    if (!filePath || !fs.existsSync(filePath)) {
        return null;
    }
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function writeJsonAtomic(filePath, payload) {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    const tempPath = `${filePath}.tmp`;
    fs.writeFileSync(tempPath, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
    fs.renameSync(tempPath, filePath);
}

function appendJsonLine(filePath, payload) {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.appendFileSync(filePath, `${JSON.stringify(payload)}\n`, "utf8");
}

function isPlainObject(value) {
    return value && typeof value === "object" && !Array.isArray(value);
}

function mergeConfig(base, override) {
    if (!isPlainObject(override)) {
        return structuredClone(base);
    }

    const result = structuredClone(base);
    for (const [key, value] of Object.entries(override)) {
        if (isPlainObject(value) && isPlainObject(result[key])) {
            result[key] = mergeConfig(result[key], value);
        } else {
            result[key] = value;
        }
    }
    return result;
}

function resolveConfigPath(args) {
    if (args.config) {
        return expandHome(args.config);
    }
    if (process.env.GRIDPOOL_HEALTH_CONFIG) {
        return expandHome(process.env.GRIDPOOL_HEALTH_CONFIG);
    }
    return DEFAULT_CONFIG_PATHS.find(filePath => fs.existsSync(filePath)) || null;
}

function loadConfig(args) {
    const configPath = resolveConfigPath(args);
    const userConfig = readJsonIfExists(configPath);
    const config = mergeConfig(DEFAULT_CONFIG, userConfig || {});
    config.configPath = configPath;

    if (args["telegram-disabled"] === "true") {
        config.telegram.enabled = false;
    }
    if (args["codex-disabled"] === "true") {
        config.codex.enabled = false;
    }

    return config;
}

function statePathFor(stateDir) {
    return path.join(stateDir, "state.json");
}

function loadState(stateDir) {
    const state = readJsonIfExists(statePathFor(stateDir));
    const merged = mergeConfig({
        version: SCRIPT_VERSION,
        initialized: false,
        failureCounts: {},
        failureFirstSeenUtc: {},
        alertCooldowns: {},
        openAlertLifecycles: {},
        activeIncidentKeys: [],
        codexCooldowns: {},
        hashrateHistory: {},
        lastRoundByNode: {},
        lastGridPoolBlockByNode: {},
        known: {
            datumAddresses: [],
            hydrapoolWorkers: [],
            peers: [],
            unknownListAddresses: []
        },
        telegram: {
            updateOffset: null,
            silencedUntilUtc: null,
            lastMorningDigestDate: null
        },
        recentAlerts: [],
        lastSnapshot: null
    }, state || {});
    merged.version = SCRIPT_VERSION;
    return merged;
}

function normalizeBaseUrl(url) {
    return String(url || "").replace(/\/+$/, "");
}

function normalizeAddress(value) {
    return String(value || "").trim().toLowerCase();
}

function currentIso() {
    return new Date().toISOString();
}

function parseDate(value) {
    const time = Date.parse(value || "");
    return Number.isFinite(time) ? time : null;
}

function localDateParts(timezone) {
    const parts = new Intl.DateTimeFormat("en-CA", {
        timeZone: timezone,
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        hourCycle: "h23"
    }).formatToParts(new Date());

    const byType = Object.fromEntries(parts.map(part => [part.type, part.value]));
    return {
        date: `${byType.year}-${byType.month}-${byType.day}`,
        hour: Number(byType.hour)
    };
}

function durationMsLabel(ms) {
    if (!Number.isFinite(ms)) return "--";
    if (ms < 1000) return `${ms.toFixed(0)} ms`;
    return `${(ms / 1000).toFixed(1)} s`;
}

function formatHashrateThs(value) {
    const ths = Number(value);
    if (!Number.isFinite(ths) || ths <= 0) return "--";
    if (ths >= 1_000_000) return `${trimNumber(ths / 1_000_000)} EH/s`;
    if (ths >= 1_000) return `${trimNumber(ths / 1_000)} PH/s`;
    if (ths >= 1) return `${trimNumber(ths)} TH/s`;
    return `${trimNumber(ths * 1000)} GH/s`;
}

function trimNumber(value) {
    return Number(value).toFixed(2).replace(/\.00$/, "").replace(/(\.\d)0$/, "$1");
}

async function fetchWithTimeout(url, options = {}, timeoutMs = 8000) {
    const controller = new AbortController();
    const started = Date.now();
    const timeout = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const response = await fetch(url, {
            ...options,
            signal: controller.signal
        });
        const text = await response.text();
        return {
            ok: response.ok,
            status: response.status,
            url,
            durationMs: Date.now() - started,
            text,
            json: safeJson(text)
        };
    } catch (error) {
        return {
            ok: false,
            status: null,
            url,
            durationMs: Date.now() - started,
            error: error?.name === "AbortError" ? "request timed out" : String(error?.message || error)
        };
    } finally {
        clearTimeout(timeout);
    }
}

function safeJson(text) {
    if (!text) return null;
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

function basicAuthHeader(username, password) {
    const token = Buffer.from(`${username}:${password}`, "utf8").toString("base64");
    return `Basic ${token}`;
}

function gridPoolAdminHeaders(node) {
    const envName = String(node?.adminKeyEnv || "").trim();
    const adminKey = envName ? process.env[envName] || "" : "";
    return adminKey ? { "X-Boot-Admin-Key": adminKey } : {};
}

async function fetchJson(baseUrl, pathSuffix, config, options = {}) {
    const result = await fetchWithTimeout(`${normalizeBaseUrl(baseUrl)}${pathSuffix}`, options, config.requestTimeoutMs);
    return result;
}

async function collectGridPoolNode(node, config) {
    const baseUrl = normalizeBaseUrl(node.baseUrl);
    const result = {
        type: "gridpool",
        name: node.name,
        baseUrl,
        enabled: node.enabled !== false,
        skipped: node.enabled === false,
        critical: node.critical !== false,
        ok: true,
        checks: {},
        errors: [],
        summary: null,
        ready: null,
        localMiners: [],
        payoutAddresses: [],
        candidateAddresses: [],
        currentState: null,
        peers: [],
        peerRecords: [],
        peerRelayLatency: null,
        consensusGroup: node.consensusGroup || "",
        adminKeyEnv: node.adminKeyEnv || "",
        suppressCoinbaseModeAlert: node.suppressCoinbaseModeAlert === true,
        suppressTeamHashrateAlerts: node.suppressTeamHashrateAlerts === true,
        // Keep the old option as an alias while the monitor moves from the
        // legacy DATUM-named summary field to all fresh local mining sources.
        suppressLocalMiningHashrateAlerts: node.suppressLocalMiningHashrateAlerts === true ||
            node.suppressLocalDatumHashrateAlerts === true,
        version: null,
        networkKey: ""
    };

    if (node.enabled === false) {
        return result;
    }

    const requestOptions = { headers: gridPoolAdminHeaders(node) };
    const live = await fetchJson(baseUrl, "/health/live", config, requestOptions);
    result.checks.live = compactFetchResult(live);
    if (!live.ok) result.errors.push(`live failed: ${live.error || live.status}`);

    const ready = await fetchJson(baseUrl, "/health/ready", config, requestOptions);
    result.checks.ready = compactFetchResult(ready);
    result.ready = ready.json;
    if (!ready.ok) result.errors.push(`ready failed: ${ready.error || ready.status}`);

    const summary = await fetchJson(baseUrl, "/api/network/summary", config, requestOptions);
    result.checks.summary = compactFetchResult(summary);
    result.summary = summary.json;
    if (!summary.ok || !summary.json) result.errors.push(`summary failed: ${summary.error || summary.status}`);

    const miners = await fetchJson(baseUrl, "/api/network/local-miners?limit=500&window=24h", config, requestOptions);
    result.checks.localMiners = compactFetchResult(miners, { optionalStatuses: [404] });
    result.localMiners = Array.isArray(miners.json?.miners) ? miners.json.miners : [];
    if (!miners.ok && miners.status !== 404) {
        result.errors.push(`local-miners failed: ${miners.error || miners.status}`);
    }

    const payouts = await fetchJson(baseUrl, "/api/mining/payouts", config, requestOptions);
    result.checks.payouts = compactFetchResult(payouts);
    result.payoutAddresses = extractAddresses(payouts.json?.payouts);
    if (!payouts.ok) result.errors.push(`payouts failed: ${payouts.error || payouts.status}`);

    const candidateStateId = summary.json?.candidateStateId;
    if (candidateStateId) {
        const state = await fetchJson(baseUrl, `/api/network/state/${encodeURIComponent(candidateStateId)}`, config, requestOptions);
        result.checks.candidateState = compactFetchResult(state);
        result.candidateState = state.json;
        result.candidateAddresses = extractAddresses(state.json?.winnersList);
        if (!state.ok) result.errors.push(`candidate state failed: ${state.error || state.status}`);
    }

    const currentStateId = summary.json?.currentStateId;
    if (currentStateId) {
        const state = await fetchJson(baseUrl, `/api/network/state/${encodeURIComponent(currentStateId)}`, config, requestOptions);
        result.checks.currentState = compactFetchResult(state);
        result.currentState = state.json;
    }

    const latency = await fetchJson(baseUrl, "/api/network/peer-relay-latency?window=24h&limit=1000", config, requestOptions);
    result.checks.peerRelayLatency = compactFetchResult(latency, { optionalStatuses: [404] });
    result.peerRelayLatency = latency.json;
    if (!latency.ok && latency.status !== 404) {
        result.errors.push(`peer relay latency failed: ${latency.error || latency.status}`);
    }

    result.peerRecords = Array.isArray(summary.json?.peers) ? summary.json.peers : [];
    result.peers = result.peerRecords
        .map(peer => peer.endpoint || peer.url || peer.address)
        .filter(Boolean);
    result.version = summary.json?.versionInfo || {
        consensusVersion: summary.json?.consensusVersion,
        stateBundleSchemaVersion: summary.json?.stateBundleSchemaVersion,
        httpApiVersion: summary.json?.httpApiVersion,
        peerTransportVersion: summary.json?.peerTransportVersion,
        udpRelayVersion: summary.json?.udpRelayVersion,
        releaseVersion: summary.json?.releaseVersion
    };
    result.networkKey = node.consensusGroup ||
        summary.json?.networkId ||
        `${summary.json?.bitcoinNetwork || "unknown"}:${summary.json?.bootNetworkId || ""}` ||
        node.name;

    result.ok = result.errors.length === 0;
    return result;
}

async function collectTcpEndpoint(endpoint, config) {
    const started = Date.now();
    const timeoutMs = Number(endpoint.timeoutMs || config.requestTimeoutMs || 8000);
    const host = String(endpoint.host || "").trim();
    const port = Number(endpoint.port);
    const result = {
        type: "tcp",
        name: endpoint.name,
        host,
        port,
        critical: endpoint.critical === true,
        ok: false,
        durationMs: null,
        error: null
    };

    if (!host || !Number.isInteger(port) || port <= 0 || port > 65535) {
        result.error = "invalid TCP endpoint config";
        result.durationMs = Date.now() - started;
        return result;
    }

    return new Promise(resolve => {
        const socket = new net.Socket();
        let settled = false;
        const finish = (ok, error = null) => {
            if (settled) return;
            settled = true;
            socket.destroy();
            result.ok = ok;
            result.error = error;
            result.durationMs = Date.now() - started;
            resolve(result);
        };

        socket.setTimeout(timeoutMs);
        socket.once("connect", () => finish(true));
        socket.once("timeout", () => finish(false, "connection timed out"));
        socket.once("error", error => finish(false, String(error?.message || error)));
        socket.connect(port, host);
    });
}

function compactFetchResult(result, { optionalStatuses = [] } = {}) {
    const unavailable = !result.ok && optionalStatuses.includes(result.status);
    return {
        ok: !!result.ok || unavailable,
        available: !!result.ok,
        status: result.status,
        durationMs: result.durationMs,
        error: result.error || null
    };
}

function extractAddresses(items) {
    return [...new Set((Array.isArray(items) ? items : [])
        .map(item => normalizeAddress(item?.address || item?.minerAddress || item?.username))
        .filter(Boolean))];
}

async function collectHydrapool(hydrapool, config) {
    const baseUrl = normalizeBaseUrl(hydrapool.baseUrl);
    const username = process.env[hydrapool.usernameEnv || "HYDRAPOOL_API_USER"] || hydrapool.defaultUsername || "";
    const password = process.env[hydrapool.passwordEnv || "HYDRAPOOL_API_PASSWORD"] || hydrapool.defaultPassword || "";
    const headers = username || password
        ? { Authorization: basicAuthHeader(username, password) }
        : {};

    const result = {
        type: "hydrapool",
        name: hydrapool.name,
        baseUrl,
        critical: hydrapool.critical === true,
        ok: true,
        checks: {},
        errors: [],
        metrics: [],
        allWorkers: [],
        workers: [],
        users: []
    };

    const health = await fetchWithTimeout(`${baseUrl}/health`, { headers }, config.requestTimeoutMs);
    result.checks.health = compactFetchResult(health);
    if (!health.ok) result.errors.push(`health failed: ${health.error || health.status}`);

    const metrics = await fetchWithTimeout(`${baseUrl}/metrics`, { headers }, config.requestTimeoutMs);
    result.checks.metrics = compactFetchResult(metrics);
    if (!metrics.ok) {
        result.errors.push(`metrics failed: ${metrics.error || metrics.status}`);
    } else {
        result.metrics = parsePrometheusMetrics(metrics.text);
        result.allWorkers = extractHydrapoolWorkers(result.metrics);
        result.workers = result.allWorkers.filter(worker => isActiveHydrapoolWorker(worker, config));
        result.users = [...new Set(result.workers.map(worker => worker.btcaddress).filter(Boolean))];
    }

    result.ok = result.errors.length === 0;
    return result;
}

function parsePrometheusMetrics(text) {
    const metrics = [];
    for (const line of String(text || "").split(/\r?\n/)) {
        const trimmed = line.trim();
        if (!trimmed || trimmed.startsWith("#")) continue;
        const match = trimmed.match(/^([a-zA-Z_:][a-zA-Z0-9_:]*)(?:\{([^}]*)\})?\s+([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)/);
        if (!match) continue;
        metrics.push({
            name: match[1],
            labels: parsePrometheusLabels(match[2] || ""),
            value: Number(match[3])
        });
    }
    return metrics;
}

function parsePrometheusLabels(labelText) {
    const labels = {};
    const regex = /([a-zA-Z_][a-zA-Z0-9_]*)="((?:\\.|[^"])*)"/g;
    let match;
    while ((match = regex.exec(labelText)) !== null) {
        labels[match[1]] = match[2]
            .replace(/\\"/g, '"')
            .replace(/\\\\/g, "\\")
            .replace(/\\n/g, "\n");
    }
    return labels;
}

function extractHydrapoolWorkers(metrics) {
    const byWorker = new Map();
    for (const metric of metrics) {
        if (!metric.labels?.btcaddress) continue;
        const btcaddress = normalizeAddress(metric.labels.btcaddress);
        const workername = String(metric.labels.workername || "");
        const id = `${btcaddress}.${workername}`;
        const worker = byWorker.get(id) || {
            id,
            btcaddress,
            workername,
            bestShare: null,
            bestShareEver: null,
            lastShareAt: null,
            validSharesTotal: null
        };

        if (metric.name === "worker_best_share") worker.bestShare = metric.value;
        if (metric.name === "worker_best_share_ever") worker.bestShareEver = metric.value;
        if (metric.name === "worker_last_share_at") worker.lastShareAt = metric.value;
        if (metric.name === "worker_shares_valid_total") worker.validSharesTotal = metric.value;
        byWorker.set(id, worker);
    }
    return [...byWorker.values()].sort((a, b) => a.id.localeCompare(b.id));
}

function isActiveHydrapoolWorker(worker, config) {
    const lastShareAt = Number(worker.lastShareAt);
    if (!Number.isFinite(lastShareAt) || lastShareAt <= 0) {
        return false;
    }
    const maxAgeMinutes = Number(config.thresholds.activeHydrapoolWorkerMaxAgeMinutes || 60);
    const ageMs = Date.now() - (lastShareAt * 1000);
    return ageMs >= 0 && ageMs <= maxAgeMinutes * 60_000;
}

function collectServiceStatus(services) {
    return (services || []).map(service => {
        const active = spawnSync("systemctl", ["is-active", service.name], {
            encoding: "utf8",
            timeout: 5000
        });
        const enabled = spawnSync("systemctl", ["is-enabled", service.name], {
            encoding: "utf8",
            timeout: 5000
        });
        return {
            name: service.name,
            critical: service.critical === true,
            active: active.stdout.trim() || active.stderr.trim() || "unknown",
            enabled: enabled.stdout.trim() || enabled.stderr.trim() || "unknown",
            ok: active.status === 0 && active.stdout.trim() === "active"
        };
    });
}

async function collectSnapshot(config) {
    const gridpoolNodes = [];
    for (const node of config.nodes || []) {
        gridpoolNodes.push(await collectGridPoolNode(node, config));
    }

    const hydrapools = [];
    for (const hydrapool of config.hydrapools || []) {
        hydrapools.push(await collectHydrapool(hydrapool, config));
    }

    const tcpEndpoints = [];
    for (const endpoint of config.tcpEndpoints || []) {
        tcpEndpoints.push(await collectTcpEndpoint(endpoint, config));
    }

    return {
        version: SCRIPT_VERSION,
        monitorName: config.monitorName,
        collectedAtUtc: currentIso(),
        gridpoolNodes,
        hydrapools,
        tcpEndpoints,
        services: collectServiceStatus(config.services || [])
    };
}

function buildAlerts(snapshot, state, config) {
    const alerts = [];
    const firstRun = !state.initialized;

    for (const node of snapshot.gridpoolNodes) {
        if (node.skipped) {
            const prefix = `gridpool:${node.name}:`;
            for (const key of Object.keys(state.failureCounts || {})) {
                if (key.startsWith(prefix)) {
                    resetFailure(state, key);
                }
            }
            continue;
        }

        for (const [checkName, check] of Object.entries(node.checks || {})) {
            const key = `gridpool:${node.name}:${checkName}`;
            if (!check.ok) {
                const count = incrementFailure(state, key);
                if (count >= Number(config.thresholds.endpointFailureConsecutive || 2)) {
                    alerts.push({
                        severity: node.critical ? "critical" : "warning",
                        category: "endpoint-down",
                        fingerprint: key,
                        title: `GridPool ${node.name} ${checkName} unreachable`,
                        detail: `${node.baseUrl} ${check.status || ""} ${check.error || ""}`.trim(),
                        codexEligible: true
                    });
                }
            } else {
                resetFailure(state, key);
            }
        }

        const minPeers = Number(node.minimumPeerCount || 0);
        if (minPeers > 0 && Number(node.summary?.peerCount || 0) < minPeers) {
            alerts.push({
                severity: "warning",
                category: "peer-count-low",
                fingerprint: `gridpool:${node.name}:peer-count-low`,
                title: `GridPool ${node.name} peer count is low`,
                detail: `Saw ${node.summary?.peerCount ?? 0} peers, expected at least ${minPeers}.`,
                codexEligible: true
            });
        }

        maybeAddGridPoolBlockAlerts(alerts, node, state);
        maybeAddHashrateAlerts(alerts, node, state, config);
        maybeAddDatumRejectRateAlert(alerts, node, config);
        maybeAddPeerCompatibilityAlerts(alerts, node);
        maybeAddCoinbaseModeAlert(alerts, node);
        maybeAddNodeVersionVisibilityAlert(alerts, node);
        maybeAddBitcoinNotificationAlert(alerts, node, state);
        maybeAddPeerTipProtectionAlert(alerts, node);
        maybeAddOutboundRelayAlert(alerts, node, config);
    }

    maybeAddConsensusComparisonAlerts(alerts, snapshot, state, config);

    for (const hydrapool of snapshot.hydrapools) {
        for (const [checkName, check] of Object.entries(hydrapool.checks || {})) {
            const key = `hydrapool:${hydrapool.name}:${checkName}`;
            if (!check.ok) {
                const count = incrementFailure(state, key);
                if (count >= Number(config.thresholds.endpointFailureConsecutive || 2)) {
                    alerts.push({
                        severity: hydrapool.critical ? "critical" : "warning",
                        category: "endpoint-down",
                        fingerprint: key,
                        title: `Hydrapool ${hydrapool.name} ${checkName} unreachable`,
                        detail: `${hydrapool.baseUrl} ${check.status || ""} ${check.error || ""}`.trim(),
                        codexEligible: true
                    });
                }
            } else {
                resetFailure(state, key);
            }
        }
    }

    for (const endpoint of snapshot.tcpEndpoints || []) {
        const key = `tcp:${endpoint.name}`;
        if (!endpoint.ok) {
            const count = incrementFailure(state, key);
            if (count >= Number(config.thresholds.endpointFailureConsecutive || 2)) {
                alerts.push({
                    severity: endpoint.critical ? "critical" : "warning",
                    category: "endpoint-down",
                    fingerprint: key,
                    title: `TCP endpoint ${endpoint.name} unreachable`,
                    detail: `${endpoint.host}:${endpoint.port} ${endpoint.error || ""}`.trim(),
                    codexEligible: true
                });
            }
        } else {
            resetFailure(state, key);
        }
    }

    for (const service of snapshot.services || []) {
        const key = `service:${service.name}`;
        if (!service.ok) {
            const count = incrementFailure(state, key);
            if (count >= Number(config.thresholds.endpointFailureConsecutive || 2)) {
                alerts.push({
                    severity: service.critical ? "critical" : "warning",
                    category: "service-inactive",
                    fingerprint: key,
                    title: `${service.name} is not active`,
                    detail: `active=${service.active}; enabled=${service.enabled}`,
                    codexEligible: true
                });
            }
        } else {
            resetFailure(state, key);
        }
    }

    if (!firstRun) {
        maybeAddNewIdentityAlerts(alerts, snapshot, state, config);
        maybeAddUnknownListAddressAlerts(alerts, snapshot, state, config);
    }

    updateKnownSets(snapshot, state, config);
    updateRoundMemory(snapshot, state);
    return alerts;
}

function maybeAddDatumRejectRateAlert(alerts, node, config) {
    const diagnostics = node.summary?.localDatumDiagnostics;
    if (!diagnostics) return;

    const total = Number(diagnostics.totalSubmissions || 0);
    const rejected = Number(diagnostics.rejectedCount || 0);
    const minSubmissions = Number(config.thresholds.datumRejectRateMinSubmissions || 25);
    if (total < minSubmissions || rejected <= 0) return;

    const rejectRate = rejected / total;
    const maxRate = Number(config.thresholds.datumRejectRateMax || 0.10);
    if (rejectRate <= maxRate) return;

    const topReasons = Array.isArray(diagnostics.rejectionReasons)
        ? diagnostics.rejectionReasons
            .slice(0, 4)
            .map(item => `${item.reason || "unknown"}=${item.count}`)
            .join(", ")
        : "";

    alerts.push({
        severity: "warning",
        category: "datum-reject-rate-high",
        fingerprint: `gridpool:${node.name}:datum-reject-rate-high`,
        title: `GridPool ${node.name} DATUM reject rate is high`,
        detail: `${rejected}/${total} rejected (${(rejectRate * 100).toFixed(1)}%) over ${diagnostics.windowSeconds || "--"}s. ${topReasons}`.trim(),
        codexEligible: true
    });
}

function maybeAddPeerCompatibilityAlerts(alerts, node) {
    for (const peer of node.peerRecords || []) {
        const status = String(peer.compatibilityStatus || "").toLowerCase();
        if (!status || status === "compatible" || status === "unknown") continue;
        const endpoint = peer.endpoint || peer.url || peer.address || peer.nodeId || "unknown-peer";
        alerts.push({
            severity: status === "incompatible" ? "warning" : "info",
            category: "peer-version-mismatch",
            fingerprint: `gridpool:${node.name}:peer-version:${endpoint}:${status}`,
            title: `GridPool ${node.name} sees peer compatibility issue`,
            detail: `${endpoint}: ${peer.compatibilityStatus}${peer.compatibilityReason ? ` - ${peer.compatibilityReason}` : ""}`,
            codexEligible: status === "incompatible"
        });
    }
}

function maybeAddCoinbaseModeAlert(alerts, node) {
    if (node.suppressCoinbaseModeAlert === true) return;

    const mode = String(node.summary?.coinbaseOutputMode || "").toLowerCase();
    if (!mode || mode === "condensed") return;
    alerts.push({
        severity: "warning",
        category: "coinbase-stress-mode",
        fingerprint: `gridpool:${node.name}:coinbase-mode:${mode}`,
        title: `GridPool ${node.name} is serving non-standard coinbase outputs`,
        detail: `coinbaseOutputMode=${node.summary?.coinbaseOutputMode}; coinbaseOutputCount=${node.summary?.coinbaseOutputCount ?? "--"}. This should be lab-only firmware stress testing.`,
        codexEligible: true
    });
}

function maybeAddNodeVersionVisibilityAlert(alerts, node) {
    if (!node.summary) return;
    const version = node.version || {};
    if (version.consensusVersion != null && version.stateBundleSchemaVersion != null) return;
    alerts.push({
        severity: "warning",
        category: "node-version-missing",
        fingerprint: `gridpool:${node.name}:node-version-missing`,
        title: `GridPool ${node.name} does not expose protocol version fields`,
        detail: "Upgrade this node before public package release so peers/operators can see consensus and state-bundle schema compatibility.",
        codexEligible: true
    });
}

function maybeAddBitcoinNotificationAlert(alerts, node, state) {
    const notification = node.summary?.bitcoinNotification;
    if (!notification) return;

    const mode = String(notification.mode || "").toLowerCase();
    if (mode !== "attached-node") return;

    if (notification.miningSafe === false) {
        alerts.push({
            severity: "critical",
            category: "bitcoin-source-degraded",
            fingerprint: `gridpool:${node.name}:bitcoin-source-degraded`,
            title: `GridPool ${node.name} attached Bitcoin source is unsafe`,
            detail: notification.degradedReason || notification.rpc?.lastError ||
                "Authenticated Bitcoin RPC is not synchronized.",
            codexEligible: true
        });
    } else if (notification.degradedReason) {
        alerts.push({
            severity: "warning",
            category: "bitcoin-zmq-degraded",
            fingerprint: `gridpool:${node.name}:bitcoin-zmq-degraded`,
            title: `GridPool ${node.name} Bitcoin ZMQ latency path is degraded`,
            detail: notification.degradedReason,
            codexEligible: true
        });
    }

    state.bitcoinNotificationCounters ||= {};
    const previous = state.bitcoinNotificationCounters[node.name] || {};
    const current = {};
    for (const topic of notification.zmqTopics || []) {
        const key = `${topic.topic || "unknown"}|${topic.endpointLabel || ""}`;
        current[key] = {
            gaps: Number(topic.sequenceGapCount || 0),
            resets: Number(topic.resetCount || 0)
        };
        const before = previous[key];
        if (before && (current[key].gaps > Number(before.gaps || 0) ||
            current[key].resets > Number(before.resets || 0))) {
            alerts.push({
                severity: "warning",
                category: "bitcoin-zmq-sequence-anomaly",
                fingerprint: `gridpool:${node.name}:bitcoin-zmq-sequence:${key}`,
                title: `GridPool ${node.name} observed a Bitcoin ZMQ sequence anomaly`,
                detail: `${key}: gaps ${before.gaps || 0}->${current[key].gaps}, resets ${before.resets || 0}->${current[key].resets}. RPC reconciliation should verify the active tip.`,
                codexEligible: true
            });
        }
    }
    state.bitcoinNotificationCounters[node.name] = current;
}

function maybeAddPeerTipProtectionAlert(alerts, node) {
    if (!node.summary || node.summary.miningWorkSafe !== false) return;
    alerts.push({
        severity: "critical",
        category: "local-bitcoin-lagging",
        fingerprint: `gridpool:${node.name}:local-bitcoin-lagging`,
        title: `GridPool ${node.name} paused stale-parent mining work`,
        detail: node.summary.miningWorkSafetyReason ||
            `Peer tip ${shortId(node.summary.provisionalTipBlockHash) || "--"} remains unconfirmed by the local Bitcoin node.`,
        codexEligible: false
    });
}

function maybeAddOutboundRelayAlert(alerts, node, config) {
    const summary = node.summary;
    if (!summary) return;
    const datumHashrate = Number((node.localMining || currentLocalMining(node, config)).sources
        .find(source => source.source === "datum")?.hashrateThs || 0);
    const staleMs = Number(config.thresholds.outboundRelayStaleMinutes || 10) * 60_000;
    const lastLocalShare = parseDate(summary.lastValidLocalDatumShareUtc);
    const datumSessionOpened = parseDate(summary.lastDatumSessionOpenedUtc);
    const lastRelay = parseDate(summary.lastSuccessfulOutboundRelayUtc);
    const localShareRecent = lastLocalShare && Date.now() - lastLocalShare <= staleMs;
    const localInputReference = lastLocalShare || datumSessionOpened;
    const localInputStale = localInputReference && Date.now() - localInputReference > staleMs;
    const relayStale = summary.outboundRelayHealthy === false ||
        (datumHashrate > 0 && lastRelay && Date.now() - lastRelay > staleMs);
    if (datumHashrate > 0 && localShareRecent && relayStale) {
        alerts.push({
            severity: "critical",
            category: "outbound-relay-stale",
            fingerprint: `gridpool:${node.name}:outbound-relay-stale`,
            title: `GridPool ${node.name} has local hashrate but outbound relay is stale`,
            detail: summary.outboundRelayHealthReason || `datumHashrateThs=${datumHashrate}; lastSuccessfulOutboundRelayUtc=${summary.lastSuccessfulOutboundRelayUtc || "never"}`,
            codexEligible: true
        });
    } else if (datumHashrate > 0 && localInputStale && Number(summary.activeDatumSessionCount || 0) > 0) {
        alerts.push({
            severity: "warning",
            category: "local-datum-share-stale",
            fingerprint: `gridpool:${node.name}:local-datum-share-stale`,
            title: `GridPool ${node.name} DATUM session is alive but miner shares stopped`,
            detail: `datumHashrateThs=${datumHashrate}; lastValidLocalDatumShareUtc=${summary.lastValidLocalDatumShareUtc || "never"}; lastCoinbaserResponseUtc=${summary.lastSuccessfulDatumCoinbaserResponseUtc || "never"}. Check the local Stratum worker or rental failover before diagnosing peer relay.`,
            codexEligible: true
        });
    }

    const attemptStaleMs = Number(config.thresholds.peerOutboundAttemptStaleMinutes || 10) * 60_000;
    const stalePeers = (node.peerRecords || []).filter(peer => {
        const attempt = parseDate(peer.lastAttemptUtc);
        const lastSuccess = parseDate(peer.lastSuccessUtc);
        const lastSeen = parseDate(peer.lastSeenUtc);
        const lastActivity = [lastSuccess, lastSeen]
            .filter(Boolean)
            .reduce((latest, value) => latest && latest > value ? latest : value, null);
        return peer.sessionConnected === true &&
            attempt &&
            Date.now() - attempt > attemptStaleMs &&
            (!lastActivity || Date.now() - lastActivity > attemptStaleMs);
    });
    if (stalePeers.length > 0) {
        alerts.push({
            severity: "warning",
            category: "peer-outbound-attempt-stale",
            fingerprint: `gridpool:${node.name}:peer-outbound-attempt-stale:${stalePeers.map(peer => peer.endpoint).sort().join("|")}`,
            title: `GridPool ${node.name} has connected sessions with frozen outbound polling`,
            detail: stalePeers.slice(0, 5).map(peer => `${peer.endpoint}=attempt:${peer.lastAttemptUtc || "never"},tip:${shortId(peer.lastTipBlockHash) || "--"},state:${shortId(peer.lastCurrentStateId) || "--"}`).join("; "),
            codexEligible: true
        });
    }
}

function maybeAddConsensusComparisonAlerts(alerts, snapshot, state, config) {
    for (const group of buildConsensusReport(snapshot, config)) {
        if (group.nodes.length < 2) continue;

        addDivergenceAlertForField(alerts, state, config, group, "consensusVersion", "consensus version", true);
        addDivergenceAlertForField(alerts, state, config, group, "stateBundleSchemaVersion", "state schema version", true);
        addConsensusStateDivergenceAlert(alerts, state, config, group);

        const hardDivergence = ["consensusVersion", "stateBundleSchemaVersion", "currentStateId", "activeSnapshotId"]
            .some(fieldName => fieldDiverges(group, fieldName));
        if (!hardDivergence) {
            addDivergenceAlertForField(alerts, state, config, group, "candidateStateId", "candidate state", false);
        } else {
            resetFailure(state, `consensus:${group.groupKey}:candidateStateId`);
        }


        const nodesByTip = new Map();
        for (const node of group.nodes) {
            const tip = normalizeId(node.currentTipBlockHash);
            if (!tip) continue;
            if (!nodesByTip.has(tip)) nodesByTip.set(tip, []);
            nodesByTip.get(tip).push(node);
        }
        for (const [tip, nodes] of nodesByTip) {
            const heights = [...new Set(nodes.map(node => node.currentTipBlockHeight).filter(value => value != null))];
            if (nodes.length > 1 && heights.length > 1) {
                alerts.push({
                    severity: "warning",
                    category: "tip-metadata-mismatch",
                    fingerprint: `consensus:${group.groupKey}:tip-height:${tip}`,
                    title: `GridPool ${group.groupKey} reports inconsistent heights for one Bitcoin tip`,
                    detail: nodes.map(node => `${node.name}=${node.currentTipBlockHeight ?? "--"}`).join(", "),
                    codexEligible: false
                });
            }
        }

        const workSetCounts = group.nodes
            .map(node => Number(node.workSetCount))
            .filter(value => Number.isFinite(value));
        const reserveLimits = group.nodes
            .map(node => Number(node.workSetReserveLimit))
            .filter(value => Number.isFinite(value) && value > 0);
        const expectedReserve = reserveLimits.length ? Math.max(...reserveLimits) : 0;
        const minWorkSet = workSetCounts.length ? Math.min(...workSetCounts) : 0;
        if (expectedReserve > 0 && minWorkSet < expectedReserve * 0.9) {
            alerts.push({
                severity: "warning",
                category: "work-set-underfilled",
                fingerprint: `consensus:${group.groupKey}:work-set-underfilled`,
                title: `GridPool ${group.groupKey} Work Set is underfilled on at least one node`,
                detail: group.nodes.map(node => `${node.name}=${node.workSetCount}/${node.workSetReserveLimit}`).join(", "),
                codexEligible: true
            });
        }
    }
}

function consensusStateFingerprint(group) {
    const values = group.nodes.map(node =>
        `${node.name}:${normalizeId(node.currentStateId)}:${normalizeId(node.activeSnapshotId)}`);
    return `consensus:${group.groupKey}:state-snapshot:${values.sort().join("|")}`;
}

function compactProofSetDiff(group) {
    const sourceNodes = group.nodes.map(compact => ({
        compact,
        source: compact.__source,
        ids: new Set(compact.__source?.currentState?.activeSnapshotProofIds || [])
    })).filter(node => node.ids.size > 0);
    if (sourceNodes.length < 2) return "proofDiff=unavailable";
    const [left, right] = sourceNodes;
    const intersection = [...left.ids].filter(id => right.ids.has(id));
    const leftOnly = [...left.ids].filter(id => !right.ids.has(id));
    const rightOnly = [...right.ids].filter(id => !left.ids.has(id));
    const describeTop = (node, ids) => {
        const bundle = node.source.currentState || {};
        const proofs = [...(bundle.shareProofs || []), ...(bundle.workSetProofs || [])];
        const byId = new Map(proofs.map(proof => [proof.shareId, proof]));
        const top = ids.map(id => byId.get(id)).filter(Boolean)
            .sort((a, b) => Number(b.difficulty || 0) - Number(a.difficulty || 0))[0];
        return top ? `${Number(top.difficulty || 0).toPrecision(4)}@${String(top.source || "unknown").slice(0, 40)}` : "--";
    };
    return `proofDiff intersection=${intersection.length}, ${left.compact.name}-only=${leftOnly.length} top=${describeTop(left, leftOnly)}, ${right.compact.name}-only=${rightOnly.length} top=${describeTop(right, rightOnly)}`;
}

function addConsensusStateDivergenceAlert(alerts, state, config, group) {
    const key = `consensus:${group.groupKey}:state-snapshot`;
    const diverged = fieldDiverges(group, "currentStateId") || fieldDiverges(group, "activeSnapshotId");
    // A snapshot is keyed to the Bitcoin tip. Different states are expected while
    // nodes are briefly at different heights and do not represent a consensus fork.
    const knownHeights = group.nodes
        .map(node => Number(node.currentTipBlockHeight))
        .filter(Number.isFinite);
    const comparableBoundary = knownHeights.length !== group.nodes.length ||
        new Set(knownHeights).size === 1;
    if (!diverged || !comparableBoundary) {
        resetFailure(state, key);
        return;
    }
    const count = incrementFailure(state, key);
    if (count < Number(config.thresholds.consensusDivergenceConsecutive || 2)) return;
    alerts.push({
        severity: "critical",
        category: "consensus-divergence",
        fingerprint: consensusStateFingerprint(group),
        title: `GridPool ${group.groupKey} current state / active snapshot diverged`,
        detail: `${group.nodes.map(node => `${node.name}=state:${shortId(node.currentStateId) || "--"},snapshot:${shortId(node.activeSnapshotId) || "--"}`).join("; ")}; ${compactProofSetDiff(group)}`,
        codexEligible: true
    });
}

function addDivergenceAlertForField(alerts, state, config, group, fieldName, label, immediate) {
    const values = divergentValues(group, fieldName);
    const key = `consensus:${group.groupKey}:${fieldName}`;
    if (values.length <= 1) {
        resetFailure(state, key);
        return;
    }

    const count = incrementFailure(state, key);
    const threshold = immediate
        ? Number(config.thresholds.consensusDivergenceConsecutive || 2)
        : Number(config.thresholds.candidateDivergenceConsecutive || 3);
    if (count < threshold) return;

    if (!immediate) {
        const firstSeen = parseDate(state.failureFirstSeenUtc?.[key]);
        const minimumMinutes = Number(config.thresholds.candidateDivergenceMinimumMinutes ?? 10);
        if (minimumMinutes > 0 && (!firstSeen || Date.now() - firstSeen < minimumMinutes * 60_000)) {
            return;
        }
    }

    alerts.push({
        severity: immediate ? "critical" : "warning",
        category: immediate ? "consensus-divergence" : "candidate-divergence",
        fingerprint: key,
        title: `GridPool ${group.groupKey} ${label} diverged`,
        detail: group.nodes.map(node => `${node.name}=${shortId(node[fieldName]) || "--"}`).join(", "),
        codexEligible: true
    });
}

function fieldDiverges(group, fieldName) {
    return divergentValues(group, fieldName).length > 1;
}

function divergentValues(group, fieldName) {
    return [...new Set(group.nodes.map(node => normalizeId(node[fieldName])).filter(Boolean))];
}

function incrementFailure(state, key) {
    state.failureCounts[key] = Number(state.failureCounts[key] || 0) + 1;
    state.failureFirstSeenUtc ||= {};
    if (!state.failureFirstSeenUtc[key]) {
        state.failureFirstSeenUtc[key] = currentIso();
    }
    return state.failureCounts[key];
}

function resetFailure(state, key) {
    state.failureCounts[key] = 0;
    if (state.failureFirstSeenUtc) {
        delete state.failureFirstSeenUtc[key];
    }
}

function maybeAddGridPoolBlockAlerts(alerts, node, state) {
    const summary = node.summary || {};
    const blockHash = summary.lastGridPoolBlockHash;
    const blockMemory = state.lastGridPoolBlockByNode || {};
    const hasPreviousBlockMemory = Object.prototype.hasOwnProperty.call(blockMemory, node.name);
    const previousBlockHash = blockMemory[node.name]?.hash || null;
    if (blockHash && hasPreviousBlockMemory && blockHash !== previousBlockHash) {
        alerts.push({
            severity: "critical",
            category: "gridpool-block-found",
            fingerprint: `gridpool:${node.name}:block:${blockHash}`,
            title: `GridPool block observed on ${node.name}`,
            detail: `height=${summary.lastGridPoolBlockHeight || "--"} miner=${summary.lastGridPoolBlockMinerAddress || "--"} paidSnapshot=${summary.lastPaidSnapshotId || "--"} hash=${blockHash}`,
            codexEligible: true
        });
    }
}

function maybeAddHashrateAlerts(alerts, node, state, config) {
    const observed = Number(node.summary?.currentRoundObservedHashrateThs);

    if (node.suppressTeamHashrateAlerts !== true) {
        addHashrateSample(state, `${node.name}:team`, observed);
        const teamAlert = trendAlertForSeries(state, `${node.name}:team`, config, `GridPool ${node.name} team hashrate`);
        if (teamAlert) alerts.push(teamAlert);
    }

    const localMining = currentLocalMining(node, config);
    node.localMining = localMining;
    if (node.suppressLocalMiningHashrateAlerts !== true && localMining.hashrateThs > 0) {
        addHashrateSample(state, `${node.name}:local`, localMining.hashrateThs);
        const localAlert = trendAlertForSeries(
            state,
            `${node.name}:local`,
            config,
            `GridPool ${node.name} local mining hashrate`,
            localMining.description);
        if (localAlert) alerts.push(localAlert);
    } else if (!localMining.hasFreshSamples) {
        // Do not compare an eventual reconnect with samples from a prior
        // mining session. That produces false spike/drop notifications.
        delete state.hashrateHistory[`${node.name}:local`];
    }
}

function currentLocalMining(node, config) {
    const freshnessMinutes = Number(config.thresholds.localMiningFreshnessMinutes || 20);
    const cutoffMs = Date.now() - (freshnessMinutes * 60_000);
    const bySource = new Map();
    let minerCount = 0;
    let latestShareMs = null;

    for (const miner of node.localMiners || []) {
        const hashrateThs = Number(miner.currentHashrateThs);
        const lastShareMs = parseDate(miner.lastShareUtc);
        if (!Number.isFinite(hashrateThs) || hashrateThs <= 0 || !lastShareMs || lastShareMs < cutoffMs) {
            continue;
        }

        const source = String(miner.source || "unknown").trim().toLowerCase() || "unknown";
        bySource.set(source, (bySource.get(source) || 0) + hashrateThs);
        minerCount += 1;
        latestShareMs = latestShareMs == null ? lastShareMs : Math.max(latestShareMs, lastShareMs);
    }

    // Older nodes did not expose per-miner telemetry. Retain a conservative
    // DATUM-only fallback for that compatibility case, never when the modern
    // endpoint positively reports a different local source.
    if (!Array.isArray(node.localMiners)) {
        const legacyDatumHashrateThs = Number(node.summary?.localDatumHashrateThs);
        if (Number.isFinite(legacyDatumHashrateThs) && legacyDatumHashrateThs > 0) {
            bySource.set("datum", legacyDatumHashrateThs);
            minerCount = 1;
        }
    }

    const sources = [...bySource.entries()]
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([source, hashrateThs]) => ({ source, hashrateThs }));
    const hashrateThs = sources.reduce((sum, source) => sum + source.hashrateThs, 0);
    const latestShareUtc = latestShareMs == null ? null : new Date(latestShareMs).toISOString();
    const description = hashrateThs > 0
        ? `Fresh sources: ${sources.map(source => `${source.source}=${formatHashrateThs(source.hashrateThs)}`).join(", ")}; ${minerCount} miner(s); latest share ${latestShareUtc || "unavailable (legacy node telemetry)"}.`
        : `No local miner has submitted a share in the last ${freshnessMinutes} minute(s).`;

    return {
        hashrateThs,
        hasFreshSamples: hashrateThs > 0,
        sources,
        minerCount,
        latestShareUtc,
        description
    };
}

function addHashrateSample(state, key, value) {
    if (!Number.isFinite(value) || value <= 0) return;
    const history = state.hashrateHistory[key] || [];
    history.push({ utc: currentIso(), value });
    state.hashrateHistory[key] = history.slice(-24);
}

function trendAlertForSeries(state, key, config, label, context = "") {
    const samplesNeeded = Number(config.thresholds.hashrateSamplesForTrend || 3);
    const history = state.hashrateHistory[key] || [];
    if (history.length < samplesNeeded * 2) return null;

    const recent = history.slice(-samplesNeeded).map(item => Number(item.value));
    const previous = history.slice(-(samplesNeeded * 2), -samplesNeeded).map(item => Number(item.value));
    const recentAvg = avg(recent);
    const previousAvg = avg(previous);
    const min = Number(config.thresholds.minimumHashrateThsForTrend || 1);
    if (!Number.isFinite(recentAvg) || !Number.isFinite(previousAvg) || previousAvg < min) return null;

    const dropFraction = Number(config.thresholds.hashrateDropFraction || 0.35);
    const spikeMultiplier = Number(config.thresholds.hashrateSpikeMultiplier || 2);

    if (recentAvg <= previousAvg * (1 - dropFraction)) {
        return {
            severity: "warning",
            category: "hashrate-drop",
            fingerprint: `hashrate:${key}:drop`,
            title: `${label} dropped`,
            detail: `${formatHashrateThs(previousAvg)} -> ${formatHashrateThs(recentAvg)} across ${samplesNeeded} samples.${context ? ` ${context}` : ""}`,
            codexEligible: true
        };
    }

    if (recentAvg >= previousAvg * spikeMultiplier) {
        return {
            severity: "info",
            category: "hashrate-spike",
            fingerprint: `hashrate:${key}:spike`,
            title: `${label} spiked`,
            detail: `${formatHashrateThs(previousAvg)} -> ${formatHashrateThs(recentAvg)} across ${samplesNeeded} samples.${context ? ` ${context}` : ""}`,
            codexEligible: false
        };
    }

    return null;
}

function avg(values) {
    const valid = values.filter(value => Number.isFinite(value));
    if (!valid.length) return null;
    return valid.reduce((sum, value) => sum + value, 0) / valid.length;
}

function maybeAddNewIdentityAlerts(alerts, snapshot, state) {
    const knownDatum = new Set((state.known.datumAddresses || []).map(normalizeAddress));
    for (const address of currentDatumAddresses(snapshot)) {
        if (!knownDatum.has(address)) {
            alerts.push({
                severity: "info",
                category: "new-datum-user",
                fingerprint: `datum:${address}`,
                title: "New local DATUM miner address",
                detail: address,
                codexEligible: false
            });
        }
    }

    const knownWorkers = new Set(state.known.hydrapoolWorkers || []);
    for (const worker of currentHydrapoolWorkers(snapshot)) {
        if (!knownWorkers.has(worker.id)) {
            alerts.push({
                severity: "info",
                category: "new-hydrapool-worker",
                fingerprint: `hydrapool-worker:${worker.id}`,
                title: "New Hydrapool Stratum worker",
                detail: `${worker.btcaddress}${worker.workername ? `.${worker.workername}` : ""}`,
                codexEligible: false
            });
        }
    }

    const knownPeers = new Set(state.known.peers || []);
    for (const peer of currentPeers(snapshot)) {
        if (!knownPeers.has(peer)) {
            alerts.push({
                severity: "info",
                category: "new-peer",
                fingerprint: `peer:${peer}`,
                title: "New GridPool peer visible",
                detail: peer,
                codexEligible: false
            });
        }
    }
}

function maybeAddUnknownListAddressAlerts(alerts, snapshot, state, config) {
    const known = knownAddressSet(snapshot, config);
    const priorUnknown = new Set((state.known.unknownListAddresses || []).map(normalizeAddress));
    const listAddresses = currentListAddresses(snapshot);
    for (const address of listAddresses) {
        if (known.has(address) || priorUnknown.has(address)) {
            continue;
        }

        alerts.push({
            severity: "warning",
            category: "unknown-list-address",
            fingerprint: `unknown-list-address:${address}`,
            title: "Address on GridPool list is not local DATUM or Hydrapool",
            detail: address,
            codexEligible: true
        });
    }
}

function knownAddressSet(snapshot, config) {
    const known = new Set();
    for (const item of config.knownAddresses || []) {
        known.add(normalizeAddress(typeof item === "string" ? item : item.address));
    }
    for (const address of currentDatumAddresses(snapshot)) {
        known.add(address);
    }
    for (const worker of currentHydrapoolWorkers(snapshot)) {
        known.add(worker.btcaddress);
    }
    return known;
}

function currentDatumAddresses(snapshot) {
    return [...new Set((snapshot.gridpoolNodes || [])
        .flatMap(node => node.localMiners || [])
        .map(miner => normalizeAddress(miner.address || miner.username))
        .filter(Boolean))];
}

function currentHydrapoolWorkers(snapshot) {
    return (snapshot.hydrapools || []).flatMap(hydrapool => hydrapool.workers || []);
}

function currentPeers(snapshot) {
    return [...new Set((snapshot.gridpoolNodes || []).flatMap(node => node.peers || []).filter(Boolean))].sort();
}

function buildConsensusReport(snapshot, config) {
    const groups = new Map();
    for (const node of snapshot.gridpoolNodes || []) {
        if (!node.ok || !node.summary) continue;
        const groupKey = node.consensusGroup ||
            node.summary.networkId ||
            node.networkKey ||
            node.name;
        if (!groups.has(groupKey)) {
            groups.set(groupKey, []);
        }
        const compact = compactNodeConsensus(node);
        Object.defineProperty(compact, "__source", { value: node, enumerable: false });
        groups.get(groupKey).push(compact);
    }

    return [...groups.entries()].map(([groupKey, nodes]) => {
        const fields = [
            "consensusVersion",
            "stateBundleSchemaVersion",
            "httpApiVersion",
            "peerTransportVersion",
            "udpRelayVersion",
            "currentStateId",
            "candidateStateId",
            "activeSnapshotId",
            "lastPaidSnapshotId",
            "currentTipBlockHash",
            "currentTipBlockHeight"
        ];
        const divergences = {};
        for (const field of fields) {
            const values = [...new Set(nodes.map(node => normalizeId(node[field])).filter(Boolean))];
            if (values.length > 1) {
                divergences[field] = values;
            }
        }

        return {
            groupKey,
            nodeCount: nodes.length,
            aligned: Object.keys(divergences).length === 0,
            divergences,
            nodes
        };
    }).sort((a, b) => a.groupKey.localeCompare(b.groupKey));
}

function compactNodeConsensus(node) {
    const summary = node.summary || {};
    const version = node.version || {};
    return {
        name: node.name,
        baseUrl: node.baseUrl,
        networkId: summary.networkId || "",
        bitcoinNetwork: summary.bitcoinNetwork || "",
        consensusVersion: version.consensusVersion ?? summary.consensusVersion ?? null,
        stateBundleSchemaVersion: version.stateBundleSchemaVersion ?? summary.stateBundleSchemaVersion ?? null,
        httpApiVersion: version.httpApiVersion ?? summary.httpApiVersion ?? null,
        peerTransportVersion: version.peerTransportVersion ?? summary.peerTransportVersion ?? null,
        udpRelayVersion: version.udpRelayVersion ?? summary.udpRelayVersion ?? null,
        releaseVersion: version.releaseVersion ?? summary.releaseVersion ?? "",
        currentRoundNumber: summary.currentRoundNumber ?? null,
        currentStateId: summary.currentStateId || "",
        candidateStateId: summary.candidateStateId || "",
        activeSnapshotId: summary.activeSnapshotId || "",
        lastPaidSnapshotId: summary.lastPaidSnapshotId || "",
        currentTipBlockHash: summary.currentTipBlockHash || "",
        currentTipBlockHeight: summary.currentTipBlockHeight ?? null,
        currentTipCompactTarget: summary.currentTipCompactTarget ?? null,
        peerTipStaleProtectionEnabled: summary.peerTipStaleProtectionEnabled ?? false,
        miningWorkSafe: summary.miningWorkSafe ?? true,
        localBitcoinLagging: summary.localBitcoinLagging ?? false,
        miningWorkSafetyReason: summary.miningWorkSafetyReason || "",
        provisionalTipBlockHash: summary.provisionalTipBlockHash || "",
        provisionalSnapshotId: summary.provisionalSnapshotId || "",
        workSetCount: summary.workSetCount ?? null,
        workSetReserveLimit: summary.workSetReserveLimit ?? null,
        activeSnapshotProofCount: summary.activeSnapshotProofCount ?? null,
        coinbaseOutputMode: summary.coinbaseOutputMode || "",
        coinbaseOutputCount: summary.coinbaseOutputCount ?? null,
        currentStateTotalDifficulty: summary.currentStateTotalDifficulty ?? null,
        onDeckTotalDifficulty: summary.onDeckTotalDifficulty ?? null,
        teamHashrateThs: summary.currentRoundObservedHashrateThs ?? null,
        localHashrateThs: summary.localDatumHashrateThs ?? null,
        peerCount: summary.peerCount ?? null,
        supportFeeEnabled: summary.supportFeeEnabled ?? null,
        payoutVariant: summary.payoutVariant || "",
        datumAcceptanceRate: datumAcceptanceRate(summary.localDatumDiagnostics)
    };
}

function datumAcceptanceRate(diagnostics) {
    const total = Number(diagnostics?.totalSubmissions || 0);
    if (!Number.isFinite(total) || total <= 0) return null;
    const accepted = Number(diagnostics?.acceptedCount || 0);
    return accepted / total;
}

function normalizeId(value) {
    if (value == null) return "";
    return String(value).trim().toLowerCase();
}

function shortId(value) {
    const text = String(value || "").trim();
    if (!text) return "";
    if (text.length <= 16) return text;
    return `${text.slice(0, 8)}...${text.slice(-6)}`;
}

function currentListAddresses(snapshot) {
    return [...new Set((snapshot.gridpoolNodes || [])
        .flatMap(node => [...(node.payoutAddresses || []), ...(node.candidateAddresses || [])])
        .map(normalizeAddress)
        .filter(Boolean))];
}

function updateKnownSets(snapshot, state, config) {
    state.known.datumAddresses = unionSorted(state.known.datumAddresses, currentDatumAddresses(snapshot));
    state.known.hydrapoolWorkers = unionSorted(state.known.hydrapoolWorkers, currentHydrapoolWorkers(snapshot).map(worker => worker.id));
    state.known.peers = unionSorted(state.known.peers, currentPeers(snapshot));
    const known = knownAddressSet(snapshot, config);
    const unknown = currentListAddresses(snapshot).filter(address => !known.has(address));
    state.known.unknownListAddresses = unionSorted(state.known.unknownListAddresses, unknown);
}

function updateRoundMemory(snapshot, state) {
    for (const node of snapshot.gridpoolNodes || []) {
        state.lastRoundByNode[node.name] = {
            roundNumber: node.summary?.currentRoundNumber ?? null,
            currentStateId: node.summary?.currentStateId ?? null,
            activeSnapshotId: node.summary?.activeSnapshotId ?? null,
            lastPaidSnapshotId: node.summary?.lastPaidSnapshotId ?? null,
            currentTipBlockHash: node.summary?.currentTipBlockHash ?? null,
            currentTipBlockHeight: node.summary?.currentTipBlockHeight ?? null,
            lastRotationUtc: node.summary?.lastRotationUtc ?? null
        };
        state.lastGridPoolBlockByNode[node.name] = {
            hash: node.summary?.lastGridPoolBlockHash || null,
            height: node.summary?.lastGridPoolBlockHeight || null,
            utc: node.summary?.lastGridPoolBlockUtc || null,
            miner: node.summary?.lastGridPoolBlockMinerAddress || null,
            paidSnapshotId: node.summary?.lastPaidSnapshotId || null
        };
    }
}

function unionSorted(a, b) {
    return [...new Set([...(a || []), ...(b || [])].filter(Boolean))].sort();
}

function filterAlertsForDelivery(alerts, state, config) {
    const delivered = [];
    const now = Date.now();
    const nowUtc = currentIso();
    const mutedUntil = parseDate(state.telegram.silencedUntilUtc);
    const muted = mutedUntil && mutedUntil > now;
    const muteCritical = muted && state.telegram.silenceCritical === true;
    const cooldownMs = Number(config.alertCooldownMinutes || 60) * 60_000;
    state.openAlertLifecycles ||= {};

    const activeLifecycles = new Map(
        alerts
            .filter(isLifecycleManagedAlert)
            .map(alert => [alert.fingerprint, alert]));

    for (const [fingerprint, alert] of activeLifecycles) {
        const existing = state.openAlertLifecycles[fingerprint];
        const lifecycle = existing || {
            severity: alert.severity,
            category: alert.category,
            title: alert.title,
            detail: alert.detail,
            openedUtc: nowUtc,
            announcedUtc: null
        };
        lifecycle.severity = alert.severity;
        lifecycle.category = alert.category;
        lifecycle.title = alert.title;
        lifecycle.detail = alert.detail;
        lifecycle.lastSeenUtc = nowUtc;
        state.openAlertLifecycles[fingerprint] = lifecycle;

        const critical = alert.severity === "critical";
        const deliveryMuted = muted && (!critical || muteCritical);
        if ((!existing || !lifecycle.announcedUtc) && !deliveryMuted) {
            lifecycle.announcedUtc = nowUtc;
            addDeliveredAlert(delivered, state, alert);
        }
    }

    for (const [fingerprint, lifecycle] of Object.entries(state.openAlertLifecycles)) {
        if (activeLifecycles.has(fingerprint)) continue;
        delete state.openAlertLifecycles[fingerprint];
        if (!lifecycle.announcedUtc) continue;

        const resolved = {
            severity: "info",
            category: "alert-resolved",
            fingerprint: `resolved:${fingerprint}:${lifecycle.openedUtc || nowUtc}`,
            title: `Resolved: ${lifecycle.title}`,
            detail: `Recovered at ${nowUtc}; open since ${lifecycle.openedUtc || "unknown"}.`,
            codexEligible: false
        };
        if (!(muted && !muteCritical)) {
            addDeliveredAlert(delivered, state, resolved);
        }
    }

    for (const alert of alerts) {
        if (isLifecycleManagedAlert(alert)) continue;
        const critical = alert.severity === "critical" || alert.category === "gridpool-block-found";
        if (muted && (!critical || muteCritical)) continue;

        const last = parseDate(state.alertCooldowns[alert.fingerprint]);
        const consensusReminder = alert.category === "consensus-divergence";
        if (last && now - last < cooldownMs && (!critical || consensusReminder)) continue;

        state.alertCooldowns[alert.fingerprint] = currentIso();
        addDeliveredAlert(delivered, state, alert);
    }

    state.recentAlerts = (state.recentAlerts || []).slice(0, 50);
    return delivered;
}

function isLifecycleManagedAlert(alert) {
    return alert.category === "endpoint-down" || alert.category === "service-inactive";
}

function addDeliveredAlert(delivered, state, alert) {
    delivered.push(alert);
    state.recentAlerts ||= [];
    state.recentAlerts.unshift({
        ...alert,
        utc: currentIso()
    });
}

function stableIncidentKey(alert) {
    const fingerprint = String(alert?.fingerprint || "");
    if (fingerprint.startsWith("consensus:")) {
        return fingerprint.split(":").slice(0, 3).join(":");
    }
    return fingerprint || `${alert?.category || "alert"}:${alert?.title || "unknown"}`;
}

function selectNewIncidentAlerts(alerts, state) {
    const previous = new Set(state.activeIncidentKeys || []);
    const active = alerts
        .filter(alert => alert.severity === "warning" || alert.severity === "critical");
    const newlyActive = active.filter(alert => !previous.has(stableIncidentKey(alert)));
    state.activeIncidentKeys = [...new Set(active.map(stableIncidentKey))].sort();
    return newlyActive;
}

function safeFileComponent(value) {
    return String(value || "unknown").replace(/[^a-zA-Z0-9_.-]+/g, "_").slice(0, 100);
}

function nodesForIncidentAlerts(alerts, snapshot) {
    const nodes = snapshot.gridpoolNodes || [];
    const selectedGroups = new Set();
    const selectedNames = new Set();

    for (const alert of alerts) {
        const parts = String(alert.fingerprint || "").split(":");
        if (parts[0] === "consensus" && parts[1]) selectedGroups.add(parts[1]);
        if (parts[0] === "gridpool" && parts[1]) selectedNames.add(parts[1]);
        for (const node of nodes) {
            const nodeName = String(node.name || "").toLowerCase();
            if (parts.some(part => {
                const value = String(part || "").toLowerCase();
                return value === nodeName || value.includes(nodeName);
            })) {
                selectedNames.add(node.name);
            }
        }
    }

    for (const node of nodes) {
        if (selectedNames.has(node.name) && node.consensusGroup) selectedGroups.add(node.consensusGroup);
    }

    const selected = nodes.filter(node =>
        selectedNames.has(node.name) ||
        (node.consensusGroup && selectedGroups.has(node.consensusGroup)));
    return selected.length ? selected : nodes;
}

function incidentFetchPayload(result) {
    return {
        capturedAtUtc: currentIso(),
        ok: result.ok,
        status: result.status,
        url: result.url,
        durationMs: result.durationMs,
        error: result.error || null,
        data: result.json ?? result.text ?? null
    };
}

async function captureIncidentDiagnostics(alerts, snapshot, stateDir, config) {
    if (!alerts.length || config.incidentCapture?.enabled === false) return null;

    const startedUtc = currentIso();
    const suffix = safeFileComponent(stableIncidentKey(alerts[0]));
    const directory = path.join(
        stateDir,
        "incidents",
        `${startedUtc.replace(/[:.]/g, "-")}-${suffix}`);
    fs.mkdirSync(directory, { recursive: true });

    const window = encodeURIComponent(config.incidentCapture?.window || "24h");
    const sessionLimit = Math.max(1, Number(config.incidentCapture?.sessionLimit || 1000));
    const eventLimit = Math.max(1, Number(config.incidentCapture?.eventLimit || 2000));
    const relayLimit = Math.max(1, Number(config.incidentCapture?.relayLimit || 2000));
    const endpoints = [
        ["summary", "/api/network/summary"],
        ["datum-sessions", `/api/network/datum-sessions?window=${window}&limit=${sessionLimit}`],
        ["datum-share-responses", `/api/network/datum-share-responses?window=${window}&limit=${sessionLimit}`],
        ["datum-protocol-events", `/api/network/datum-protocol-events?window=${window}&limit=${eventLimit}`],
        ["coinbaser-diagnostics", `/api/network/coinbaser-diagnostics?window=${window}&limit=${sessionLimit}`],
        ["network-events", `/api/network/events?window=${window}&limit=${eventLimit}`],
        ["peer-relay-latency", `/api/network/peer-relay-latency?window=${window}&limit=${relayLimit}`]
    ];
    const nodes = nodesForIncidentAlerts(alerts, snapshot);
    const manifest = {
        schemaVersion: 1,
        startedUtc,
        completedUtc: null,
        alerts,
        nodes: []
    };

    await Promise.all(nodes.map(async node => {
        const nodeDirectory = path.join(directory, "nodes", safeFileComponent(node.name));
        const captures = await Promise.all(endpoints.map(async ([name, suffixPath]) => {
            const result = await fetchJson(
                node.baseUrl,
                suffixPath,
                config,
                { headers: gridPoolAdminHeaders(node) });
            writeJsonAtomic(path.join(nodeDirectory, `${name}.json`), incidentFetchPayload(result));
            return { name, ok: result.ok, status: result.status, error: result.error || null };
        }));
        manifest.nodes.push({ name: node.name, baseUrl: node.baseUrl, captures });
    }));

    manifest.nodes.sort((a, b) => a.name.localeCompare(b.name));
    manifest.completedUtc = currentIso();
    writeJsonAtomic(path.join(directory, "manifest.json"), manifest);
    for (const alert of alerts) alert.diagnosticCapturePath = directory;
    return directory;
}

function shouldSendMorningDigest(state, config, forceDigest) {
    if (forceDigest) return true;
    if (!config.morningDigest?.enabled) return false;
    const parts = localDateParts(config.timezone || "America/New_York");
    if (parts.hour < Number(config.morningDigest.hourLocal ?? 7)) return false;
    return state.telegram.lastMorningDigestDate !== parts.date;
}

function markMorningDigestSent(state, config) {
    state.telegram.lastMorningDigestDate = localDateParts(config.timezone || "America/New_York").date;
}

function buildAlertMessage(alerts, codexResult = null) {
    const lines = [
        `GridPool monitor alert (${alerts.length})`,
        ""
    ];

    for (const alert of alerts.slice(0, 10)) {
        lines.push(`[${alert.severity.toUpperCase()}] ${alert.title}`);
        if (alert.detail) lines.push(alert.detail);
        if (alert.diagnosticCapturePath) lines.push(`Diagnostics: ${alert.diagnosticCapturePath}`);
        lines.push("");
    }

    if (alerts.length > 10) {
        lines.push(`...and ${alerts.length - 10} more alerts.`);
        lines.push("");
    }

    if (codexResult) {
        lines.push("Codex investigation:");
        lines.push(`Root cause: ${codexResult.root_cause || "--"}`);
        lines.push(`Resolved: ${codexResult.resolved === true ? "yes" : "no"}`);
        lines.push(`Manual action: ${codexResult.manual_action_needed === true ? (codexResult.recommended_manual_action || "needed") : "none reported"}`);
        if (codexResult.confidence) lines.push(`Confidence: ${codexResult.confidence}`);
    }

    return lines.join("\n").trim();
}

function buildDigest(snapshot, state, config = DEFAULT_CONFIG) {
    const lines = [
        `GridPool morning digest: ${snapshot.monitorName}`,
        `Collected: ${snapshot.collectedAtUtc}`,
        ""
    ];

    const consensusReport = buildConsensusReport(snapshot, {});
    if (consensusReport.length) {
        lines.push("Consensus groups:");
        for (const group of consensusReport) {
            const stateValues = [...new Set(group.nodes.map(node => shortId(node.currentStateId)).filter(Boolean))];
            const candidateValues = [...new Set(group.nodes.map(node => shortId(node.candidateStateId)).filter(Boolean))];
            lines.push(`${group.groupKey}: ${group.nodeCount} node(s); state=${stateValues.join(", ") || "--"}; candidate=${candidateValues.join(", ") || "--"}`);
        }
        lines.push("");
    }

    for (const node of snapshot.gridpoolNodes || []) {
        const localMining = node.localMining || currentLocalMining(node, config);
        lines.push(`GridPool ${node.name}: ${node.ok ? "ok" : "problem"}`);
        lines.push(`Snapshot ${node.summary?.currentRoundNumber ?? "--"}; state ${shortId(node.summary?.currentStateId) || "--"}; candidate ${shortId(node.summary?.candidateStateId) || "--"}; tip ${node.summary?.currentTipBlockHeight ?? "--"}; peers ${node.summary?.peerCount ?? "--"}`);
        lines.push(`Last GridPool block ${node.summary?.lastGridPoolBlockHeight ?? "--"}; paid snapshot ${node.summary?.lastPaidSnapshotId || "--"}`);
        lines.push(`Team hashrate ${node.summary?.currentRoundObservedHashrateDisplay || formatHashrateThs(node.summary?.currentRoundObservedHashrateThs)}; fresh local mining ${formatHashrateThs(localMining.hashrateThs)}`);
        lines.push(`Local miners: ${localMining.minerCount} fresh / ${(node.localMiners || []).length} tracked; sources: ${localMining.sources.map(source => `${source.source}=${formatHashrateThs(source.hashrateThs)}`).join(", ") || "none"}; payout addresses: ${(node.payoutAddresses || []).length}; candidate addresses: ${(node.candidateAddresses || []).length}`);
        if (node.errors?.length) lines.push(`Errors: ${node.errors.join("; ")}`);
        lines.push("");
    }

    for (const hydrapool of snapshot.hydrapools || []) {
        lines.push(`Hydrapool ${hydrapool.name}: ${hydrapool.ok ? "ok" : "problem"}`);
        lines.push(`Workers: ${(hydrapool.workers || []).length}; users: ${(hydrapool.users || []).length}`);
        if (hydrapool.errors?.length) lines.push(`Errors: ${hydrapool.errors.join("; ")}`);
        lines.push("");
    }

    for (const endpoint of snapshot.tcpEndpoints || []) {
        lines.push(`TCP ${endpoint.name}: ${endpoint.ok ? "ok" : "problem"} ${endpoint.host}:${endpoint.port} ${durationMsLabel(endpoint.durationMs)}`);
        if (endpoint.error) lines.push(`Error: ${endpoint.error}`);
    }
    if ((snapshot.tcpEndpoints || []).length) lines.push("");

    const inactiveServices = (snapshot.services || []).filter(service => !service.ok);
    lines.push(`Services inactive: ${inactiveServices.length ? inactiveServices.map(service => service.name).join(", ") : "none"}`);
    const unknown = state.known?.unknownListAddresses || [];
    lines.push(`Unknown list addresses seen: ${unknown.length ? unknown.slice(0, 6).join(", ") : "none"}`);

    const openAlerts = Object.values(state.openAlertLifecycles || {})
        .sort((left, right) => String(left.openedUtc || "").localeCompare(String(right.openedUtc || "")));
    if (openAlerts.length) {
        lines.push("");
        lines.push("Open endpoint/service warnings:");
        for (const alert of openAlerts) {
            lines.push(`[${String(alert.severity || "warning").toUpperCase()}] ${alert.title} (open since ${alert.openedUtc || "unknown"})`);
        }
    }

    const recentAlerts = (state.recentAlerts || []).slice(0, 5);
    if (recentAlerts.length) {
        lines.push("");
        lines.push("Recent alerts:");
        for (const alert of recentAlerts) {
            lines.push(`${alert.utc} [${alert.severity}] ${alert.title}`);
        }
    }

    return lines.join("\n").trim();
}

function buildStatusMessage(snapshot) {
    const lines = [`GridPool status: ${snapshot.monitorName}`];
    for (const group of buildConsensusReport(snapshot, {})) {
        const currentStates = [...new Set(group.nodes.map(node => shortId(node.currentStateId)).filter(Boolean))];
        lines.push(`Consensus ${group.groupKey}: nodes=${group.nodeCount} states=${currentStates.join(", ") || "--"}`);
    }
    for (const node of snapshot.gridpoolNodes || []) {
        lines.push(`${node.name}: ${node.ok ? "ok" : "problem"} snapshot=${node.summary?.currentRoundNumber ?? "--"} state=${shortId(node.summary?.currentStateId) || "--"} team=${node.summary?.currentRoundObservedHashrateDisplay || formatHashrateThs(node.summary?.currentRoundObservedHashrateThs)} peers=${node.summary?.peerCount ?? "--"} lastBlock=${node.summary?.lastGridPoolBlockHeight ?? "--"}`);
    }
    for (const hydrapool of snapshot.hydrapools || []) {
        lines.push(`${hydrapool.name}: ${hydrapool.ok ? "ok" : "problem"} workers=${(hydrapool.workers || []).length}`);
    }
    for (const endpoint of snapshot.tcpEndpoints || []) {
        lines.push(`${endpoint.name}: ${endpoint.ok ? "ok" : "problem"} ${endpoint.host}:${endpoint.port} ${durationMsLabel(endpoint.durationMs)}`);
    }
    return lines.join("\n");
}

class TelegramClient {
    constructor(config) {
        this.enabled = !!config.telegram?.enabled;
        this.token = process.env[config.telegram?.botTokenEnv || "TELEGRAM_BOT_TOKEN"] || "";
        this.chatIds = (process.env[config.telegram?.allowedChatIdsEnv || "TELEGRAM_ALLOWED_CHAT_IDS"] || "")
            .split(",")
            .map(item => item.trim())
            .filter(Boolean);
        const commandChatIds = (process.env[config.telegram?.commandChatIdsEnv || "TELEGRAM_COMMAND_CHAT_IDS"] || "")
            .split(",")
            .map(item => item.trim())
            .filter(Boolean);
        this.commandChatIds = commandChatIds.length > 0 ? commandChatIds : this.chatIds;
    }

    get available() {
        return this.enabled && !!this.token && this.chatIds.length > 0;
    }

    apiUrl(method) {
        return `https://api.telegram.org/bot${this.token}/${method}`;
    }

    async sendMessage(text, chatIds = this.chatIds) {
        if (!this.available) {
            return [];
        }

        const results = [];
        for (const chatId of chatIds) {
            const response = await fetchWithTimeout(this.apiUrl("sendMessage"), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    chat_id: chatId,
                    text: truncateTelegram(text),
                    disable_web_page_preview: true
                })
            }, 12000);
            results.push(response);
        }
        return results;
    }

    async getUpdates(offset) {
        if (!this.enabled || !this.token) {
            return [];
        }
        const url = new URL(this.apiUrl("getUpdates"));
        url.searchParams.set("timeout", "0");
        if (offset != null) url.searchParams.set("offset", String(offset));
        const response = await fetchWithTimeout(url.toString(), {}, 12000);
        if (!response.ok || !Array.isArray(response.json?.result)) {
            return [];
        }
        return response.json.result;
    }

    /**
     * Register the bot command menu shown in Telegram clients (/ menu).
     * Safe to call every run; Telegram overwrites the previous list.
     */
    async setMyCommands(config = {}) {
        if (!this.enabled || !this.token) {
            return false;
        }
        const commands = [
            { command: "status", description: "Compact live GridPool node status" },
            { command: "digest", description: "Full digest now (same shape as morning)" },
            { command: "silence", description: "Mute alerts: /silence 6h or /silence 6h all" },
            { command: "unsilence", description: "Clear alert mute / resume normal alerts" },
            { command: "help", description: "List monitor bot commands" }
        ];
        if (config.codex?.enabled) {
            commands.splice(2, 0, {
                command: "investigate",
                description: "Run a manual Codex investigation (if enabled)"
            });
        }
        const response = await fetchWithTimeout(this.apiUrl("setMyCommands"), {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ commands })
        }, 12000);
        return !!(response.ok && response.json?.ok);
    }
}

function truncateTelegram(text) {
    const value = String(text || "");
    if (value.length <= 3900) return value;
    return `${value.slice(0, 3800)}\n\n[truncated]`;
}

async function processTelegramCommands(telegram, state, snapshot, config, stateDir) {
    if (!telegram.enabled || !telegram.token) {
        return;
    }
    const updates = await telegram.getUpdates(state.telegram.updateOffset);
    for (const update of updates) {
        state.telegram.updateOffset = Math.max(Number(state.telegram.updateOffset || 0), Number(update.update_id) + 1);
        const message = update.message || update.edited_message;
        const text = String(message?.text || "").trim();
        const chatId = String(message?.chat?.id || "");
        if (!text || !chatId || !telegram.chatIds.includes(chatId) || !telegram.commandChatIds.includes(chatId)) {
            continue;
        }

        if (text.startsWith("/status")) {
            await telegram.sendMessage(buildStatusMessage(snapshot), [chatId]);
        } else if (text.startsWith("/digest")) {
            await telegram.sendMessage(buildDigest(snapshot, state, config), [chatId]);
        } else if (text.startsWith("/help") || text.startsWith("/start") || text.startsWith("/commands")) {
            await telegram.sendMessage(buildHelpMessage(config, state), [chatId]);
        } else if (text.startsWith("/unsilence") || text.startsWith("/unmute")) {
            state.telegram.silencedUntilUtc = null;
            state.telegram.silenceCritical = false;
            await telegram.sendMessage("Alert mute cleared. Normal alerting resumed.", [chatId]);
        } else if (text.startsWith("/silence") || text.startsWith("/mute") || text.startsWith("/pause")) {
            const parts = text.split(/\s+/).slice(1);
            let durationToken = "6h";
            let silenceAll = false;
            for (const part of parts) {
                const lower = String(part || "").toLowerCase();
                if (lower === "all" || lower === "critical" || lower === "everything") {
                    silenceAll = true;
                    continue;
                }
                if (parseDurationMs(part)) {
                    durationToken = part;
                }
            }
            const duration = parseDurationMs(durationToken) || (6 * 60 * 60_000);
            state.telegram.silencedUntilUtc = new Date(Date.now() + duration).toISOString();
            state.telegram.silenceCritical = silenceAll;
            const scope = silenceAll ? "ALL alerts (including critical)" : "non-critical alerts";
            await telegram.sendMessage(
                `${scope} silenced until ${state.telegram.silencedUntilUtc}.\nUse /unsilence to resume early.\nTip: /silence 6h all also mutes divergence/critical.`,
                [chatId]
            );
        } else if (text.startsWith("/investigate")) {
            if (!config.codex?.enabled) {
                await telegram.sendMessage("Codex investigation is disabled on this monitor. Use /status or /digest, then inspect the monitor logs manually.", [chatId]);
                continue;
            }
            const incident = {
                severity: "warning",
                category: "manual-investigation",
                fingerprint: `manual-investigation:${Date.now()}`,
                title: "Manual Telegram investigation request",
                detail: "User requested /investigate from Telegram."
            };
            const codexResult = await runCodexInvestigation(incident, snapshot, state, config, stateDir, { force: true });
            await telegram.sendMessage(buildAlertMessage([incident], codexResult), [chatId]);
        }
    }
}

function buildHelpMessage(config, state) {
    const mutedUntil = parseDate(state?.telegram?.silencedUntilUtc);
    const muted = mutedUntil && mutedUntil.getTime() > Date.now();
    const lines = [
        "GridPool monitor commands:",
        "",
        "/status — compact live node status",
        "/digest — full digest now",
        "/silence 6h — mute non-critical alerts for 6 hours",
        "/silence 6h all — mute ALL alerts incl. critical/divergence",
        "/unsilence — clear mute / resume alerts",
        "/help — this menu"
    ];
    if (config.codex?.enabled) {
        lines.splice(5, 0, "/investigate — manual Codex investigation");
    }
    lines.push("");
    if (muted) {
        const scope = state.telegram.silenceCritical ? "all alerts" : "non-critical alerts";
        lines.push(`Currently muted (${scope}) until ${state.telegram.silencedUntilUtc}.`);
    } else {
        lines.push("Alerts are not muted.");
    }
    lines.push("Open the / menu in Telegram for the same shortcuts.");
    return lines.join("\n");
}

function parseDurationMs(value) {
    const match = String(value || "").trim().match(/^(\d+)(m|h|d)?$/i);
    if (!match) return null;
    const amount = Number(match[1]);
    const unit = (match[2] || "m").toLowerCase();
    if (unit === "m") return amount * 60_000;
    if (unit === "h") return amount * 60 * 60_000;
    if (unit === "d") return amount * 24 * 60 * 60_000;
    return null;
}

async function runCodexForAlerts(alerts, snapshot, state, config, stateDir) {
    const incident = alerts
        .filter(alert => alert.codexEligible !== false)
        .sort((a, b) => severityRank(b.severity) - severityRank(a.severity))[0];
    if (!incident) return null;
    return runCodexInvestigation(incident, snapshot, state, config, stateDir);
}

function severityRank(value) {
    if (value === "critical") return 3;
    if (value === "warning") return 2;
    return 1;
}

async function runCodexInvestigation(incident, snapshot, state, config, stateDir, options = {}) {
    if (!config.codex?.enabled) {
        return null;
    }

    const cooldownMs = Number(config.codexCooldownMinutes || 360) * 60_000;
    const last = parseDate(state.codexCooldowns[incident.fingerprint]);
    if (!options.force && last && Date.now() - last < cooldownMs) {
        return null;
    }
    state.codexCooldowns[incident.fingerprint] = currentIso();

    const incidentDir = path.join(stateDir, "incidents");
    fs.mkdirSync(incidentDir, { recursive: true });
    const safeName = incident.fingerprint.replace(/[^a-zA-Z0-9_.-]+/g, "_").slice(0, 120);
    const packetPath = path.join(incidentDir, `${Date.now()}-${safeName}.json`);
    const outputPath = path.join(incidentDir, `${Date.now()}-${safeName}.codex.json`);
    const schemaPath = path.join(incidentDir, "codex-result.schema.json");

    writeJsonAtomic(packetPath, {
        incident,
        snapshot,
        previousSnapshot: state.lastSnapshot,
        recentAlerts: state.recentAlerts?.slice(0, 10) || []
    });
    writeJsonAtomic(schemaPath, codexResultSchema());

    const prompt = `You are investigating a GridPool health monitor incident.

Rules:
- Investigate only.
- Do not edit files.
- Do not restart services.
- Do not deploy code.
- Prefer read-only commands and logs.
- Return JSON matching the provided schema.

Incident packet path: ${packetPath}
Incident summary:
${JSON.stringify(incident, null, 2)}
`;

    const args = [
        ...(config.codex.resumeArgs || DEFAULT_CONFIG.codex.resumeArgs),
        "--output-schema", schemaPath,
        "-o", outputPath
    ];
    if (config.codex.model) {
        args.push("-m", config.codex.model);
    }
    args.push("-");

    const result = spawnSync("codex", args, {
        input: prompt,
        encoding: "utf8",
        timeout: Number(config.codex.timeoutSeconds || 600) * 1000,
        cwd: config.codex.repoDir || process.cwd()
    });

    const parsed = readJsonOrExtractObjectIfExists(outputPath);
    if (parsed) {
        appendJsonLine(path.join(stateDir, "codex-investigations.jsonl"), {
            utc: currentIso(),
            incident,
            outputPath,
            result: parsed
        });
        return parsed;
    }

    return {
        root_cause: "Codex investigation failed or produced no parseable JSON.",
        evidence: [
            `exit=${result.status}`,
            `signal=${result.signal || ""}`,
            `stderr=${String(result.stderr || "").slice(0, 500)}`
        ],
        resolved: false,
        manual_action_needed: true,
        recommended_manual_action: `Check ${packetPath} and rerun: codex exec -C ${config.codex.repoDir || process.cwd()} resume --last -`,
        confidence: "low"
    };
}

function codexResultSchema() {
    return {
        type: "object",
        additionalProperties: false,
        properties: {
            root_cause: { type: "string" },
            evidence: {
                type: "array",
                items: { type: "string" }
            },
            resolved: { type: "boolean" },
            manual_action_needed: { type: "boolean" },
            recommended_manual_action: { type: "string" },
            confidence: {
                type: "string",
                enum: ["low", "medium", "high"]
            }
        },
        required: [
            "root_cause",
            "evidence",
            "resolved",
            "manual_action_needed",
            "recommended_manual_action",
            "confidence"
        ]
    };
}

function readJsonOrExtractObjectIfExists(filePath) {
    if (!filePath || !fs.existsSync(filePath)) {
        return null;
    }

    const text = fs.readFileSync(filePath, "utf8").trim();
    if (!text) {
        return null;
    }

    try {
        return JSON.parse(text);
    } catch {
        // Some Codex CLI versions may write a fenced or prose-wrapped final
        // message even when given an output schema. Keep automation useful by
        // extracting the first balanced JSON object.
    }

    const fenced = text.match(/```(?:json)?\s*([\s\S]*?)```/i);
    if (fenced) {
        try {
            return JSON.parse(fenced[1].trim());
        } catch {
            // Fall through to balanced-object extraction.
        }
    }

    const objectText = extractFirstJsonObject(text);
    if (!objectText) {
        return null;
    }

    try {
        return JSON.parse(objectText);
    } catch {
        return null;
    }
}

function extractFirstJsonObject(text) {
    const start = text.indexOf("{");
    if (start < 0) {
        return null;
    }

    let depth = 0;
    let inString = false;
    let escaped = false;
    for (let index = start; index < text.length; index += 1) {
        const char = text[index];
        if (escaped) {
            escaped = false;
            continue;
        }

        if (char === "\\") {
            escaped = inString;
            continue;
        }

        if (char === "\"") {
            inString = !inString;
            continue;
        }

        if (inString) {
            continue;
        }

        if (char === "{") {
            depth += 1;
        } else if (char === "}") {
            depth -= 1;
            if (depth === 0) {
                return text.slice(start, index + 1);
            }
        }
    }

    return null;
}

function compactSummary(snapshot, alerts) {
    return {
        collectedAtUtc: snapshot.collectedAtUtc,
        consensus: buildConsensusReport(snapshot, {}),
        gridpoolNodes: snapshot.gridpoolNodes.map(node => ({
            name: node.name,
            baseUrl: node.baseUrl,
            ok: node.ok,
            round: node.summary?.currentRoundNumber,
            networkId: node.summary?.networkId,
            bitcoinNetwork: node.summary?.bitcoinNetwork,
            consensusVersion: node.version?.consensusVersion ?? node.summary?.consensusVersion,
            stateBundleSchemaVersion: node.version?.stateBundleSchemaVersion ?? node.summary?.stateBundleSchemaVersion,
            releaseVersion: node.version?.releaseVersion ?? node.summary?.releaseVersion,
            currentStateId: node.summary?.currentStateId,
            candidateStateId: node.summary?.candidateStateId,
            activeSnapshotId: node.summary?.activeSnapshotId,
            workSetCount: node.summary?.workSetCount,
            workSetReserveLimit: node.summary?.workSetReserveLimit,
            coinbaseOutputMode: node.summary?.coinbaseOutputMode,
            coinbaseOutputCount: node.summary?.coinbaseOutputCount,
            teamHashrate: node.summary?.currentRoundObservedHashrateDisplay || formatHashrateThs(node.summary?.currentRoundObservedHashrateThs),
            localMiningHashrate: formatHashrateThs((node.localMining || currentLocalMining(node, DEFAULT_CONFIG)).hashrateThs),
            localMiningSources: (node.localMining || currentLocalMining(node, DEFAULT_CONFIG)).sources,
            datumAcceptanceRate: datumAcceptanceRate(node.summary?.localDatumDiagnostics),
            localMiners: node.localMiners.length,
            peers: node.summary?.peerCount,
            checks: node.checks,
            peerRelayLatency: compactPeerRelayLatency(node.peerRelayLatency),
            errors: node.errors
        })),
        hydrapools: snapshot.hydrapools.map(hydrapool => ({
            name: hydrapool.name,
            ok: hydrapool.ok,
            workers: hydrapool.workers.length,
            users: hydrapool.users.length
        })),
        tcpEndpoints: (snapshot.tcpEndpoints || []).map(endpoint => ({
            name: endpoint.name,
            host: endpoint.host,
            port: endpoint.port,
            ok: endpoint.ok,
            durationMs: endpoint.durationMs,
            error: endpoint.error
        })),
        services: snapshot.services.map(service => ({
            name: service.name,
            ok: service.ok,
            active: service.active
        })),
        alerts: alerts.map(alert => ({
            severity: alert.severity,
            category: alert.category,
            title: alert.title
        }))
    };
}

function compactPeerRelayLatency(series) {
    if (!series) return null;
    return {
        windowSeconds: series.windowSeconds ?? null,
        totalEvents: series.totalEvents ?? null,
        transports: Array.isArray(series.transports)
            ? series.transports.map(transport => ({
                transport: transport.transport,
                arrivalCount: transport.arrivalCount,
                firstArrivalCount: transport.firstArrivalCount,
                acceptedCount: transport.acceptedCount,
                duplicateCount: transport.duplicateCount,
                rejectedCount: transport.rejectedCount,
                averageDeltaFromFirstMs: transport.averageDeltaFromFirstMs,
                medianDeltaFromFirstMs: transport.medianDeltaFromFirstMs,
                p95DeltaFromFirstMs: transport.p95DeltaFromFirstMs,
                averagePayloadBytes: transport.averagePayloadBytes,
                minPayloadBytes: transport.minPayloadBytes,
                maxPayloadBytes: transport.maxPayloadBytes
            }))
            : []
    };
}

function writeMonitorLogs(stateDir, snapshot, alerts) {
    const date = snapshot.collectedAtUtc.slice(0, 10);
    const compact = compactSummary(snapshot, alerts);
    const consensus = {
        collectedAtUtc: snapshot.collectedAtUtc,
        monitorName: snapshot.monitorName,
        groups: compact.consensus
    };

    appendJsonLine(path.join(stateDir, "snapshots", `${date}.jsonl`), compact);
    appendJsonLine(path.join(stateDir, "consensus", `${date}.jsonl`), consensus);
    if (alerts.length) {
        appendJsonLine(path.join(stateDir, "alerts", `${date}.jsonl`), {
            utc: snapshot.collectedAtUtc,
            alerts
        });
    }

    writeJsonAtomic(path.join(stateDir, "latest-summary.json"), compact);
    writeJsonAtomic(path.join(stateDir, "latest-consensus.json"), consensus);
}

async function main() {
    const args = parseArgs(process.argv.slice(2));
    if (args["self-test"] === "true") {
        runSelfTests();
        return;
    }
    if (args.help === "true" || args.h === "true") {
        usage();
        return;
    }

    const config = loadConfig(args);
    const stateDir = expandHome(args["state-dir"] || process.env.GRIDPOOL_HEALTH_STATE_DIR || DEFAULT_STATE_DIR);
    fs.mkdirSync(stateDir, { recursive: true });
    const state = loadState(stateDir);
    const telegram = new TelegramClient(config);

    if (args["test-telegram"] === "true") {
        await telegram.setMyCommands(config);
        await telegram.sendMessage(`GridPool monitor test message from ${os.hostname()} at ${currentIso()}.`);
        return;
    }

    // Keep the Telegram / menu in sync so operators do not have to remember commands.
    try {
        const menuOk = await telegram.setMyCommands(config);
        if (!menuOk) {
            console.warn("[gridpool-health-monitor] setMyCommands failed or telegram unavailable");
        }
    } catch (error) {
        console.warn(`[gridpool-health-monitor] setMyCommands error: ${error?.message || error}`);
    }

    const snapshot = await collectSnapshot(config);
    const alerts = buildAlerts(snapshot, state, config);
    const newIncidentAlerts = selectNewIncidentAlerts(alerts, state);
    if (newIncidentAlerts.length) {
        try {
            const capturePath = await captureIncidentDiagnostics(newIncidentAlerts, snapshot, stateDir, config);
            if (capturePath) {
                console.log(`[gridpool-health-monitor] captured incident diagnostics: ${capturePath}`);
            }
        } catch (error) {
            console.warn(`[gridpool-health-monitor] incident diagnostic capture failed: ${error?.message || error}`);
        }
    }
    const deliverableAlerts = filterAlertsForDelivery(alerts, state, config);

    await processTelegramCommands(telegram, state, snapshot, config, stateDir);

    let codexResult = null;
    if (deliverableAlerts.some(alert => alert.severity === "warning" || alert.severity === "critical")) {
        codexResult = await runCodexForAlerts(deliverableAlerts, snapshot, state, config, stateDir);
    }

    if (deliverableAlerts.length) {
        await telegram.sendMessage(buildAlertMessage(deliverableAlerts, codexResult));
    }

    if (shouldSendMorningDigest(state, config, args["force-digest"] === "true")) {
        await telegram.sendMessage(buildDigest(snapshot, state, config));
        markMorningDigestSent(state, config);
    }

    if (!state.initialized) {
        state.initialized = true;
        state.initializedAtUtc = currentIso();
    }
    state.lastSnapshot = snapshot;
    state.lastRunUtc = currentIso();

    writeMonitorLogs(stateDir, snapshot, alerts);
    appendJsonLine(path.join(stateDir, "runs.jsonl"), {
        utc: currentIso(),
        summary: compactSummary(snapshot, deliverableAlerts)
    });
    writeJsonAtomic(statePathFor(stateDir), state);

    if (args["print-summary"] === "true") {
        console.log(JSON.stringify(compactSummary(snapshot, deliverableAlerts), null, 2));
    } else {
        console.log(`[gridpool-health-monitor] ${snapshot.collectedAtUtc} alerts=${deliverableAlerts.length} state=${statePathFor(stateDir)}`);
    }
}

function runSelfTests() {
    const group = { groupKey: "test", nodes: [
        { name: "a", currentStateId: "one", activeSnapshotId: "alpha" },
        { name: "b", currentStateId: "two", activeSnapshotId: "beta" }
    ] };
    const first = consensusStateFingerprint(group);
    const reordered = consensusStateFingerprint({ ...group, nodes: [...group.nodes].reverse() });
    if (first !== reordered) throw new Error("consensus fingerprint must be order independent");

    const state = { telegram: {}, alertCooldowns: { [first]: currentIso() }, recentAlerts: [] };
    const delivered = filterAlertsForDelivery(
        [{ severity: "critical", category: "consensus-divergence", fingerprint: first }],
        state,
        { alertCooldownMinutes: 60 });
    if (delivered.length !== 0) throw new Error("consensus divergence reminder cooldown failed");

    const lifecycleState = { telegram: {}, alertCooldowns: {}, openAlertLifecycles: {}, recentAlerts: [] };
    const endpointDown = [{
        severity: "warning",
        category: "endpoint-down",
        fingerprint: "gridpool:remote:live",
        title: "GridPool remote live unreachable",
        detail: "https://remote.example request timed out"
    }];
    if (filterAlertsForDelivery(endpointDown, lifecycleState, { alertCooldownMinutes: 60 }).length !== 1) {
        throw new Error("new endpoint failure was not delivered");
    }
    if (filterAlertsForDelivery(endpointDown, lifecycleState, { alertCooldownMinutes: 60 }).length !== 0) {
        throw new Error("continuing endpoint failure was delivered more than once");
    }
    const resolvedLifecycle = filterAlertsForDelivery([], lifecycleState, { alertCooldownMinutes: 60 });
    if (resolvedLifecycle.length !== 1 || resolvedLifecycle[0].category !== "alert-resolved" ||
        Object.keys(lifecycleState.openAlertLifecycles).length !== 0) {
        throw new Error("endpoint recovery lifecycle notification failed");
    }

    const freshMining = currentLocalMining({ localMiners: [
        { source: "ckpool", currentHashrateThs: 5, lastShareUtc: currentIso() },
        { source: "datum", currentHashrateThs: 2, lastShareUtc: currentIso() },
        { source: "hydrapool", currentHashrateThs: 500, lastShareUtc: new Date(Date.now() - 60 * 60_000).toISOString() }
    ] }, { thresholds: { localMiningFreshnessMinutes: 20 } });
    if (freshMining.hashrateThs !== 7 || freshMining.sources.length !== 2 || freshMining.minerCount !== 2) {
        throw new Error("fresh source-aware local mining calculation failed");
    }
    const privateDiagnostic = compactFetchResult({ ok: false, status: 404, durationMs: 1 }, {
        optionalStatuses: [404]
    });
    if (!privateDiagnostic.ok || privateDiagnostic.available || privateDiagnostic.status !== 404) {
        throw new Error("privacy-protected diagnostic endpoint was not treated as optional");
    }
    const incidentState = { activeIncidentKeys: [] };
    if (selectNewIncidentAlerts(
        [{ severity: "warning", category: "test", fingerprint: "gridpool:a:test" }],
        incidentState).length !== 1) {
        throw new Error("new incident edge was not detected");
    }
    if (selectNewIncidentAlerts(
        [{ severity: "warning", category: "test", fingerprint: "gridpool:a:test" }],
        incidentState).length !== 0) {
        throw new Error("active incident was captured more than once");
    }
    selectNewIncidentAlerts([], incidentState);
    if (selectNewIncidentAlerts(
        [{ severity: "warning", category: "test", fingerprint: "gridpool:a:test" }],
        incidentState).length !== 1) {
        throw new Error("resolved incident recurrence was not detected");
    }
    const incidentNodes = nodesForIncidentAlerts(
        [{ fingerprint: "hashrate:b:local:drop" }],
        { gridpoolNodes: [
            { name: "a", consensusGroup: "test" },
            { name: "b", consensusGroup: "test" },
            { name: "other", consensusGroup: "other" }
        ] });
    if (incidentNodes.map(node => node.name).sort().join(",") !== "a,b") {
        throw new Error("incident node and consensus peer selection failed");
    }

    const crossHeightAlerts = [];
    addConsensusStateDivergenceAlert(
        crossHeightAlerts,
        { failureCounts: {}, failureFirstSeenUtc: {} },
        { thresholds: { consensusDivergenceConsecutive: 1 } },
        { groupKey: "test", nodes: [
            { name: "a", currentStateId: "one", activeSnapshotId: "alpha", currentTipBlockHeight: 100 },
            { name: "b", currentStateId: "two", activeSnapshotId: "beta", currentTipBlockHeight: 101 }
        ] });
    if (crossHeightAlerts.length !== 0) {
        throw new Error("cross-height snapshot transition was classified as consensus divergence");
    }

    const sameHeightAlerts = [];
    addConsensusStateDivergenceAlert(
        sameHeightAlerts,
        { failureCounts: {}, failureFirstSeenUtc: {} },
        { thresholds: { consensusDivergenceConsecutive: 1 } },
        { groupKey: "test", nodes: [
            { name: "a", currentStateId: "one", activeSnapshotId: "alpha", currentTipBlockHeight: 101 },
            { name: "b", currentStateId: "two", activeSnapshotId: "beta", currentTipBlockHeight: 101 }
        ] });
    if (sameHeightAlerts.length !== 1 || sameHeightAlerts[0].category !== "consensus-divergence") {
        throw new Error("same-height state divergence was not detected");
    }

    const staleInputAlerts = [];
    maybeAddOutboundRelayAlert(staleInputAlerts, {
        name: "test",
        summary: {
            localDatumHashrateThs: 10,
            activeDatumSessionCount: 1,
            lastDatumSessionOpenedUtc: new Date(Date.now() - 30 * 60_000).toISOString(),
            lastValidLocalDatumShareUtc: new Date(Date.now() - 20 * 60_000).toISOString(),
            lastSuccessfulOutboundRelayUtc: new Date(Date.now() - 20 * 60_000).toISOString(),
            outboundRelayHealthy: false
        },
        peerRecords: []
    }, { thresholds: { outboundRelayStaleMinutes: 10, peerOutboundAttemptStaleMinutes: 10 } });
    if (staleInputAlerts.length !== 1 || staleInputAlerts[0].category !== "local-datum-share-stale") {
        throw new Error("stopped DATUM input was misclassified as an outbound relay failure");
    }

    const activePeerAlerts = [];
    maybeAddOutboundRelayAlert(activePeerAlerts, {
        name: "test",
        summary: {},
        peerRecords: [{
            endpoint: "https://peer.example",
            sessionConnected: true,
            lastAttemptUtc: new Date(Date.now() - 20 * 60_000).toISOString(),
            lastSuccessUtc: new Date(Date.now() - 30_000).toISOString(),
            lastSeenUtc: new Date(Date.now() - 30_000).toISOString()
        }]
    }, { thresholds: { peerOutboundAttemptStaleMinutes: 10 } });
    if (activePeerAlerts.length !== 0) {
        throw new Error("active peer traffic was misclassified as frozen outbound polling");
    }

    const frozenPeerAlerts = [];
    maybeAddOutboundRelayAlert(frozenPeerAlerts, {
        name: "test",
        summary: {},
        peerRecords: [{
            endpoint: "https://peer.example",
            sessionConnected: true,
            lastAttemptUtc: new Date(Date.now() - 20 * 60_000).toISOString(),
            lastSuccessUtc: new Date(Date.now() - 20 * 60_000).toISOString(),
            lastSeenUtc: new Date(Date.now() - 20 * 60_000).toISOString()
        }]
    }, { thresholds: { peerOutboundAttemptStaleMinutes: 10 } });
    if (frozenPeerAlerts.length !== 1 || frozenPeerAlerts[0].category !== "peer-outbound-attempt-stale") {
        throw new Error("frozen outbound polling was not detected");
    }
    console.log("gridpool-health-monitor self-test: ok");
}

main().catch(error => {
    console.error(error);
    process.exit(1);
});
