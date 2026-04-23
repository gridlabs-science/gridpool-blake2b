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
    console.error("Usage: node scripts/boot-soak-report.mjs --main-url <url> [--peer-url <url>] [--window 12h] [--limit 5000] [--out report.json]");
    process.exit(1);
}

function normalizeBaseUrl(url) {
    return url.replace(/\/+$/, "");
}

function parseDate(value) {
    const time = Date.parse(value ?? "");
    return Number.isFinite(time) ? time : null;
}

function formatPercent(numerator, denominator) {
    if (!denominator) {
        return "--";
    }

    return `${((numerator / denominator) * 100).toFixed(2)}%`;
}

function countBy(items, selector) {
    const map = new Map();
    for (const item of items) {
        const key = selector(item) || "unknown";
        map.set(key, (map.get(key) || 0) + 1);
    }
    return Object.fromEntries([...map.entries()].sort((a, b) => b[1] - a[1]));
}

function percentile(values, p) {
    const sorted = values
        .filter(value => Number.isFinite(value))
        .sort((a, b) => a - b);
    if (!sorted.length) {
        return null;
    }

    const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
    return sorted[index];
}

function summarizeSessionOverlap(items, selector, nowMs = Date.now()) {
    const groups = new Map();
    for (const item of items) {
        const key = selector(item);
        const startMs = parseDate(item.startedUtc);
        if (!key || startMs == null) {
            continue;
        }

        const endMs = parseDate(item.closedUtc) ?? nowMs;
        if (endMs < startMs) {
            continue;
        }

        if (!groups.has(key)) {
            groups.set(key, []);
        }

        groups.get(key).push({
            sessionId: item.sessionId,
            startMs,
            endMs
        });
    }

    let maxConcurrent = 0;
    const overlappingSessionIds = new Set();

    for (const group of groups.values()) {
        group.sort((a, b) => a.startMs - b.startMs || a.endMs - b.endMs);
        let active = [];
        for (const interval of group) {
            active = active.filter(item => item.endMs > interval.startMs);
            if (active.length > 0) {
                overlappingSessionIds.add(interval.sessionId);
                for (const overlapping of active) {
                    overlappingSessionIds.add(overlapping.sessionId);
                }
            }

            active.push(interval);
            maxConcurrent = Math.max(maxConcurrent, active.length);
        }
    }

    return {
        maxConcurrent,
        sessionsWithOverlap: overlappingSessionIds.size
    };
}

function summarizeDatumResponses(responses) {
    const items = Array.isArray(responses) ? responses : [];
    const lowDifficulty = items.filter(item => item.rejectionReason === "Low difficulty");
    return {
        count: items.length,
        acceptedSamples: items.filter(item => item.accepted).length,
        rejected: items.filter(item => !item.accepted).length,
        slowOver500ms: items.filter(item => Number(item.totalDurationMs) >= 500).length,
        p95TotalMs: percentile(items.map(item => Number(item.totalDurationMs)), 95),
        p95ValidationMs: percentile(items.map(item => Number(item.validationDurationMs)), 95),
        p95SendMs: percentile(items.map(item => Number(item.responseSendDurationMs)), 95),
        rejectionReasons: countBy(items.filter(item => !item.accepted), item => item.rejectionReason),
        lowDifficulty: {
            count: lowDifficulty.length,
            nonceOnly: lowDifficulty.filter(item => item.nonceOnlySubmit).length,
            cached: lowDifficulty.filter(item => item.usedCachedJob).length,
            quickDiff: lowDifficulty.filter(item => item.quickDiff).length
        }
    };
}

