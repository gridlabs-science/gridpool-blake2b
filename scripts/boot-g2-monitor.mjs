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
    console.error("Usage: node scripts/boot-g2-monitor.mjs --main-url <url> [--peer-url <url>] [--duration-seconds 300] [--interval-seconds 5] [--flush-seconds 30] [--request-timeout-ms 4000] [--out report.json]");
    process.exit(1);
}

function normalizeBaseUrl(url) {
    return url.replace(/\/+$/, "");
}

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

function parseDate(value) {
    const time = Date.parse(value ?? "");
    return Number.isFinite(time) ? time : null;
}

function currentIso() {
    return new Date().toISOString();
}

function writeJsonAtomic(filePath, payload) {
    const tempPath = `${filePath}.tmp`;
    fs.writeFileSync(tempPath, `${JSON.stringify(payload, null, 2)}\n`);
    fs.renameSync(tempPath, filePath);
}

function appendJsonLine(filePath, payload) {
    fs.appendFileSync(filePath, `${JSON.stringify(payload)}\n`);
}

function getCheckpointPath(outPath) {
    if (!outPath) {
        return null;
    }

    return outPath.endsWith(".json")
        ? `${outPath.slice(0, -5)}.checkpoints.jsonl`
        : `${outPath}.checkpoints.jsonl`;
}

function extractItems(payload) {
    if (!payload || typeof payload !== "object") {
        return [];
    }

    if (Array.isArray(payload.events)) {
        return payload.events;
    }

    if (Array.isArray(payload.sessions)) {
        return payload.sessions;
    }

    if (Array.isArray(payload.items)) {
        return payload.items;
    }

    return [];
}

function chooseWindow(durationSeconds) {
    if (durationSeconds <= 60 * 60) return "1h";
    if (durationSeconds <= 12 * 60 * 60) return "12h";
    if (durationSeconds <= 24 * 60 * 60) return "24h";
    return "7d";
}

function apiWindowForTelemetry(durationSeconds) {
    if (durationSeconds <= 12 * 60 * 60) return "12h";
    if (durationSeconds <= 24 * 60 * 60) return "24h";
    return "7d";
}

function countBy(items, selector) {
    const counts = new Map();
    for (const item of items) {
        const key = selector(item) || "unknown";
        counts.set(key, (counts.get(key) || 0) + 1);
    }
    return Object.fromEntries([...counts.entries()].sort((a, b) => b[1] - a[1]));
}

function formatPercent(numerator, denominator) {
    if (!denominator) return "--";
    return `${((numerator / denominator) * 100).toFixed(2)}%`;
}

