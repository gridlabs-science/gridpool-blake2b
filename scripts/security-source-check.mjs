#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const trackedFiles = execFileSync("git", ["ls-files", "-z"], {
    cwd: repoRoot,
    encoding: "utf8"
}).split("\0").filter(Boolean);

const failures = [];
const logCall = /(?:Console\.Write(?:Line)?|_?logger\.Log(?:Trace|Debug|Information|Warning|Error|Critical))\s*\(/;
const forbiddenLogMaterial = /(?:Export\s*\(\s*KeyBlobFormat\.RawPrivateKey|ed25519PrivKeyBytes|x25519PrivKeyBytes|testSharedKeyBytes|_channelSharedSecretBytes|_sessionNonceSender|_sessionNonceReceiver|_sendingHeaderKey|_receivingHeaderKey)/;

function stripComments(source) {
    return source
        .replace(/\/\*[\s\S]*?\*\//g, "")
        .replace(/^\s*\/\/.*$/gm, "");
}

for (const relativePath of trackedFiles.filter(file => file.endsWith(".cs"))) {
    const source = stripComments(fs.readFileSync(path.join(repoRoot, relativePath), "utf8"));
    source.split(/\r?\n/).forEach((line, index) => {
        if (logCall.test(line) && forbiddenLogMaterial.test(line)) {
            failures.push(`${relativePath}:${index + 1}: possible secret key material in logs`);
        }
    });
}

const forbiddenConfigKeys = new Set([
    "ed25519_private_key",
    "x25519_private_key",
    "admin_api_key"
]);

function isExplicitPlaceholder(value) {
    return /(?:change[-_ ]?this|replace[-_ ]?this|example|placeholder|your[-_ ]|set[-_ ]?me)/i.test(value);
}

function inspectJson(value, relativePath, jsonPath = "$") {
    if (Array.isArray(value)) {
        value.forEach((item, index) => inspectJson(item, relativePath, `${jsonPath}[${index}]`));
        return;
    }
    if (!value || typeof value !== "object") return;
    for (const [key, child] of Object.entries(value)) {
        const childPath = `${jsonPath}.${key}`;
        if (forbiddenConfigKeys.has(key) && typeof child === "string" && child.trim() && !isExplicitPlaceholder(child)) {
            failures.push(`${relativePath}:${childPath}: tracked secret field must be empty`);
        }
        inspectJson(child, relativePath, childPath);
    }
}

for (const relativePath of trackedFiles.filter(file => file.endsWith(".json"))) {
    try {
        inspectJson(JSON.parse(fs.readFileSync(path.join(repoRoot, relativePath), "utf8")), relativePath);
    } catch {
        // JSON-with-comments and generated third-party JSON are outside this narrow guard.
    }
}

if (failures.length) {
    console.error("Security source check failed:\n" + failures.map(item => `- ${item}`).join("\n"));
    process.exit(1);
}

console.log(`Security source check passed (${trackedFiles.length} tracked files scanned).`);