function summarizeDatumSessions(sessions) {
    const items = Array.isArray(sessions) ? sessions : [];
    const nowMs = Date.now();
    const closed = items.filter(item => item.closedUtc);
    const durations = closed.map(item => Number(item.durationMs));
    const handshakeDurations = items.map(item => Number(item.handshakeMs));
    const idleBeforeClose = closed.map(item => Number(item.idleBeforeCloseMs));
    const overlapByIdentity = summarizeSessionOverlap(items, item => item.clientIdentityKey, nowMs);
    const overlapByPayout = summarizeSessionOverlap(items, item => item.lockedPayoutAddress, nowMs);
    const overlapByRemote = summarizeSessionOverlap(items, item => item.remoteEndpoint, nowMs);

    return {
        count: items.length,
        activeCount: items.filter(item => !item.closedUtc).length,
        handshakeCompleted: items.filter(item => item.handshakeCompleted).length,
        protocolCounts: countBy(items, item => item.protocol),
        closeDispositions: countBy(closed, item => item.closeDisposition),
        serverCloseEventTypes: countBy(closed.filter(item => item.serverCloseEventType), item => item.serverCloseEventType),
        serverInitiatedCount: closed.filter(item => item.serverInitiatedClose).length,
        sessionsWithShares: items.filter(item => Number(item.shareResponseCount) > 0).length,
        zeroShareSessions: items.filter(item => Number(item.shareResponseCount) === 0).length,
        zeroWorkAfterHello: items.filter(item =>
            item.handshakeCompleted &&
            Number(item.coinbaserFetchCount) === 0 &&
            Number(item.shareResponseCount) === 0).length,
        shortIdleClosures25sTo40s: closed.filter(item =>
            Number(item.idleBeforeCloseMs) >= 25_000 &&
            Number(item.idleBeforeCloseMs) <= 40_000).length,
        shortHandshakeNoWork25sTo40s: closed.filter(item =>
            item.handshakeCompleted &&
            Number(item.coinbaserFetchCount) === 0 &&
            Number(item.shareResponseCount) === 0 &&
            Number(item.durationMs) >= 25_000 &&
            Number(item.durationMs) <= 40_000).length,
        p50DurationMs: percentile(durations, 50),
        p95DurationMs: percentile(durations, 95),
        p95HandshakeMs: percentile(handshakeDurations, 95),
        p95IdleBeforeCloseMs: percentile(idleBeforeClose, 95),
        totalCoinbaserFetches: items.reduce((sum, item) => sum + Number(item.coinbaserFetchCount || 0), 0),
        totalRefreshRequests: items.reduce((sum, item) => sum + Number(item.refreshRequestCount || 0), 0),
        totalShareResponses: items.reduce((sum, item) => sum + Number(item.shareResponseCount || 0), 0),
        overlap: {
            sameIdentity: overlapByIdentity,
            samePayout: overlapByPayout,
            sameRemote: overlapByRemote
        }
    };
}

function correlateRejects(rejects, events) {
    const roundRotations = events
        .filter(event => event.eventType === "round-rotation")
        .map(event => ({ ...event, timestampMs: parseDate(event.timestampUtc) }))
        .filter(event => event.timestampMs != null)
        .sort((a, b) => a.timestampMs - b.timestampMs);

    const chainTips = events
        .filter(event => event.eventType === "chain-tip")
        .map(event => ({ ...event, timestampMs: parseDate(event.timestampUtc) }))
        .filter(event => event.timestampMs != null)
        .sort((a, b) => a.timestampMs - b.timestampMs);

    const parentBoundaries = [...roundRotations, ...chainTips]
        .sort((a, b) => a.timestampMs - b.timestampMs);

    const result = {
        payoutMismatchAfter60s: 0,
        wrongParentAfter60s: 0,
        wrongParentWithin10sBeforeChainTip: 0,
        wrongParentWithin10sAfterChainTip: 0,
        wrongParentOutside10sChainTipWindow: 0,
        wrongParentAfter60sOutside10sChainTipWindow: 0,
        fallbackAfter60s: 0,
        unmatched: 0
    };

    for (const reject of rejects) {
        const rejectMs = parseDate(reject.timestampUtc);
        if (rejectMs == null) {
            result.unmatched += 1;
            continue;
        }

        if (reject.rejectionCategory === "Payout mismatch") {
            const lastRotation = findLatestEventBefore(roundRotations, rejectMs);
            if (!lastRotation || rejectMs - lastRotation.timestampMs > 60_000) {
                result.payoutMismatchAfter60s += 1;
            }
            continue;
        }

        if (reject.rejectionCategory === "Wrong parent block") {
            const lastParentBoundary = findLatestEventBefore(parentBoundaries, rejectMs);
            const afterParentBoundary60s = !lastParentBoundary || rejectMs - lastParentBoundary.timestampMs > 60_000;

            const previousChainTip = findLatestEventBefore(chainTips, rejectMs);
            const nextChainTip = findNextEventAfter(chainTips, rejectMs);
            const afterChainTipMs = previousChainTip ? rejectMs - previousChainTip.timestampMs : null;
            const beforeChainTipMs = nextChainTip ? nextChainTip.timestampMs - rejectMs : null;
            let outsideChainTipWindow = false;
            if (beforeChainTipMs != null && beforeChainTipMs <= 10_000) {
                result.wrongParentWithin10sBeforeChainTip += 1;
            } else if (afterChainTipMs != null && afterChainTipMs <= 10_000) {
                result.wrongParentWithin10sAfterChainTip += 1;
            } else {
                result.wrongParentOutside10sChainTipWindow += 1;
                outsideChainTipWindow = true;
            }

            if (afterParentBoundary60s) {
                result.wrongParentAfter60s += 1;
                if (outsideChainTipWindow) {
                    result.wrongParentAfter60sOutside10sChainTipWindow += 1;
                }
            }
            continue;
        }

        if (reject.rejectionCategory === "Solo fallback template") {
            const lastParentBoundary = findLatestEventBefore(parentBoundaries, rejectMs);
            if (!lastParentBoundary || rejectMs - lastParentBoundary.timestampMs > 60_000) {
                result.fallbackAfter60s += 1;
            }
            continue;
        }
    }

    return result;
}

