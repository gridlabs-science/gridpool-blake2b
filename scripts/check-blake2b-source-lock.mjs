#!/usr/bin/env node

import { readFileSync } from "node:fs";

const lockPath = new URL("../config/blake2b-source-lock.json", import.meta.url);
const testnetConfigPath = new URL("../deploy/blake-vps/knots-testnet4.conf", import.meta.url);
const lock = JSON.parse(readFileSync(lockPath, "utf8"));
const testnetConfig = readFileSync(testnetConfigPath, "utf8");

const fail = (message) => {
  throw new Error(`Blake2b source lock: ${message}`);
};
const requireEqual = (actual, expected, label) => {
  if (actual !== expected) {
    fail(`${label} must be ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
};
const requireSha = (value, label, expected = undefined) => {
  if (typeof value !== "string" || !/^[0-9a-f]{40}$/.test(value)) {
    fail(`${label} must be a lowercase 40-character commit hash`);
  }
  if (expected !== undefined) requireEqual(value, expected, label);
};
const requireOptionalDigest = (value, label) => {
  if (value !== null && (typeof value !== "string" || !/^sha256:[0-9a-f]{64}$/.test(value))) {
    fail(`${label} must be null or a sha256:<64 lowercase hex> digest`);
  }
};

requireEqual(lock.schema, "gridpool-blake2b-source-lock-v1", "schema");
if (!Number.isFinite(Date.parse(lock.updated_utc))) fail("updated_utc must be an ISO timestamp");

requireSha(
  lock.gridpool.development_baseline,
  "gridpool.development_baseline",
  "b4c92a9090c11efd74298e06b02cfe56727373ea",
);
requireSha(
  lock.gridpool.required_security_ancestor,
  "gridpool.required_security_ancestor",
  "400fc6e1352ebba72cd557b3c782df52c54d77c8",
);
requireSha(
  lock.gridpool.explicitly_excluded_commit,
  "gridpool.explicitly_excluded_commit",
  "f09ce5e6e2f90cf85c009a586b2d02db792ea4c4",
);

requireEqual(lock.knots.testnet4.tag, "v29.4.1.knots20260508rc3", "knots.testnet4.tag");
requireSha(
  lock.knots.testnet4.peeled_commit,
  "knots.testnet4.peeled_commit",
  "afbe91c299e16519f03902939fdbda8af9bd527d",
);
requireEqual(lock.knots.testnet4.activation_height, 150027, "knots.testnet4.activation_height");
requireEqual(
  lock.knots.testnet4.activation_block_hash,
  "000000000000007a178eb03e6619f0420d7d38e278e6bb5ee16f15ac5b32cee6",
  "knots.testnet4.activation_block_hash",
);
requireEqual(
  lock.knots.testnet4.activation_headline,
  "PyBLOCK-LOTTO-BLAKE2b-t4-ASIC",
  "knots.testnet4.activation_headline",
);
requireEqual(lock.knots.testnet4.activation_header_bytes, 164, "knots.testnet4.activation_header_bytes");
requireEqual(lock.knots.testnet4.pre_activation_header_bytes, 80, "knots.testnet4.pre_activation_header_bytes");
requireEqual(lock.knots.testnet4.network_id, "gridpool-blake2b-testnet4-v1", "knots.testnet4.network_id");
requireEqual(
  lock.knots.testnet4.domain_fingerprint,
  "2ad111b42ae7bd90e41e385d838853455cacc54aefe5f61cbc094c01ee6908d0",
  "knots.testnet4.domain_fingerprint",
);
requireEqual(lock.knots.testnet4.first_blake_target_compact, "1a00ffff", "knots.testnet4.first_blake_target_compact");
if (!testnetConfig.includes(`blake2b_headline=${lock.knots.testnet4.activation_headline}\n`)) {
  fail("deploy/blake-vps/knots-testnet4.conf must pin the locked activation headline");
}
if (!/\[testnet4\][\s\S]*addnode=seed\.testnet-bitcoin\.haf\.ovh:48333/.test(testnetConfig)) {
  fail("the Blake-capable Testnet4 seed must be scoped to [testnet4]");
}

requireEqual(lock.knots.mainnet.tag, "v29.4.1.knots20260508rc4", "knots.mainnet.tag");
requireSha(lock.knots.mainnet.peeled_commit, "knots.mainnet.peeled_commit", "dc82be77dd741dfa63e1f816367b15364d55b051");
requireEqual(lock.knots.mainnet.activation_height, 961640, "knots.mainnet.activation_height");
requireEqual(lock.knots.mainnet.activation_block_hash, "0000000000000050c1e5f69672f459293be14f46e5a494e7a8c8541396f18eeb", "knots.mainnet.activation_block_hash");
requireEqual(lock.knots.mainnet.activation_parent_block_hash, "00000000000000000001bbc439e13f749dca850d32c7a2834165338713027e65", "knots.mainnet.activation_parent_block_hash");
requireEqual(lock.knots.mainnet.activation_headline, "8-30 NYPost Deride And Conquer", "knots.mainnet.activation_headline");
requireEqual(lock.knots.mainnet.activation_coinbase_scriptsig, "0368ac0e2a53696c656e74576176650f382d3330204e59506f73742044657269646520416e6420436f6e717565720003ff92100eb12e000000000000000000000000", "knots.mainnet.activation_coinbase_scriptsig");
requireEqual(lock.knots.mainnet.activation_header_bytes, 164, "knots.mainnet.activation_header_bytes");
requireEqual(lock.knots.mainnet.pre_activation_header_bytes, 80, "knots.mainnet.pre_activation_header_bytes");
requireEqual(lock.knots.mainnet.network_id, "gridpool-blake2b-mainnet-v1", "knots.mainnet.network_id");
requireEqual(lock.knots.mainnet.domain_fingerprint, "8d19554cd57c217c6fb0680e506cd9356eb60e6dfd7c050385477f07895aef2c", "knots.mainnet.domain_fingerprint");
requireEqual(lock.knots.mainnet.rdts_expiry_unix, 1819756800, "knots.mainnet.rdts_expiry_unix");
requireEqual(lock.knots.mainnet.first_blake_target_compact, "1a008d4f", "knots.mainnet.first_blake_target_compact");
requireEqual(lock.knots.mainnet.target_shift, 22, "knots.mainnet.target_shift");
requireEqual(lock.knots.mainnet.profile_revision, "knots-rc4-dc82be77-activated-v1", "knots.mainnet.profile_revision");
if (typeof lock.knots.mainnet.domain_fingerprint !== "string" || !/^[0-9a-f]{64}$/.test(lock.knots.mainnet.domain_fingerprint)) {
  fail("knots.mainnet.domain_fingerprint must be a lowercase 64-character hash");
}

requireSha(
  lock.datum.experimental_base,
  "datum.experimental_base",
  "2fea7e51286d3821c19dc1c240b8caa92bd92532",
);
requireSha(
  lock.datum.superseded_base,
  "datum.superseded_base",
  "e894b8ac29ae06bf6e3b14dafd21f72dcd65fb84",
);
requireEqual(lock.datum.upstream_repository, "https://github.com/innerhat-dev/datum_gateway", "datum.upstream_repository");
requireEqual(lock.datum.fork_repository, "https://github.com/gridlabs-science/datum-gateway-blake2b-gridpool", "datum.fork_repository");
requireEqual(lock.datum.default_branch, "develop", "datum.default_branch");
requireSha(lock.datum.fork_head, "datum.fork_head", "23476bc420451214eb54856498d25b3a6bc3a8fc");
requireSha(lock.datum.gridpool_coinbase_implementation, "datum.gridpool_coinbase_implementation", "70670c5438ad176c7d8194fba7169944aeffb453");
requireSha(lock.datum.unknown_firmware_implementation, "datum.unknown_firmware_implementation", "df502b3e2d2e4ed30c39a0aeecbe075f338a0360");

requireOptionalDigest(lock.artifacts.knots_testnet4_binary_sha256, "artifacts.knots_testnet4_binary_sha256");
requireOptionalDigest(lock.artifacts.datum_testnet4_binary_sha256, "artifacts.datum_testnet4_binary_sha256");
requireOptionalDigest(lock.artifacts.gridpool_image_digest, "artifacts.gridpool_image_digest");
requireOptionalDigest(lock.artifacts.datum_image_digest, "artifacts.datum_image_digest");

console.log("Blake2b source lock checks passed.");
