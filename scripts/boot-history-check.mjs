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
    console.error("Usage: node scripts/boot-history-check.mjs --main-url <url> [--peer-url <url>] [--limit 200] [--out report.json]");
    process.exit(1);
}

function normalizeBaseUrl(url) {
    return url.replace(/\/+$/, "");
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

function normalizeRecipients(recipients) {
    return [...(recipients || [])]
        .map(item => ({
            address: item.address || "",
            username: item.username || "",
            slotCount: item.slotCount || 0,
            totalValue: item.totalValue || 0
        }))
        .sort((a, b) =>
            a.address.localeCompare(b.address) ||
            a.username.localeCompare(b.username) ||
            a.slotCount - b.slotCount ||
            a.totalValue - b.totalValue);
}

function recipientSetsEqual(left, right) {
    const a = normalizeRecipients(left);
    const b = normalizeRecipients(right);
    return JSON.stringify(a) === JSON.stringify(b);
}

function canonicalHistory(history) {
    return [...(history || [])]
        .filter(entry => entry.isCanonical && !entry.isOrphaned)
        .sort((a, b) => a.roundNumber - b.roundNumber);
}

function analyzeHistory(history) {
    const canonical = canonicalHistory(history);
    const issues = [];

    for (let i = 0; i < canonical.length - 1; i += 1) {
        const current = canonical[i];
        const next = canonical[i + 1];
        if (!recipientSetsEqual(current.nextRecipients, next.paidRecipients)) {
            issues.push({
                type: "next-vs-paid-mismatch",
                roundNumber: current.roundNumber,
                nextRoundNumber: next.roundNumber,
                currentStateId: current.stateId,
                nextStateId: next.stateId
            });
        }
    }

    const roundCounts = new Map();
    for (const entry of canonical) {
        roundCounts.set(entry.roundNumber, (roundCounts.get(entry.roundNumber) || 0) + 1);
    }
    for (const [roundNumber, count] of roundCounts.entries()) {
        if (count > 1) {
            issues.push({ type: "duplicate-canonical-round-number", roundNumber, count });
        }
    }

    const orphanStateIds = new Set(
        (history || [])
            .filter(entry => entry.isOrphaned)
            .map(entry => entry.stateId)
    );
    for (const entry of canonical) {
        if (orphanStateIds.has(entry.stateId)) {
            issues.push({ type: "canonical-state-also-marked-orphaned", stateId: entry.stateId, roundNumber: entry.roundNumber });
        }
    }

    return {
        canonicalCount: canonical.length,
        orphanCount: (history || []).filter(entry => entry.isOrphaned).length,
        issues
    };
}

async function fetchNode(baseUrl, limit) {
    const [summary, history] = await Promise.all([
        fetchJson(baseUrl, "/api/network/summary"),
        fetchJson(baseUrl, "/api/network/history", { limit })
    ]);

    return {
        baseUrl,
        summary,
        history,
        analysis: analyzeHistory(history)
    };
}

async function main() {
    const args = parseArgs(process.argv.slice(2));
    if (!args["main-url"]) usage();

    const mainUrl = args["main-url"];
    const peerUrl = args["peer-url"] || null;
    const limit = Number(args.limit || 200);
    const outPath = args.out || null;

    const mainNode = await fetchNode(mainUrl, limit);
    const peerNode = peerUrl ? await fetchNode(peerUrl, limit) : null;

    const comparison = peerNode ? {
        currentRoundMatches: mainNode.summary.currentRoundNumber === peerNode.summary.currentRoundNumber,
        currentStateMatches: mainNode.summary.currentStateId === peerNode.summary.currentStateId,
        candidateStateMatches: mainNode.summary.candidateStateId === peerNode.summary.candidateStateId,
        tipMatches: mainNode.summary.currentTipBlockHeight === peerNode.summary.currentTipBlockHeight &&
            mainNode.summary.currentTipBlockHash === peerNode.summary.currentTipBlockHash
    } : null;

    const report = {
        generatedAtUtc: new Date().toISOString(),
        limit,
        main: mainNode,
        peer: peerNode,
        comparison
    };

    console.log(`[history-check] main canonical=${mainNode.analysis.canonicalCount} orphaned=${mainNode.analysis.orphanCount} issues=${mainNode.analysis.issues.length}`);
    if (mainNode.analysis.issues.length > 0) {
        console.log(JSON.stringify(mainNode.analysis.issues, null, 2));
    }
    if (peerNode) {
        console.log(`[history-check] peer canonical=${peerNode.analysis.canonicalCount} orphaned=${peerNode.analysis.orphanCount} issues=${peerNode.analysis.issues.length}`);
        if (peerNode.analysis.issues.length > 0) {
            console.log(JSON.stringify(peerNode.analysis.issues, null, 2));
        }
        console.log(`[comparison] ${JSON.stringify(comparison)}`);
    }

    if (outPath) {
        await import("node:fs/promises").then(fs => fs.writeFile(outPath, JSON.stringify(report, null, 2)));
        console.log(`Wrote ${outPath}`);
    }

    if (mainNode.analysis.issues.length > 0 || (peerNode && peerNode.analysis.issues.length > 0)) {
        process.exitCode = 1;
    }
}

main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
});
