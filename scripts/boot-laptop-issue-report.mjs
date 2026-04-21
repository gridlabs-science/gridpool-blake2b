#!/usr/bin/env node

import fs from "node:fs";

function parseArgs(argv) {
    const args = {};
    for (let i = 0; i < argv.length; i += 1) {
        const arg = argv[i];
        if (!arg.startsWith("--")) {
            continue;
        }

        const key = arg.slice(2);
        const value = argv[i + 1] && !argv[i + 1].startsWith("--") ? argv[++i] : "true";
        args[key] = value;
    }

    return args;
}

function usage() {
    console.error("Usage: node scripts/boot-laptop-issue-report.mjs --main-url <url> [--peer-url <url>] [--since <iso>] [--until <iso>] [--window 12h] [--limit 5000] [--out report.json]");
    process.exit(1);
}

function normalizeBaseUrl(url) {
    return url.replace(/\/+$/, "");
}

function parseDate(value) {
    const time = Date.parse(value ?? "");
    return Number.isFinite(time) ? time : null;
}

function parseBound(value, fallback) {
    if (!value) {
        return fallback;
    }

    const parsed = parseDate(value);
    if (parsed == null) {
        throw new Error(`Invalid date: ${value}`);
    }

    return parsed;
}

function countBy(items, selector) {
    const counts = new Map();
    for (const item of items) {
        const key = selector(item) || "unknown";
        counts.set(key, (counts.get(key) || 0) + 1);
    }

    return Object.fromEntries([...counts.entries()].sort((a, b) => b[1] - a[1]));
}

function topEntries(counts, limit = 8) {
    return Object.fromEntries(Object.entries(counts).slice(0, limit));
}

function formatPercent(numerator, denominator) {
    if (!denominator) {
        return "--";
    }

    return `${((numerator / denominator) * 100).toFixed(2)}%`;
}

async function fetchJson(baseUrl, path, params = {}) {
    const url = new URL(`${normalizeBaseUrl(baseUrl)}${path}`);
    for (const [key, value] of Object.entries(params)) {
        if (value != null) {
            url.searchParams.set(key, String(value));
        }
    }

    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`${url} returned ${response.status}`);
    }

    return response.json();
}

function timestampMs(item) {
    return parseDate(item.timestampUtc);
}

function filterByTime(items, sinceMs, untilMs) {
    return items.filter(item => {
        const ms = timestampMs(item);
        return ms != null && ms >= sinceMs && ms <= untilMs;
    });
}

function withTimestamp(items) {
    return items
        .map(item => ({ ...item, timestampMs: timestampMs(item) }))
        .filter(item => item.timestampMs != null)
        .sort((a, b) => a.timestampMs - b.timestampMs);
}

function findLatestBefore(items, timestamp) {
    let latest = null;
    for (const item of items) {
        if (item.timestampMs <= timestamp) {
            latest = item;
        } else {
            break;
        }
    }

    return latest;
}

function delayBucket(deltaMs) {
    if (deltaMs == null) {
        return "unmatched";
    }

    if (deltaMs < 0) {
        return "before";
    }

    if (deltaMs <= 10_000) {
        return "0-10s";
    }

    if (deltaMs <= 30_000) {
        return "10-30s";
    }

    if (deltaMs <= 60_000) {
        return "30-60s";
    }

    if (deltaMs <= 300_000) {
        return "1-5m";
    }

    return ">5m";
}

