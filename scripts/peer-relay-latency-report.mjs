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
    console.error("Usage: node scripts/peer-relay-latency-report.mjs --url <base-url> [--window 12h] [--limit 500] [--transport udp] [--remote-endpoint <url>]");
    process.exit(1);
}

function normalizeBaseUrl(url) {
    return url.replace(/\/+$/, "");
}

function formatMs(value) {
    return Number.isFinite(value) ? `${value.toFixed(2)} ms` : "--";
}

function formatBytes(value) {
    return Number.isFinite(value) ? `${Math.round(value)} B` : "--";
}

async function fetchJson(baseUrl, path, query) {
    const url = new URL(`${normalizeBaseUrl(baseUrl)}${path}`);
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

function printReport(payload) {
    console.log(`Peer relay latency: ${payload.totalEvents ?? 0} event(s), window=${payload.windowSeconds ?? 0}s`);
    const transports = Array.isArray(payload.transports) ? payload.transports : [];
    if (transports.length === 0) {
        console.log("  No peer relay observations found.");
        return;
    }

    for (const transport of transports) {
        console.log(`  ${transport.transport || "unknown"}: arrivals=${transport.arrivalCount ?? 0} first=${transport.firstArrivalCount ?? 0} accepted=${transport.acceptedCount ?? 0} duplicates=${transport.duplicateCount ?? 0}`);
        console.log(`    delta avg/median/p95: ${formatMs(transport.averageDeltaFromFirstMs)} / ${formatMs(transport.medianDeltaFromFirstMs)} / ${formatMs(transport.p95DeltaFromFirstMs)}`);
        console.log(`    payload avg/min/max: ${formatBytes(transport.averagePayloadBytes)} / ${formatBytes(transport.minPayloadBytes)} / ${formatBytes(transport.maxPayloadBytes)}`);
    }
}

const args = parseArgs(process.argv.slice(2));
if (!args.url) {
    usage();
}

const payload = await fetchJson(args.url, "/api/network/peer-relay-latency", {
    window: args.window || "12h",
    limit: args.limit || 500,
    transport: args.transport,
    remoteEndpoint: args["remote-endpoint"]
});

printReport(payload);