function percentile(values, p) {
    const sorted = values
        .filter(value => Number.isFinite(value))
        .sort((a, b) => a - b);
    if (!sorted.length) return null;
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

function summarizeDatumProtocolEvents(events) {
    const items = Array.isArray(events) ? events : [];
    const sendEvents = items.filter(item => item.direction === "send" && item.eventType === "send");
    const closeEvents = items.filter(item => item.eventType === "session-close");
    const powOutcomes = items.filter(item => item.eventType === "pow-submit-outcome");
    const sorted = items
        .map(item => ({ ...item, timestampMs: parseDate(item.timestampUtc) }))
        .filter(item => item.timestampMs != null)
        .sort((a, b) => a.timestampMs - b.timestampMs || Number(a.sequence || 0) - Number(b.sequence || 0));

    const lastSendBySession = new Map();
    const lastRecvBySession = new Map();
    const closeGapAfterLastSendMs = [];
    const closeGapAfterLastRecvMs = [];
    let clientNoDataCloseAfterRecentSend5s = 0;
    let clientNoDataCloseAfterRecentRecv5s = 0;

    for (const item of sorted) {
        if (item.direction === "send" && item.eventType === "send") {
            lastSendBySession.set(item.sessionId, item.timestampMs);
        }
        if (item.direction === "recv") {
            lastRecvBySession.set(item.sessionId, item.timestampMs);
        }
        if (item.eventType !== "session-close") {
            continue;
        }

        const lastSendMs = lastSendBySession.get(item.sessionId);
        const lastRecvMs = lastRecvBySession.get(item.sessionId);
        if (lastSendMs != null) {
            const gapMs = Math.max(0, item.timestampMs - lastSendMs);
            closeGapAfterLastSendMs.push(gapMs);
            if (item.closeDisposition === "client-disconnected-no-data" && gapMs <= 5000) {
                clientNoDataCloseAfterRecentSend5s += 1;
            }
        }
        if (lastRecvMs != null) {
            const gapMs = Math.max(0, item.timestampMs - lastRecvMs);
            closeGapAfterLastRecvMs.push(gapMs);
            if (item.closeDisposition === "client-disconnected-no-data" && gapMs <= 5000) {
                clientNoDataCloseAfterRecentRecv5s += 1;
            }
        }
    }

    return {
        count: items.length,
        directionCounts: countBy(items, item => item.direction),
        eventCounts: countBy(items, item => item.eventType),
        sendLabels: countBy(sendEvents, item => item.messageLabel || "unlabeled"),
        closeDispositions: countBy(closeEvents, item => item.closeDisposition),
        powRejectReasons: countBy(powOutcomes.filter(item => item.accepted === false), item => item.rejectionReason),
        recvHeaderEof: items.filter(item => item.eventType === "recv-header-eof").length,
        partialHeaders: items.filter(item => item.eventType === "partial-header").length,
        partialBodies: items.filter(item => item.eventType === "partial-body").length,
        decryptFailures: items.filter(item => item.eventType === "decrypt-failed").length,
        sendFailures: items.filter(item => item.eventType === "send-failed").length,
        p95SendDurationMs: percentile(sendEvents.map(item => Number(item.durationMs)), 95),
        p95CloseGapAfterLastSendMs: percentile(closeGapAfterLastSendMs, 95),
        p95CloseGapAfterLastRecvMs: percentile(closeGapAfterLastRecvMs, 95),
        clientNoDataCloseAfterRecentSend5s,
        clientNoDataCloseAfterRecentRecv5s
    };
}

async function fetchJson(baseUrl, path, params = {}, timeoutMs = 4000) {
    const url = new URL(`${normalizeBaseUrl(baseUrl)}${path}`);
    for (const [key, value] of Object.entries(params)) {
        if (value != null) {
            url.searchParams.set(key, String(value));
        }
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const response = await fetch(url, { signal: controller.signal });
        if (!response.ok) {
            throw new Error(`${url} returned ${response.status}`);
        }

        return response.json();
    } catch (error) {
        if (error?.name === "AbortError") {
            throw new Error(`${url} timed out after ${timeoutMs} ms`);
        }

        throw error;
    } finally {
        clearTimeout(timer);
    }
}

async function fetchOptionalJson(baseUrl, path, params = {}, timeoutMs = 4000) {
    try {
        return await fetchJson(baseUrl, path, params, timeoutMs);
    } catch (error) {
        return {
            events: [],
            _diagnosticUnavailable: true,
            _diagnosticError: String(error?.message || error)
        };
    }
}

function diagnosticStatus(payload) {
    return payload?._diagnosticUnavailable
        ? { available: false, error: payload._diagnosticError }
        : { available: true, error: null };
}

function startInterval(state, type, timestampMs) {
    if (!state[type].active) {
        state[type].active = true;
        state[type].startMs = timestampMs;
    }
}

function endInterval(state, type, timestampMs) {
    if (!state[type].active) {
        return;
    }

    const durationMs = Math.max(0, timestampMs - state[type].startMs);
    state[type].intervals.push({
        startUtc: new Date(state[type].startMs).toISOString(),
        endUtc: new Date(timestampMs).toISOString(),
        durationMs
    });
    state[type].longestMs = Math.max(state[type].longestMs, durationMs);
    state[type].active = false;
    state[type].startMs = null;
}

function finalizeIntervals(state, timestampMs) {
    for (const type of Object.keys(state)) {
        endInterval(state, type, timestampMs);
    }
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
        }
    }

    return result;
}