function summarizeRejectCorrelation(rejects, events) {
    const byType = new Map();
    for (const event of withTimestamp(events)) {
        if (!byType.has(event.eventType)) {
            byType.set(event.eventType, []);
        }

        byType.get(event.eventType).push(event);
    }

    const roundRotations = byType.get("round-rotation") || [];
    const chainTips = byType.get("chain-tip") || [];
    const refreshes = byType.get("datum-refresh-request") || [];
    const sessionEdges = [
        ...(byType.get("datum-session-reset") || []),
        ...(byType.get("datum-session-close") || []),
        ...(byType.get("datum-session-lock") || [])
    ].sort((a, b) => a.timestampMs - b.timestampMs);
    const parentEdges = [...roundRotations, ...chainTips].sort((a, b) => a.timestampMs - b.timestampMs);

    const buckets = {
        sinceRoundRotation: {},
        sinceChainTip: {},
        sinceParentEdge: {},
        sinceDatumRefresh: {},
        sinceSessionEdge: {}
    };
    const lateByReason = {};

    for (const reject of withTimestamp(rejects)) {
        const reason = reject.rejectionCategory || reject.rejectionReason || "unknown";
        const lastRoundRotation = findLatestBefore(roundRotations, reject.timestampMs);
        const lastChainTip = findLatestBefore(chainTips, reject.timestampMs);
        const lastParentEdge = findLatestBefore(parentEdges, reject.timestampMs);
        const lastRefresh = findLatestBefore(refreshes, reject.timestampMs);
        const lastSessionEdge = findLatestBefore(sessionEdges, reject.timestampMs);

        const deltas = {
            sinceRoundRotation: lastRoundRotation ? reject.timestampMs - lastRoundRotation.timestampMs : null,
            sinceChainTip: lastChainTip ? reject.timestampMs - lastChainTip.timestampMs : null,
            sinceParentEdge: lastParentEdge ? reject.timestampMs - lastParentEdge.timestampMs : null,
            sinceDatumRefresh: lastRefresh ? reject.timestampMs - lastRefresh.timestampMs : null,
            sinceSessionEdge: lastSessionEdge ? reject.timestampMs - lastSessionEdge.timestampMs : null
        };

        for (const [key, delta] of Object.entries(deltas)) {
            const bucket = `${reason}:${delayBucket(delta)}`;
            buckets[key][bucket] = (buckets[key][bucket] || 0) + 1;
        }

        const parentLate = deltas.sinceParentEdge == null || deltas.sinceParentEdge > 60_000;
        if (parentLate) {
            lateByReason[reason] = (lateByReason[reason] || 0) + 1;
        }
    }

    return {
        lateAfterParentEdge60sByReason: Object.fromEntries(Object.entries(lateByReason).sort((a, b) => b[1] - a[1])),
        buckets
    };
}

function summarizeMinuteBursts(rejects) {
    const counts = new Map();
    for (const reject of withTimestamp(rejects)) {
        const minuteMs = Math.floor(reject.timestampMs / 60_000) * 60_000;
        const minute = new Date(minuteMs).toISOString();
        const reason = reject.rejectionCategory || reject.rejectionReason || "unknown";
        const key = `${minute} ${reason}`;
        counts.set(key, (counts.get(key) || 0) + 1);
    }

    return [...counts.entries()]
        .sort((a, b) => b[1] - a[1])
        .slice(0, 12)
        .map(([key, count]) => {
            const firstSpace = key.indexOf(" ");
            return {
                minuteUtc: key.slice(0, firstSpace),
                reason: key.slice(firstSpace + 1),
                count
            };
        });
}

async function fetchNodeReport(label, baseUrl, args, sinceMs, untilMs) {
    const window = args.window || "12h";
    const limit = Number(args.limit || 5000);
    const [summary, rejectSeries, eventSeries, coinbaserSeries] = await Promise.all([
        fetchJson(baseUrl, "/api/network/summary"),
        fetchJson(baseUrl, "/api/network/share-diagnostics", {
            window,
            source: args.source || "datum",
            accepted: false,
            limit
        }),
        fetchJson(baseUrl, "/api/network/events", {
            window,
            limit
        }),
        fetchJson(baseUrl, "/api/network/coinbaser-diagnostics", {
            window,
            limit
        })
    ]);

    const rejects = filterByTime(Array.isArray(rejectSeries?.events) ? rejectSeries.events : [], sinceMs, untilMs);
    const events = filterByTime(Array.isArray(eventSeries?.events) ? eventSeries.events : [], sinceMs, untilMs);
    const coinbaserFetches = filterByTime(Array.isArray(coinbaserSeries?.events) ? coinbaserSeries.events : [], sinceMs, untilMs);
    const diagnostics = summary.localDatumDiagnostics || {};
    const eventCounts = countBy(events, event => event.eventType);

    return {
        label,
        baseUrl,
        summary: {
            round: summary.currentRoundNumber,
            tipHeight: summary.currentTipBlockHeight,
            currentStateId: summary.currentStateId,
            candidateStateId: summary.candidateStateId,
            acceptanceRate: formatPercent(diagnostics.acceptedCount ?? 0, diagnostics.totalSubmissions ?? 0),
            acceptedCount: diagnostics.acceptedCount ?? 0,
            rejectedCount: diagnostics.rejectedCount ?? 0,
            totalSubmissions: diagnostics.totalSubmissions ?? 0,
            localDatumHashrateDisplay: summary.localDatumHashrateDisplay,
            localDatumMiners: summary.localDatumMiners || []
        },
        windowed: {
            rejectCount: rejects.length,
            rejectReasons: countBy(rejects, reject => reject.rejectionCategory || reject.rejectionReason),
            rejectRounds: topEntries(countBy(rejects, reject => String(reject.currentRoundNumber)), 12),
            rejectMiners: topEntries(countBy(rejects, reject => reject.minerAddress), 12),
            topMinuteBursts: summarizeMinuteBursts(rejects),
            eventCount: events.length,
            eventCounts,
            coinbaserFetchCount: coinbaserFetches.length,
            temporarySlotZeroFetchCount: coinbaserFetches.filter(fetch => fetch.usingTemporarySlotZero).length,
            slowCoinbaserFetchCount: coinbaserFetches.filter(fetch => Number(fetch.durationMs) >= 100).length,
            rejectCorrelation: summarizeRejectCorrelation(rejects, events)
        }
    };
}

