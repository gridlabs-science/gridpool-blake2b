#!/usr/bin/env node

import { readFileSync } from "node:fs";

const lockPath = new URL("../config/blake2b-source-lock.json", import.meta.url);
const lock = JSON.parse(readFileSync(lockPath, "utf8"));

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
requireEqual(lock.knots.testnet4.first_blake_target_compact, "1a00ffff", "knots.testnet4.first_blake_target_compact");

for (const field of ["tag", "peeled_commit", "activation_height", "profile_revision"]) {
  requireEqual(lock.knots.mainnet[field], null, `knots.mainnet.${field}`);
}

requireSha(
  lock.datum.experimental_base,
  "datum.experimental_base",
  "e894b8ac29ae06bf6e3b14dafd21f72dcd65fb84",
);

requireOptionalDigest(lock.artifacts.knots_testnet4_binary_sha256, "artifacts.knots_testnet4_binary_sha256");
requireOptionalDigest(lock.artifacts.gridpool_image_digest, "artifacts.gridpool_image_digest");
requireOptionalDigest(lock.artifacts.datum_image_digest, "artifacts.datum_image_digest");

console.log("Blake2b source lock checks passed.");