async function fetchAnalysis(baseUrl, durationSeconds, requestTimeoutMs = 4000) {
    const window = apiWindowForTelemetry(durationSeconds);
    const [summary, rejectsSeries, eventsSeries, datumResponsesSeries, datumSessionsSeries, datumProtocolSeries] = await Promise.all([
        fetchJson(baseUrl, "/api/network/summary", {}, requestTimeoutMs),
        fetchOptionalJson(baseUrl, "/api/network/share-diagnostics", {
            window,
            source: "datum",
            accepted: false,
            limit: 5000
        }, requestTimeoutMs),
        fetchOptionalJson(baseUrl, "/api/network/events", {
            window,
            limit: 5000
        }, requestTimeoutMs),
        fetchOptionalJson(baseUrl, "/api/network/datum-share-responses", {
            window,
            limit: 5000
        }, requestTimeoutMs),
        fetchOptionalJson(baseUrl, "/api/network/datum-sessions", {
            window,
            limit: 5000
        }, requestTimeoutMs),
        fetchOptionalJson(baseUrl, "/api/network/datum-protocol-events", {
            window,
            limit: 20000
        }, requestTimeoutMs)
    ]);

    const rejects = extractItems(rejectsSeries);
    const events = extractItems(eventsSeries);
    const datumResponses = extractItems(datumResponsesSeries);
    const datumSessions = extractItems(datumSessionsSeries);
    const datumProtocol = extractItems(datumProtocolSeries);
    const eventCounts = countBy(events, event => event.eventType);

    return {
        summary,
        diagnosticAvailability: {
            shareDiagnostics: diagnosticStatus(rejectsSeries),
            events: diagnosticStatus(eventsSeries),
            datumResponses: diagnosticStatus(datumResponsesSeries),
            datumSessions: diagnosticStatus(datumSessionsSeries),
            datumProtocol: diagnosticStatus(datumProtocolSeries)
        },
        rejectCount: rejects.length,
        rejectReasons: countBy(rejects, reject => reject.rejectionCategory || reject.rejectionReason),
        datumResponses: summarizeDatumResponses(datumResponses),
        datumSessions: summarizeDatumSessions(datumSessions),
        datumProtocol: summarizeDatumProtocolEvents(datumProtocol),
        lateRejects: correlateRejects(rejects, events),
        eventCounts,
        freshParentLearnedCount: eventCounts["fresh-parent-learned"] || 0,
        restartSignalCount: events.filter(event => ["datum-session-reset", "datum-session-close"].includes(event.eventType)).length
    };
}

function snapshotDivergence(divergence) {
    return {
        candidate: {
            longestMs: divergence.candidate.longestMs,
            intervalCount: divergence.candidate.intervals.length,
            intervals: divergence.candidate.intervals
        },
        current: {
            longestMs: divergence.current.longestMs,
            intervalCount: divergence.current.intervals.length,
            intervals: divergence.current.intervals
        },
        tip: {
            longestMs: divergence.tip.longestMs,
            intervalCount: divergence.tip.intervals.length,
            intervals: divergence.tip.intervals
        }
    };
}

function summarizeTipMonotonicity(samples, side) {
    const regressions = [];
    let previous = null;

    for (const sample of samples) {
        const node = sample[side];
        const height = Number(node?.currentTipBlockHeight);
        if (!Number.isFinite(height)) {
            continue;
        }

        const current = {
            timestampUtc: sample.timestampUtc,
            height,
            hash: node.currentTipBlockHash ?? null,
            round: node.currentRoundNumber ?? null
        };

        if (previous && current.height < previous.height) {
            regressions.push({
                fromTimestampUtc: previous.timestampUtc,
                toTimestampUtc: current.timestampUtc,
                fromHeight: previous.height,
                toHeight: current.height,
                delta: current.height - previous.height,
                fromHash: previous.hash,
                toHash: current.hash,
                fromRound: previous.round,
                toRound: current.round
            });
        }

        previous = current;
    }

    return {
        regressionCount: regressions.length,
        worstRegression: regressions
            .slice()
            .sort((a, b) => a.delta - b.delta)[0] ?? null,
        regressions
    };
}