function printNode(report) {
    console.log(`\n[${report.label}] ${report.baseUrl}`);
    console.log(`  Round/tip: ${report.summary.round} / ${report.summary.tipHeight ?? "--"}`);
    console.log(`  Current: ${report.summary.currentStateId}`);
    console.log(`  Candidate: ${report.summary.candidateStateId}`);
    console.log(`  DATUM acceptance: ${report.summary.acceptedCount}/${report.summary.totalSubmissions} (${report.summary.acceptanceRate})`);
    console.log(`  Local hashrate: ${report.summary.localDatumHashrateDisplay || "--"}`);
    console.log(`  Window rejects: ${report.windowed.rejectCount}`);
    console.log(`  Reasons: ${JSON.stringify(report.windowed.rejectReasons)}`);
    console.log(`  Reject rounds: ${JSON.stringify(report.windowed.rejectRounds)}`);
    console.log(`  Events: ${JSON.stringify(topEntries(report.windowed.eventCounts, 8))}`);
    console.log(`  Coinbaser fetches: ${report.windowed.coinbaserFetchCount} (temporary slot-0: ${report.windowed.temporarySlotZeroFetchCount}, slow>=100ms: ${report.windowed.slowCoinbaserFetchCount})`);
    console.log(`  Late rejects >60s after chain-tip/round-rotation: ${JSON.stringify(report.windowed.rejectCorrelation.lateAfterParentEdge60sByReason)}`);
    if (report.windowed.topMinuteBursts.length > 0) {
        console.log(`  Top reject bursts: ${JSON.stringify(report.windowed.topMinuteBursts.slice(0, 5))}`);
    }
}

async function main() {
    const args = parseArgs(process.argv.slice(2));
    if (!args["main-url"]) {
        usage();
    }

    const nowMs = Date.now();
    const sinceMs = parseBound(args.since, nowMs - 12 * 60 * 60 * 1000);
    const untilMs = parseBound(args.until, nowMs);
    if (sinceMs > untilMs) {
        throw new Error("--since must be before --until");
    }

    const mainReport = await fetchNodeReport("main", args["main-url"], args, sinceMs, untilMs);
    const peerReport = args["peer-url"]
        ? await fetchNodeReport("peer", args["peer-url"], args, sinceMs, untilMs)
        : null;
    const comparison = peerReport
        ? {
            currentConverged: mainReport.summary.currentStateId === peerReport.summary.currentStateId,
            candidateConverged: mainReport.summary.candidateStateId === peerReport.summary.candidateStateId,
            tipConverged: mainReport.summary.tipHeight === peerReport.summary.tipHeight
        }
        : null;

    console.log(`[boot-laptop-issue-report] ${new Date(sinceMs).toISOString()} to ${new Date(untilMs).toISOString()}`);
    printNode(mainReport);
    if (peerReport) {
        printNode(peerReport);
    }

    if (comparison) {
        console.log(`\n[comparison] ${JSON.stringify(comparison)}`);
    }

    const output = {
        generatedAtUtc: new Date().toISOString(),
        sinceUtc: new Date(sinceMs).toISOString(),
        untilUtc: new Date(untilMs).toISOString(),
        main: mainReport,
        peer: peerReport,
        comparison
    };

    if (args.out) {
        fs.writeFileSync(args.out, JSON.stringify(output, null, 2));
        console.log(`\nWrote ${args.out}`);
    }
}

main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
});
