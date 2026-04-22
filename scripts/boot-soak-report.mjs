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

async function fetchNodeReport(baseUrl, window, limit) {
    const [summary, rejects, events] = await Promise.all([
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
        })
    ]);

    const rejectEvents = Array.isArray(rejects?.events) ? rejects.events : [];
    const eventItems = Array.isArray(events?.events) ? events.events : [];
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