function findLatestEventBefore(events, timestampMs) {
    let latest = null;
    for (const event of events) {
        if (event.timestampMs <= timestampMs) {
            latest = event;
        } else {
            break;
        }
    }
    return latest;
}

function findNextEventAfter(events, timestampMs) {
    for (const event of events) {
        if (event.timestampMs >= timestampMs) {
            return event;
        }
    }

    return null;
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

async function fetchOptionalJson(baseUrl, path, params = {}) {
    try {
        return await fetchJson(baseUrl, path, params);
    } catch {
        return { events: [] };
    }
}

async function fetchNodeReport(baseUrl, window, limit) {
    const [summary, rejects, events, datumResponses, datumSessions] = await Promise.all([
        fetchJson(baseUrl, "/api/network/summary"),
        fetchJson(baseUrl, "/api/network/share-diagnostics", {
            window,
            source: "datum",
            accepted: false,
            limit
        }),
        fetchJson(baseUrl, "/api/network/events", {
            window,
            limit
        }),
        fetchOptionalJson(baseUrl, "/api/network/datum-share-responses", {
            window,
            limit
        }),
        fetchOptionalJson(baseUrl, "/api/network/datum-sessions", {
            window,
            limit
        })
    ]);

    const rejectEvents = Array.isArray(rejects?.events) ? rejects.events : [];
    const eventItems = Array.isArray(events?.events) ? events.events : [];
    const datumResponseItems = Array.isArray(datumResponses?.events) ? datumResponses.events : [];
    const datumSessionItems = Array.isArray(datumSessions?.events) ? datumSessions.events : [];
    const correlated = correlateRejects(rejectEvents, eventItems);
    const rejectionCounts = countBy(rejectEvents, reject => reject.rejectionCategory || reject.rejectionReason);
    const eventCounts = countBy(eventItems, event => event.eventType);
    const restartSignals = eventItems.filter(event =>
        ["datum-session-reset", "datum-session-close"].includes(event.eventType));

    return {
        baseUrl,
        summary,
        rejectCount: rejectEvents.length,
        rejectionCounts,
        datumResponses: summarizeDatumResponses(datumResponseItems),
        datumSessions: summarizeDatumSessions(datumSessionItems),
        correlatedRejects: correlated,
        eventCount: eventItems.length,
        eventCounts,
        freshParentLearnedCount: eventCounts["fresh-parent-learned"] || 0,
        restartSignalCount: restartSignals.length
    };
}

function buildComparison(mainReport, peerReport) {
    if (!peerReport) {
        return null;
    }

    const mainSummary = mainReport.summary;
    const peerSummary = peerReport.summary;
    const currentConverged =
        mainSummary.currentStateId === peerSummary.currentStateId &&
        mainSummary.candidateStateId === peerSummary.candidateStateId &&
        mainSummary.currentTipBlockHeight === peerSummary.currentTipBlockHeight;

    return {
        currentConverged,
        mainCurrentStateId: mainSummary.currentStateId,
        peerCurrentStateId: peerSummary.currentStateId,
        mainCandidateStateId: mainSummary.candidateStateId,
        peerCandidateStateId: peerSummary.candidateStateId,
        mainTipHeight: mainSummary.currentTipBlockHeight,
        peerTipHeight: peerSummary.currentTipBlockHeight
    };
}

function printSummary(report, label) {
    const summary = report.summary;
    const diagnostics = summary.localDatumDiagnostics || {};
    console.log(`\n[${label}] ${report.baseUrl}`);
    console.log(`  Round: ${summary.currentRoundNumber}`);
    console.log(`  Tip: ${summary.currentTipBlockHeight ?? "--"}`);
    console.log(`  Current state: ${summary.currentStateId}`);
    console.log(`  Candidate state: ${summary.candidateStateId}`);
    console.log(`  DATUM acceptance: ${diagnostics.acceptedCount ?? 0}/${diagnostics.totalSubmissions ?? 0} (${formatPercent(diagnostics.acceptedCount ?? 0, diagnostics.totalSubmissions ?? 0)})`);
    console.log(`  Rejects: ${report.rejectCount}`);
    console.log(`  Reject reasons: ${JSON.stringify(report.rejectionCounts)}`);
    console.log(`  DATUM response p95 total/validation/send: ${report.datumResponses.p95TotalMs?.toFixed?.(1) ?? "--"} ms / ${report.datumResponses.p95ValidationMs?.toFixed?.(1) ?? "--"} ms / ${report.datumResponses.p95SendMs?.toFixed?.(1) ?? "--"} ms`);
    console.log(`  DATUM response rejects: ${JSON.stringify(report.datumResponses.rejectionReasons)}`);
    console.log(`  Low-diff context: ${JSON.stringify(report.datumResponses.lowDifficulty)}`);
    console.log(`  DATUM sessions: ${JSON.stringify({
        count: report.datumSessions.count,
        active: report.datumSessions.activeCount,
        p95DurationMs: report.datumSessions.p95DurationMs,
        shortHandshakeNoWork25sTo40s: report.datumSessions.shortHandshakeNoWork25sTo40s,
        closeDispositions: report.datumSessions.closeDispositions,
        overlap: report.datumSessions.overlap
    })}`);
    console.log(`  Late rejects >60s: ${JSON.stringify(report.correlatedRejects)}`);
    console.log(`  Fresh parents learned: ${report.freshParentLearnedCount}`);
    console.log(`  Events: ${JSON.stringify(report.eventCounts)}`);
    console.log(`  Coinbaser avg/p95: ${summary.coinbaserDiagnostics?.averageDurationMs?.toFixed?.(2) ?? "--"} ms / ${summary.coinbaserDiagnostics?.p95DurationMs?.toFixed?.(2) ?? "--"} ms`);
    console.log(`  Slow fetch count: ${summary.coinbaserDiagnostics?.slowFetchCount ?? 0}`);
}

async function main() {
    const args = parseArgs(process.argv.slice(2));
    if (!args["main-url"]) {
        usage();
    }

    const window = args.window || "12h";
    const limit = Number(args.limit || 5000);

    const mainReport = await fetchNodeReport(args["main-url"], window, limit);
    const peerReport = args["peer-url"]
        ? await fetchNodeReport(args["peer-url"], window, limit)
        : null;
    const comparison = buildComparison(mainReport, peerReport);

    printSummary(mainReport, "main");
    if (peerReport) {
        printSummary(peerReport, "peer");
    }

    if (comparison) {
        console.log("\n[comparison]");
        console.log(`  Converged now: ${comparison.currentConverged}`);
        console.log(`  Main candidate: ${comparison.mainCandidateStateId}`);
        console.log(`  Peer candidate: ${comparison.peerCandidateStateId}`);
    }

    const output = {
        generatedAtUtc: new Date().toISOString(),
        window,
        limit,
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
