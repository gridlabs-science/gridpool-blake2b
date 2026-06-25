#!/usr/bin/env node

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

const SCRIPT_VERSION = 1;
const DEFAULT_STATE_DIR = path.join(os.homedir(), ".local", "state", "gridpool-monitor");
const DEFAULT_CONFIG_PATHS = [
    path.join(os.homedir(), ".config", "gridpool-health-monitor", "config.json"),
    path.join(process.cwd(), "scripts", "gridpool-health-monitor.local.json")
];

const FOUNDATION_ADDRESS = "bc1qce93hy5rhg02s6aeu7mfdvxg76x66pqqtrvzs3";

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
        allowedChatIdsEnv: "TELEGRAM_ALLOWED_CHAT_IDS"
    },
    codex: {
        enabled: true,
        repoDir: process.cwd(),
        timeoutSeconds: 600,
        model: "",
        resumeArgs: ["exec", "-C", process.cwd(), "--sandbox", "read-only", "--ask-for-approval", "never", "resume", "--last"]
    },
    thresholds: {
        endpointFailureConsecutive: 2,
        hashrateDropFraction: 0.35,
        hashrateSpikeMultiplier: 2.0,
        hashrateSamplesForTrend: 3,
        minimumHashrateThsForTrend: 1,
        activeHydrapoolWorkerMaxAgeMinutes: 60
    },
    nodes: [
        {
            name: "main",
            baseUrl: "http://127.0.0.1:5000",
            critical: true,
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
    return mergeConfig({
        version: SCRIPT_VERSION,
        initialized: false,
        failureCounts: {},
        alertCooldowns: {},
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
        critical: node.critical !== false,
        ok: true,
        checks: {},
        errors: [],
        summary: null,
        ready: null,
        localMiners: [],
        payoutAddresses: [],
        candidateAddresses: [],
        peers: []
    };

    const live = await fetchJson(baseUrl, "/health/live", config);
    result.checks.live = compactFetchResult(live);
    if (!live.ok) result.errors.push(`live failed: ${live.error || live.status}`);

    const ready = await fetchJson(baseUrl, "/health/ready", config);
    result.checks.ready = compactFetchResult(ready);
    result.ready = ready.json;
    if (!ready.ok) result.errors.push(`ready failed: ${ready.error || ready.status}`);

    const summary = await fetchJson(baseUrl, "/api/network/summary", config);
    result.checks.summary = compactFetchResult(summary);
    result.summary = summary.json;
    if (!summary.ok || !summary.json) result.errors.push(`summary failed: ${summary.error || summary.status}`);

    const miners = await fetchJson(baseUrl, "/api/network/local-miners?limit=500&window=24h", config);
    result.checks.localMiners = compactFetchResult(miners);
    result.localMiners = Array.isArray(miners.json?.miners) ? miners.json.miners : [];
    if (!miners.ok) result.errors.push(`local-miners failed: ${miners.error || miners.status}`);

    const payouts = await fetchJson(baseUrl, "/api/mining/payouts", config);
    result.checks.payouts = compactFetchResult(payouts);
    result.payoutAddresses = extractAddresses(payouts.json?.payouts);
    if (!payouts.ok) result.errors.push(`payouts failed: ${payouts.error || payouts.status}`);

    const candidateStateId = summary.json?.candidateStateId;
    if (candidateStateId) {
        const state = await fetchJson(baseUrl, `/api/network/state/${encodeURIComponent(candidateStateId)}`, config);
        result.checks.candidateState = compactFetchResult(state);
        result.candidateState = state.json;
        result.candidateAddresses = extractAddresses(state.json?.winnersList);
        if (!state.ok) result.errors.push(`candidate state failed: ${state.error || state.status}`);
    }

    result.peers = Array.isArray(summary.json?.peers)
        ? summary.json.peers.map(peer => peer.endpoint || peer.url || peer.address).filter(Boolean)
        : [];

    result.ok = result.errors.length === 0;
    return result;
}

function compactFetchResult(result) {
    return {
        ok: !!result.ok,
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

    return {
        version: SCRIPT_VERSION,
        monitorName: config.monitorName,
        collectedAtUtc: currentIso(),
        gridpoolNodes,
        hydrapools,
        services: collectServiceStatus(config.services || [])
    };
}

function buildAlerts(snapshot, state, config) {
    const alerts = [];
    const firstRun = !state.initialized;

    for (const node of snapshot.gridpoolNodes) {
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
    }

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

function incrementFailure(state, key) {
    state.failureCounts[key] = Number(state.failureCounts[key] || 0) + 1;
    return state.failureCounts[key];
}

function resetFailure(state, key) {
    state.failureCounts[key] = 0;
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
    const local = Number(node.summary?.localDatumHashrateThs);
    addHashrateSample(state, `${node.name}:team`, observed);
    addHashrateSample(state, `${node.name}:local`, local);

    const teamAlert = trendAlertForSeries(state, `${node.name}:team`, config, `GridPool ${node.name} team hashrate`);
    if (teamAlert) alerts.push(teamAlert);

    const localAlert = trendAlertForSeries(state, `${node.name}:local`, config, `GridPool ${node.name} local DATUM hashrate`);
    if (localAlert) alerts.push(localAlert);
}

function addHashrateSample(state, key, value) {
    if (!Number.isFinite(value) || value <= 0) return;
    const history = state.hashrateHistory[key] || [];
    history.push({ utc: currentIso(), value });
    state.hashrateHistory[key] = history.slice(-24);
}

function trendAlertForSeries(state, key, config, label) {
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
            detail: `${formatHashrateThs(previousAvg)} -> ${formatHashrateThs(recentAvg)} across ${samplesNeeded} samples.`,
            codexEligible: true
        };
    }

    if (recentAvg >= previousAvg * spikeMultiplier) {
        return {
            severity: "info",
            category: "hashrate-spike",
            fingerprint: `hashrate:${key}:spike`,
            title: `${label} spiked`,
            detail: `${formatHashrateThs(previousAvg)} -> ${formatHashrateThs(recentAvg)} across ${samplesNeeded} samples.`,
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
    const mutedUntil = parseDate(state.telegram.silencedUntilUtc);
    const muted = mutedUntil && mutedUntil > now;
    const cooldownMs = Number(config.alertCooldownMinutes || 60) * 60_000;

    for (const alert of alerts) {
        const critical = alert.severity === "critical" || alert.category === "gridpool-block-found";
        if (muted && !critical) continue;

        const last = parseDate(state.alertCooldowns[alert.fingerprint]);
        if (last && now - last < cooldownMs && !critical) continue;

        state.alertCooldowns[alert.fingerprint] = currentIso();
        delivered.push(alert);
        state.recentAlerts.unshift({
            ...alert,
            utc: currentIso()
        });
    }

    state.recentAlerts = (state.recentAlerts || []).slice(0, 50);
    return delivered;
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

function buildDigest(snapshot, state) {
    const lines = [
        `GridPool morning digest: ${snapshot.monitorName}`,
        `Collected: ${snapshot.collectedAtUtc}`,
        ""
    ];

    for (const node of snapshot.gridpoolNodes || []) {
        lines.push(`GridPool ${node.name}: ${node.ok ? "ok" : "problem"}`);
        lines.push(`Snapshot ${node.summary?.currentRoundNumber ?? "--"}; tip ${node.summary?.currentTipBlockHeight ?? "--"}; peers ${node.summary?.peerCount ?? "--"}`);
        lines.push(`Last GridPool block ${node.summary?.lastGridPoolBlockHeight ?? "--"}; paid snapshot ${node.summary?.lastPaidSnapshotId || "--"}`);
        lines.push(`Team hashrate ${node.summary?.currentRoundObservedHashrateDisplay || formatHashrateThs(node.summary?.currentRoundObservedHashrateThs)}; local DATUM ${node.summary?.localDatumHashrateDisplay || formatHashrateThs(node.summary?.localDatumHashrateThs)}`);
        lines.push(`Local DATUM miners: ${(node.localMiners || []).length}; payout addresses: ${(node.payoutAddresses || []).length}; candidate addresses: ${(node.candidateAddresses || []).length}`);
        if (node.errors?.length) lines.push(`Errors: ${node.errors.join("; ")}`);
        lines.push("");
    }

    for (const hydrapool of snapshot.hydrapools || []) {
        lines.push(`Hydrapool ${hydrapool.name}: ${hydrapool.ok ? "ok" : "problem"}`);
        lines.push(`Workers: ${(hydrapool.workers || []).length}; users: ${(hydrapool.users || []).length}`);
        if (hydrapool.errors?.length) lines.push(`Errors: ${hydrapool.errors.join("; ")}`);
        lines.push("");
    }

    const inactiveServices = (snapshot.services || []).filter(service => !service.ok);
    lines.push(`Services inactive: ${inactiveServices.length ? inactiveServices.map(service => service.name).join(", ") : "none"}`);
    const unknown = state.known?.unknownListAddresses || [];
    lines.push(`Unknown list addresses seen: ${unknown.length ? unknown.slice(0, 6).join(", ") : "none"}`);

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
    for (const node of snapshot.gridpoolNodes || []) {
        lines.push(`${node.name}: ${node.ok ? "ok" : "problem"} snapshot=${node.summary?.currentRoundNumber ?? "--"} team=${node.summary?.currentRoundObservedHashrateDisplay || formatHashrateThs(node.summary?.currentRoundObservedHashrateThs)} peers=${node.summary?.peerCount ?? "--"} lastBlock=${node.summary?.lastGridPoolBlockHeight ?? "--"}`);
    }
    for (const hydrapool of snapshot.hydrapools || []) {
        lines.push(`${hydrapool.name}: ${hydrapool.ok ? "ok" : "problem"} workers=${(hydrapool.workers || []).length}`);
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
        if (!text || !chatId || !telegram.chatIds.includes(chatId)) {
            continue;
        }

        if (text.startsWith("/status")) {
            await telegram.sendMessage(buildStatusMessage(snapshot), [chatId]);
        } else if (text.startsWith("/digest")) {
            await telegram.sendMessage(buildDigest(snapshot, state), [chatId]);
        } else if (text.startsWith("/help")) {
            await telegram.sendMessage("Commands: /status, /digest, /investigate, /silence 2h, /help", [chatId]);
        } else if (text.startsWith("/silence")) {
            const duration = parseDurationMs(text.split(/\s+/)[1] || "2h") || (2 * 60 * 60_000);
            state.telegram.silencedUntilUtc = new Date(Date.now() + duration).toISOString();
            await telegram.sendMessage(`Non-critical alerts silenced until ${state.telegram.silencedUntilUtc}.`, [chatId]);
        } else if (text.startsWith("/investigate")) {
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

    const parsed = readJsonIfExists(outputPath);
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

function compactSummary(snapshot, alerts) {
    return {
        collectedAtUtc: snapshot.collectedAtUtc,
        gridpoolNodes: snapshot.gridpoolNodes.map(node => ({
            name: node.name,
            ok: node.ok,
            round: node.summary?.currentRoundNumber,
            teamHashrate: node.summary?.currentRoundObservedHashrateDisplay || formatHashrateThs(node.summary?.currentRoundObservedHashrateThs),
            localDatumHashrate: node.summary?.localDatumHashrateDisplay || formatHashrateThs(node.summary?.localDatumHashrateThs),
            localMiners: node.localMiners.length,
            peers: node.summary?.peerCount
        })),
        hydrapools: snapshot.hydrapools.map(hydrapool => ({
            name: hydrapool.name,
            ok: hydrapool.ok,
            workers: hydrapool.workers.length,
            users: hydrapool.users.length
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

async function main() {
    const args = parseArgs(process.argv.slice(2));
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
        await telegram.sendMessage(`GridPool monitor test message from ${os.hostname()} at ${currentIso()}.`);
        return;
    }

    const snapshot = await collectSnapshot(config);
    const alerts = buildAlerts(snapshot, state, config);
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
        await telegram.sendMessage(buildDigest(snapshot, state));
        markMorningDigestSent(state, config);
    }

    if (!state.initialized) {
        state.initialized = true;
        state.initializedAtUtc = currentIso();
    }
    state.lastSnapshot = snapshot;
    state.lastRunUtc = currentIso();

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

main().catch(error => {
    console.error(error);
    process.exit(1);
});
