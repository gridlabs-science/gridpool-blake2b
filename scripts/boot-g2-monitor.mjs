#!/usr/bin/env node

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
    console.error("Usage: node scripts/boot-g2-monitor.mjs --main-url <url> [--peer-url <url>] [--duration-seconds 300] [--interval-seconds 5] [--out report.json]");
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

async function fetchAnalysis(baseUrl, durationSeconds) {
    const window = apiWindowForTelemetry(durationSeconds);
    const [summary, rejectsSeries, eventsSeries] = await Promise.all([
        fetchJson(baseUrl, "/api/network/summary"),
        fetchJson(baseUrl, "/api/network/share-diagnostics", {
            window,
            source: "datum",
            accepted: false,
            limit: 5000
        }),
        fetchJson(baseUrl, "/api/network/events", {
            window,
            limit: 5000
        })
    ]);

    const rejects = Array.isArray(rejectsSeries?.events) ? rejectsSeries.events : [];
    const events = Array.isArray(eventsSeries?.events) ? eventsSeries.events : [];
    const eventCounts = countBy(events, event => event.eventType);

    return {
        summary,
        rejectCount: rejects.length,
        rejectReasons: countBy(rejects, reject => reject.rejectionCategory || reject.rejectionReason),
        lateRejects: correlateRejects(rejects, events),
        eventCounts,
        freshParentLearnedCount: eventCounts["fresh-parent-learned"] || 0,
        restartSignalCount: events.filter(event => ["datum-session-reset", "datum-session-close"].includes(event.eventType)).length
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
    verdict.g2_1 = lateRejectFailures.length === 0 ? "pass-ish" : `attention: ${lateRejectFailures.join(", ")}`;

    const longestCandidateDivergenceMs = report.divergence.candidate.longestMs;
    verdict.g2_2 = report.peerUrl
        ? (longestCandidateDivergenceMs <= 15_000 ? "pass-ish" : `attention: candidate divergence ${longestCandidateDivergenceMs} ms`)
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
    const outPath = args.out || null;

    const samples = [];
    const failures = { main: [], peer: [] };
    const divergence = {
        candidate: { active: false, startMs: null, longestMs: 0, intervals: [] },
        current: { active: false, startMs: null, longestMs: 0, intervals: [] },
        tip: { active: false, startMs: null, longestMs: 0, intervals: [] }
    };
    const observedRounds = [];
    const endTime = Date.now() + durationSeconds * 1000;
    let lastMainRound = null;

    while (Date.now() < endTime) {
        const timestampMs = Date.now();
        const sample = { timestampUtc: new Date(timestampMs).toISOString() };

        try {
            sample.main = await fetchJson(mainUrl, "/api/network/summary");
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
                sample.peer = await fetchJson(peerUrl, "/api/network/summary");
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
        await sleep(intervalSeconds * 1000);
    }

    finalizeIntervals(divergence, Date.now());

    const mainAnalysis = await fetchAnalysis(mainUrl, durationSeconds);
    const peerAnalysis = peerUrl ? await fetchAnalysis(peerUrl, durationSeconds) : null;

    const report = {
        generatedAtUtc: currentIso(),
        mainUrl,
        peerUrl,
        durationSeconds,
        intervalSeconds,
        sampleCount: samples.length,
        failures,
        observedRounds,
        divergence: {
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
        },
        mainAnalysis,
        peerAnalysis,
        verdict: null
    };

    report.verdict = buildVerdict(report);

    console.log(`[g2-monitor] duration=${durationSeconds}s interval=${intervalSeconds}s samples=${samples.length}`);
    console.log(`[main] acceptance=${formatPercent(report.mainAnalysis.summary.localDatumDiagnostics?.acceptedCount ?? 0, report.mainAnalysis.summary.localDatumDiagnostics?.totalSubmissions ?? 0)} rejects=${report.mainAnalysis.rejectCount} freshParents=${report.mainAnalysis.freshParentLearnedCount} coinbaserAvgMs=${report.mainAnalysis.summary.coinbaserDiagnostics?.averageDurationMs?.toFixed?.(2) ?? "--"}`);
    console.log(`[main] wrongParentChainTipWindow=${JSON.stringify({
        before10s: report.mainAnalysis.lateRejects.wrongParentWithin10sBeforeChainTip,
        after10s: report.mainAnalysis.lateRejects.wrongParentWithin10sAfterChainTip,
        outside10s: report.mainAnalysis.lateRejects.wrongParentOutside10sChainTipWindow
    })}`);
    if (peerAnalysis) {
        console.log(`[peer] acceptance=${formatPercent(report.peerAnalysis.summary.localDatumDiagnostics?.acceptedCount ?? 0, report.peerAnalysis.summary.localDatumDiagnostics?.totalSubmissions ?? 0)} rejects=${report.peerAnalysis.rejectCount} freshParents=${report.peerAnalysis.freshParentLearnedCount} coinbaserAvgMs=${report.peerAnalysis.summary.coinbaserDiagnostics?.averageDurationMs?.toFixed?.(2) ?? "--"}`);
        console.log(`[peer] wrongParentChainTipWindow=${JSON.stringify({
            before10s: report.peerAnalysis.lateRejects.wrongParentWithin10sBeforeChainTip,
            after10s: report.peerAnalysis.lateRejects.wrongParentWithin10sAfterChainTip,
            outside10s: report.peerAnalysis.lateRejects.wrongParentOutside10sChainTipWindow
        })}`);
        console.log(`[divergence] candidateLongestMs=${report.divergence.candidate.longestMs} currentLongestMs=${report.divergence.current.longestMs} tipLongestMs=${report.divergence.tip.longestMs}`);
    }
    console.log(`[verdict] ${JSON.stringify(report.verdict)}`);

    if (outPath) {
        await import("node:fs/promises").then(fs => fs.writeFile(outPath, JSON.stringify(report, null, 2)));
        console.log(`Wrote ${outPath}`);
    }
}

main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
});
