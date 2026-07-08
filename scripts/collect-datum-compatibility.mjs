#!/usr/bin/env node

import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

const DEFAULT_OUT = path.join(os.homedir(), ".local", "state", "gridpool-compatibility", "compatibility_status.json");

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
    console.log(`Usage: node scripts/collect-datum-compatibility.mjs [options]

Options:
  --datum-url <url>       DATUM API clients JSON URL (default: http://127.0.0.1:7152/clients.json)
  --user <name>           Digest auth username (default: gridpool)
  --password-env <name>   Env var containing DATUM API password (default: DATUM_API_PASSWORD)
  --out <file>            Sanitized public telemetry output JSON
  --raw-log <file>        Optional local-only raw JSONL append log
  --salt-env <name>       Env var for IP hash salt (default: GRIDPOOL_COMPAT_SALT)
  --help                  Show this help
`);
}

function expandHome(value) {
    if (!value) return value;
    if (value === "~") return os.homedir();
    if (value.startsWith("~/")) return path.join(os.homedir(), value.slice(2));
    return value;
}

function ensureParent(filePath) {
    fs.mkdirSync(path.dirname(filePath), { recursive: true, mode: 0o700 });
}

function hashRemoteHost(remoteHost, salt) {
    if (!remoteHost) return "";
    return crypto.createHash("sha256").update(`${salt}:${remoteHost}`).digest("hex").slice(0, 16);
}

function parseTag(username) {
    const clean = String(username || "").trim();
    if (!clean || clean === "NULL") {
        return { testerTag: "unknown", workerName: "" };
    }

    const dot = clean.indexOf(".");
    if (dot <= 0) {
        return { testerTag: clean.slice(0, 64), workerName: "" };
    }

    return {
        testerTag: clean.slice(0, dot).slice(0, 64),
        workerName: clean.slice(dot + 1).slice(0, 96)
    };
}

function classifyClient(client) {
    if (client.waitingForUnsafeOverride) return "blocked-awaiting-unsafe-override";
    if (!client.authorized) return "connected-not-authorized";
    if (!client.subscribed) return "authorized-not-subscribed";
    if ((client.acceptedShareCount ?? 0) > 0) return "submitting-shares";
    return "subscribed";
}

function fetchDatumJson(datumUrl, user, password) {
    const result = spawnSync("curl", [
        "-fsS",
        "--digest",
        "-u",
        `${user}:${password}`,
        datumUrl
    ], {
        encoding: "utf8",
        maxBuffer: 10 * 1024 * 1024
    });

    if (result.status !== 0) {
        throw new Error(`curl failed (${result.status}): ${result.stderr || result.stdout}`);
    }

    return JSON.parse(result.stdout);
}

function main() {
    const args = parseArgs(process.argv.slice(2));
    if (args.help) {
        usage();
        return;
    }

    const datumUrl = args["datum-url"] || "http://127.0.0.1:7152/clients.json";
    const user = args.user || "gridpool";
    const passwordEnv = args["password-env"] || "DATUM_API_PASSWORD";
    const password = process.env[passwordEnv];
    if (!password) {
        throw new Error(`Missing DATUM API password env var: ${passwordEnv}`);
    }

    const saltEnv = args["salt-env"] || "GRIDPOOL_COMPAT_SALT";
    const salt = process.env[saltEnv] || os.hostname();
    const outPath = expandHome(args.out || DEFAULT_OUT);
    const rawLogPath = args["raw-log"] ? expandHome(args["raw-log"]) : "";

    const raw = fetchDatumJson(datumUrl, user, password);
    const nowUtc = new Date().toISOString();
    const clients = Array.isArray(raw.clients) ? raw.clients : [];

    const sanitizedClients = clients.map((client) => {
        const tag = parseTag(client.username);
        return {
            ...tag,
            status: classifyClient(client),
            subscribed: Boolean(client.subscribed),
            authorized: Boolean(client.authorized),
            unsafeFullCoinbaseOverride: Boolean(client.unsafeFullCoinbaseOverride),
            waitingForUnsafeOverride: Boolean(client.waitingForUnsafeOverride),
            coinbaseClass: client.coinbaseClass || "",
            coinbaseClassId: client.coinbaseClassId ?? null,
            acceptedShareCount: client.acceptedShareCount ?? 0,
            rejectedShareCount: client.rejectedShareCount ?? 0,
            hashrateThs: client.hashrateThs ?? null,
            lastShareAgeSeconds: client.lastShareAgeSeconds ?? null,
            userAgent: client.userAgent || "",
            remoteHostHash: hashRemoteHost(client.remoteHost, salt)
        };
    });

    const summary = {
        schemaVersion: 1,
        generatedUtc: nowUtc,
        source: {
            datumUrl,
            rawTimestampMs: raw.timestampMs ?? null
        },
        totals: {
            connected: sanitizedClients.length,
            unsafeOverride: sanitizedClients.filter((client) => client.unsafeFullCoinbaseOverride).length,
            waitingForUnsafeOverride: sanitizedClients.filter((client) => client.waitingForUnsafeOverride).length,
            submittingShares: sanitizedClients.filter((client) => client.status === "submitting-shares").length
        },
        clients: sanitizedClients
    };

    ensureParent(outPath);
    fs.writeFileSync(outPath, `${JSON.stringify(summary, null, 2)}\n`, { mode: 0o644 });

    if (rawLogPath) {
        ensureParent(rawLogPath);
        fs.appendFileSync(rawLogPath, `${JSON.stringify({ generatedUtc: nowUtc, raw })}\n`, { mode: 0o600 });
    }

    console.log(JSON.stringify({
        status: "ok",
        out: outPath,
        connected: summary.totals.connected,
        unsafeOverride: summary.totals.unsafeOverride,
        waitingForUnsafeOverride: summary.totals.waitingForUnsafeOverride
    }));
}

try {
    main();
} catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
}