function buildProgressReport({
    mainUrl,
    peerUrl,
    durationSeconds,
    intervalSeconds,
    startedAtMs,
    samples,
    failures,
    observedRounds,
    divergence,
    latestMainSummary,
    latestPeerSummary,
    flushReason
}) {
    return {
        generatedAtUtc: currentIso(),
        partial: true,
        complete: false,
        flushReason,
        mainUrl,
        peerUrl,
        durationSeconds,
        intervalSeconds,
        elapsedSeconds: Math.max(0, Math.round((Date.now() - startedAtMs) / 1000)),
        sampleCount: samples.length,
        failures,
        observedRounds,
        divergence: snapshotDivergence(divergence),
        tipMonotonicity: {
            main: summarizeTipMonotonicity(samples, "main"),
            peer: peerUrl ? summarizeTipMonotonicity(samples, "peer") : null
        },
        latestSample: samples.length > 0 ? samples[samples.length - 1] : null,
        latestMainSummary,
        latestPeerSummary
    };
}

function buildVerdict(report) {
    const verdict = {
        g2_1: "unknown",
        g2_2: "unknown",
        g2_3: "unknown"
    };

    const lateRejectFailures = [];
    if (report.mainAnalysis.lateRejects.payoutMismatchAfter60s > 0) lateRejectFailures.push("main payout mismatch >60s");
    if (report.mainAnalysis.lateRejects.wrongParentAfter60sOutside10sChainTipWindow > 0) lateRejectFailures.push("main wrong parent >60s outside chain-tip window");
    if (report.mainAnalysis.lateRejects.fallbackAfter60s > 0) lateRejectFailures.push("main fallback >60s");
    if (report.peerAnalysis) {
        if (report.peerAnalysis.lateRejects.payoutMismatchAfter60s > 0) lateRejectFailures.push("peer payout mismatch >60s");
        if (report.peerAnalysis.lateRejects.wrongParentAfter60sOutside10sChainTipWindow > 0) lateRejectFailures.push("peer wrong parent >60s outside chain-tip window");
        if (report.peerAnalysis.lateRejects.fallbackAfter60s > 0) lateRejectFailures.push("peer fallback >60s");
    }
    const requiredRejectDiagnosticsAvailable =
        report.mainAnalysis.diagnosticAvailability.shareDiagnostics.available &&
        report.mainAnalysis.diagnosticAvailability.events.available &&
        (!report.peerAnalysis || (
            report.peerAnalysis.diagnosticAvailability.shareDiagnostics.available &&
            report.peerAnalysis.diagnosticAvailability.events.available));
    verdict.g2_1 = !requiredRejectDiagnosticsAvailable
        ? "not-evaluated: private reject/event diagnostics unavailable"
        : (lateRejectFailures.length === 0 ? "pass-ish" : `attention: ${lateRejectFailures.join(", ")}`);

    const longestCandidateDivergenceMs = report.divergence.candidate.longestMs;
    const tipRegressionFailures = [];
    if ((report.tipMonotonicity?.main?.regressionCount ?? 0) > 0) tipRegressionFailures.push("main tip height regressed");
    if ((report.tipMonotonicity?.peer?.regressionCount ?? 0) > 0) tipRegressionFailures.push("peer tip height regressed");
    verdict.g2_2 = report.peerUrl
        ? (tipRegressionFailures.length > 0
            ? `attention: ${tipRegressionFailures.join(", ")}`
            : (longestCandidateDivergenceMs <= 15_000 ? "pass-ish" : `attention: candidate divergence ${longestCandidateDivergenceMs} ms`))
        : "not-evaluated";

    const mainCoinbaser = report.mainAnalysis.summary.coinbaserDiagnostics || {};
    const peerCoinbaser = report.peerAnalysis?.summary?.coinbaserDiagnostics || null;
    const mainOk = (mainCoinbaser.slowFetchCount ?? 0) === 0 &&
        (mainCoinbaser.averageDurationMs ?? 0) < 10 &&
        (mainCoinbaser.p95DurationMs ?? 0) < 50 &&
        (mainCoinbaser.averageStateReadDurationMs ?? 0) < 25;
    const peerOk = !peerCoinbaser || (
        (peerCoinbaser.slowFetchCount ?? 0) === 0 &&
        (peerCoinbaser.averageDurationMs ?? 0) < 10 &&
        (peerCoinbaser.p95DurationMs ?? 0) < 50 &&
        (peerCoinbaser.averageStateReadDurationMs ?? 0) < 25
    );
    verdict.g2_3 = mainOk && peerOk ? "pass-ish" : "attention: coinbaser thresholds exceeded";

    return verdict;
}

