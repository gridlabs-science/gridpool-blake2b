#!/usr/bin/env node
import process from "node:process";

const endpoint = process.argv[2] || "https://main.gridpool.net/api/mining/sv2-work-selection";

function fail(message) {
    console.error(`[sv2-work-selection-smoke][fail] ${message}`);
    process.exit(1);
}

const response = await fetch(endpoint, { headers: { accept: "application/json" } });
if (!response.ok) {
    fail(`${endpoint} returned HTTP ${response.status}`);
}

const body = await response.json();
const required = [
    "networkId",
    "bitcoinNetwork",
    "protocolVersion",
    "activeSnapshotId",
    "coinbaseTxOutputsHex",
    "coinbaseTxOutputsBytes",
    "coinbaseOutputs",
    "minimumDifficultyToEnterReserve",
    "mode"
];

for (const field of required) {
    if (!(field in body)) {
        fail(`missing field ${field}`);
    }
}

if (body.mode !== "coinbase-only") {
    fail(`expected mode coinbase-only, got ${body.mode}`);
}

if (!Array.isArray(body.coinbaseOutputs) || body.coinbaseOutputs.length === 0) {
    fail("coinbaseOutputs is empty");
}

if (typeof body.coinbaseTxOutputsHex !== "string" || !/^[0-9a-f]+$/i.test(body.coinbaseTxOutputsHex)) {
    fail("coinbaseTxOutputsHex is not valid hex");
}

const hexBytes = body.coinbaseTxOutputsHex.length / 2;
if (hexBytes !== body.coinbaseTxOutputsBytes) {
    fail(`coinbaseTxOutputsBytes=${body.coinbaseTxOutputsBytes} but hex has ${hexBytes} bytes`);
}

console.log(`[sv2-work-selection-smoke] ok endpoint=${endpoint}`);
console.log(`[sv2-work-selection-smoke] network=${body.networkId} bitcoin=${body.bitcoinNetwork} protocol=${body.protocolVersion} snapshot=${body.activeSnapshotId}`);
console.log(`[sv2-work-selection-smoke] outputs=${body.coinbaseOutputCount ?? body.coinbaseOutputs.length} bytes=${body.coinbaseTxOutputsBytes} minReserve=${body.minimumDifficultyToEnterReserveDisplay ?? body.minimumDifficultyToEnterReserve}`);