async function main() {
    const args = parseArgs(process.argv.slice(2));
    if (!args["main-url"]) usage();

    const mainUrl = args["main-url"];
    const peerUrl = args["peer-url"] || null;
    const durationSeconds = Number(args["duration-seconds"] || 300);
    const intervalSeconds = Number(args["interval-seconds"] || 5);
    const flushSeconds = Number(args["flush-seconds"] || Math.max(30, intervalSeconds * 2));
    const requestTimeoutMs = Number(args["request-timeout-ms"] || 4000);
    const outPath = args.out || null;
    const checkpointPath = getCheckpointPath(outPath);

    const samples = [];
    const failures = { main: [], peer: [] };
    const divergence = {
        candidate: { active: false, startMs: null, longestMs: 0, intervals: [] },
        current: { active: false, startMs: null, longestMs: 0, intervals: [] },
        tip: { active: false, startMs: null, longestMs: 0, intervals: [] }
    };
    const observedRounds = [];
    const startedAtMs = Date.now();
    const endTime = startedAtMs + durationSeconds * 1000;
    let lastMainRound = null;
    let latestMainSummary = null;
    let latestPeerSummary = null;
    let nextFlushMs = startedAtMs;
    let terminationSignal = null;

    const requestStop = signal => {
        terminationSignal = signal;
    };

    process.on("SIGINT", () => requestStop("SIGINT"));
    process.on("SIGTERM", () => requestStop("SIGTERM"));

    while (Date.now() < endTime && !terminationSignal) {
        const timestampMs = Date.now();
        const sample = { timestampUtc: new Date(timestampMs).toISOString() };

        try {
            sample.main = await fetchJson(mainUrl, "/api/network/summary", {}, requestTimeoutMs);
            latestMainSummary = sample.main;
            if (sample.main.currentRoundNumber !== lastMainRound) {
                observedRounds.push({
                    timestampUtc: sample.timestampUtc,
                    roundNumber: sample.main.currentRoundNumber
                });
                lastMainRound = sample.main.currentRoundNumber;
            }
        } catch (error) {
            failures.main.push({ timestampUtc: sample.timestampUtc, error: String(error.message || error) });
        }

        if (peerUrl) {
            try {
                sample.peer = await fetchJson(peerUrl, "/api/network/summary", {}, requestTimeoutMs);
                latestPeerSummary = sample.peer;
            } catch (error) {
                failures.peer.push({ timestampUtc: sample.timestampUtc, error: String(error.message || error) });
            }
        }

        if (sample.main && sample.peer) {
            const candidateDifferent = sample.main.candidateStateId !== sample.peer.candidateStateId;
            const currentDifferent = sample.main.currentStateId !== sample.peer.currentStateId;
            const tipDifferent = sample.main.currentTipBlockHeight !== sample.peer.currentTipBlockHeight ||
                sample.main.currentTipBlockHash !== sample.peer.currentTipBlockHash;

            if (candidateDifferent) startInterval(divergence, "candidate", timestampMs); else endInterval(divergence, "candidate", timestampMs);
            if (currentDifferent) startInterval(divergence, "current", timestampMs); else endInterval(divergence, "current", timestampMs);
            if (tipDifferent) startInterval(divergence, "tip", timestampMs); else endInterval(divergence, "tip", timestampMs);
        }

        samples.push(sample);

        if (outPath && timestampMs >= nextFlushMs) {
            const progress = buildProgressReport({
                mainUrl,
                peerUrl,
                durationSeconds,
                intervalSeconds,
                startedAtMs,
                samples,
                failures,
                observedRounds,
                divergence,
                latestMainSummary,
                latestPeerSummary,
                flushReason: terminationSignal ? `signal:${terminationSignal}` : "interval"
            });
            writeJsonAtomic(outPath, progress);
            if (checkpointPath) {
                appendJsonLine(checkpointPath, {
                    generatedAtUtc: progress.generatedAtUtc,
                    elapsedSeconds: progress.elapsedSeconds,
                    sampleCount: progress.sampleCount,
                    flushReason: progress.flushReason,
                    main: latestMainSummary
                        ? {
                            round: latestMainSummary.currentRoundNumber,
                            tipHeight: latestMainSummary.currentTipBlockHeight,
                            currentStateId: latestMainSummary.currentStateId,
                            candidateStateId: latestMainSummary.candidateStateId,
                            accepted: latestMainSummary.localDatumDiagnostics?.acceptedCount ?? null,
                            rejected: latestMainSummary.localDatumDiagnostics?.rejectedCount ?? null
                        }
                        : null,
                    peer: latestPeerSummary
                        ? {
                            round: latestPeerSummary.currentRoundNumber,
                            tipHeight: latestPeerSummary.currentTipBlockHeight,
                            currentStateId: latestPeerSummary.currentStateId,
                            candidateStateId: latestPeerSummary.candidateStateId,
                            accepted: latestPeerSummary.localDatumDiagnostics?.acceptedCount ?? null,
                            rejected: latestPeerSummary.localDatumDiagnostics?.rejectedCount ?? null
                        }
                        : null
                });
            }
            console.log(`[progress] elapsed=${progress.elapsedSeconds}s samples=${progress.sampleCount} mainRound=${latestMainSummary?.currentRoundNumber ?? "--"} peerRound=${latestPeerSummary?.currentRoundNumber ?? "--"}`);
            nextFlushMs = timestampMs + (flushSeconds * 1000);
        }

        if (!terminationSignal) {
            await sleep(intervalSeconds * 1000);
        }
    }

    finalizeIntervals(divergence, Date.now());

    const actualDurationSeconds = Math.max(1, Math.round((Date.now() - startedAtMs) / 1000));
    const mainAnalysis = await fetchAnalysis(mainUrl, actualDurationSeconds, requestTimeoutMs);
    let peerAnalysis = null;
    let peerAnalysisError = null;
    if (peerUrl) {
        try {
            peerAnalysis = await fetchAnalysis(peerUrl, actualDurationSeconds, requestTimeoutMs);
        } catch (error) {
            peerAnalysisError = String(error.message || error);
        }
    }

    const report = {
        generatedAtUtc: currentIso(),
        partial: Boolean(terminationSignal),
        complete: !terminationSignal,
        terminationSignal,
        mainUrl,
        peerUrl,
        durationSeconds: actualDurationSeconds,
        intervalSeconds,
        requestTimeoutMs,
        sampleCount: samples.length,
        samples,
        failures,
        observedRounds,
        divergence: snapshotDivergence(divergence),
        tipMonotonicity: {
            main: summarizeTipMonotonicity(samples, "main"),
            peer: peerUrl ? summarizeTipMonotonicity(samples, "peer") : null
        },
        mainAnalysis,
        peerAnalysis,
        peerAnalysisError,
        verdict: null
    };

    report.verdict = buildVerdict(report);

    console.log(`[g2-monitor] duration=${durationSeconds}s interval=${intervalSeconds}s samples=${samples.length}`);
    console.log(`[main] acceptance=${formatPercent(report.mainAnalysis.summary.localDatumDiagnostics?.acceptedCount ?? 0, report.mainAnalysis.summary.localDatumDiagnostics?.totalSubmissions ?? 0)} rejects=${report.mainAnalysis.rejectCount} freshParents=${report.mainAnalysis.freshParentLearnedCount} coinbaserAvgMs=${report.mainAnalysis.summary.coinbaserDiagnostics?.averageDurationMs?.toFixed?.(2) ?? "--"} shareRespP95Ms=${report.mainAnalysis.datumResponses.p95TotalMs?.toFixed?.(1) ?? "--"}`);
    console.log(`[main] datumSessions=${JSON.stringify({
        count: report.mainAnalysis.datumSessions.count,
        active: report.mainAnalysis.datumSessions.activeCount,
        p95DurationMs: report.mainAnalysis.datumSessions.p95DurationMs,
        shortHandshakeNoWork25sTo40s: report.mainAnalysis.datumSessions.shortHandshakeNoWork25sTo40s,
        overlap: report.mainAnalysis.datumSessions.overlap
    })}`);
    console.log(`[main] datumProtocol=${JSON.stringify({
        count: report.mainAnalysis.datumProtocol.count,
        recvHeaderEof: report.mainAnalysis.datumProtocol.recvHeaderEof,
        clientNoDataCloseAfterRecentSend5s: report.mainAnalysis.datumProtocol.clientNoDataCloseAfterRecentSend5s,
        clientNoDataCloseAfterRecentRecv5s: report.mainAnalysis.datumProtocol.clientNoDataCloseAfterRecentRecv5s,
        p95CloseGapAfterLastSendMs: report.mainAnalysis.datumProtocol.p95CloseGapAfterLastSendMs,
        sendLabels: report.mainAnalysis.datumProtocol.sendLabels
    })}`);
    console.log(`[main] wrongParentChainTipWindow=${JSON.stringify({
        before10s: report.mainAnalysis.lateRejects.wrongParentWithin10sBeforeChainTip,
        after10s: report.mainAnalysis.lateRejects.wrongParentWithin10sAfterChainTip,
        outside10s: report.mainAnalysis.lateRejects.wrongParentOutside10sChainTipWindow
    })}`);
    if (peerAnalysis) {
        console.log(`[peer] acceptance=${formatPercent(report.peerAnalysis.summary.localDatumDiagnostics?.acceptedCount ?? 0, report.peerAnalysis.summary.localDatumDiagnostics?.totalSubmissions ?? 0)} rejects=${report.peerAnalysis.rejectCount} freshParents=${report.peerAnalysis.freshParentLearnedCount} coinbaserAvgMs=${report.peerAnalysis.summary.coinbaserDiagnostics?.averageDurationMs?.toFixed?.(2) ?? "--"} shareRespP95Ms=${report.peerAnalysis.datumResponses.p95TotalMs?.toFixed?.(1) ?? "--"}`);
        console.log(`[peer] datumSessions=${JSON.stringify({
            count: report.peerAnalysis.datumSessions.count,
            active: report.peerAnalysis.datumSessions.activeCount,
            p95DurationMs: report.peerAnalysis.datumSessions.p95DurationMs,
            shortHandshakeNoWork25sTo40s: report.peerAnalysis.datumSessions.shortHandshakeNoWork25sTo40s,
            overlap: report.peerAnalysis.datumSessions.overlap
        })}`);
        console.log(`[peer] datumProtocol=${JSON.stringify({
            count: report.peerAnalysis.datumProtocol.count,
            recvHeaderEof: report.peerAnalysis.datumProtocol.recvHeaderEof,
            clientNoDataCloseAfterRecentSend5s: report.peerAnalysis.datumProtocol.clientNoDataCloseAfterRecentSend5s,
            clientNoDataCloseAfterRecentRecv5s: report.peerAnalysis.datumProtocol.clientNoDataCloseAfterRecentRecv5s,
            p95CloseGapAfterLastSendMs: report.peerAnalysis.datumProtocol.p95CloseGapAfterLastSendMs,
            sendLabels: report.peerAnalysis.datumProtocol.sendLabels
        })}`);
        console.log(`[peer] wrongParentChainTipWindow=${JSON.stringify({
            before10s: report.peerAnalysis.lateRejects.wrongParentWithin10sBeforeChainTip,
            after10s: report.peerAnalysis.lateRejects.wrongParentWithin10sAfterChainTip,
            outside10s: report.peerAnalysis.lateRejects.wrongParentOutside10sChainTipWindow
        })}`);
        console.log(`[divergence] candidateLongestMs=${report.divergence.candidate.longestMs} currentLongestMs=${report.divergence.current.longestMs} tipLongestMs=${report.divergence.tip.longestMs}`);
    }
    console.log(`[tip-monotonicity] ${JSON.stringify({
        mainRegressions: report.tipMonotonicity.main.regressionCount,
        peerRegressions: report.tipMonotonicity.peer?.regressionCount ?? null,
        mainWorst: report.tipMonotonicity.main.worstRegression,
        peerWorst: report.tipMonotonicity.peer?.worstRegression ?? null
    })}`);
    console.log(`[verdict] ${JSON.stringify(report.verdict)}`);

    if (outPath) {
        writeJsonAtomic(outPath, report);
        if (checkpointPath) {
            appendJsonLine(checkpointPath, {
                generatedAtUtc: report.generatedAtUtc,
                final: true,
                partial: report.partial,
                complete: report.complete,
                sampleCount: report.sampleCount,
                verdict: report.verdict
            });
        }
        console.log(`Wrote ${outPath}`);
    }
}

main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
});
