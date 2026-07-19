#!/usr/bin/env node

const defaultBases = [
  'https://main.gridpool.net',
  'https://test.gridpool.net'
];

const args = process.argv.slice(2);
const bases = args.length > 0 ? args : defaultBases;
let failed = false;

async function fetchJson(url) {
  const response = await fetch(url, { headers: { accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(`${url} returned HTTP ${response.status}`);
  }

  return response.json();
}

function unique(values) {
  return [...new Set(values.filter(value => typeof value === 'string' && value.length > 0))];
}

function summarizeBundle(bundle) {
  const contextIds = new Set((bundle.snapshotContexts ?? []).map(context => context.snapshotId).filter(Boolean));
  const proofSnapshotIds = unique([
    ...(bundle.shareProofs ?? []).map(proof => proof.payoutSnapshotId),
    ...(bundle.workSetProofs ?? []).map(proof => proof.payoutSnapshotId),
    ...(bundle.snapshotFamilyMember?.boundaryReserveProofs ?? []).map(proof => proof.payoutSnapshotId)
  ]);
  const missingContextIds = proofSnapshotIds.filter(id => !contextIds.has(id));

  return {
    kind: bundle.kind ?? 'unknown',
    stateId: bundle.stateId ?? '',
    shareProofs: (bundle.shareProofs ?? []).length,
    workSetProofs: (bundle.workSetProofs ?? []).length,
    boundaryReserveProofs: (bundle.snapshotFamilyMember?.boundaryReserveProofs ?? []).length,
    uniqueProofContexts: proofSnapshotIds.length,
    bundledContexts: contextIds.size,
    missingContexts: missingContextIds
  };
}

async function checkBase(base) {
  const normalizedBase = base.replace(/\/+$/, '');
  const summary = await fetchJson(`${normalizedBase}/api/network/summary`);
  const stateIds = unique([summary.currentStateId, summary.candidateStateId]);
  if (stateIds.length === 0) {
    throw new Error(`${normalizedBase} did not advertise current or candidate state IDs`);
  }

  console.log(`\n${normalizedBase}`);
  console.log(`  network=${summary.networkId ?? 'unknown'} protocol=${summary.protocolVersion ?? 'unknown'} round=${summary.currentRoundNumber ?? 'unknown'}`);

  for (const stateId of stateIds) {
    const bundle = await fetchJson(`${normalizedBase}/api/network/state/${encodeURIComponent(stateId)}`);
    const result = summarizeBundle(bundle);
    const label = result.missingContexts.length === 0 ? 'OK' : 'FAIL';
    console.log(`  ${label} ${result.kind} ${result.stateId.slice(0, 12)} share=${result.shareProofs} work=${result.workSetProofs} boundary=${result.boundaryReserveProofs} proofContexts=${result.uniqueProofContexts} bundledContexts=${result.bundledContexts} missing=${result.missingContexts.length}`);

    if (result.missingContexts.length > 0) {
      failed = true;
      console.log(`    missing: ${result.missingContexts.slice(0, 10).join(', ')}${result.missingContexts.length > 10 ? ', ...' : ''}`);
    }
  }
}

for (const base of bases) {
  try {
    await checkBase(base);
  } catch (error) {
    failed = true;
    console.error(`\n${base}`);
    console.error(`  FAIL ${error instanceof Error ? error.message : String(error)}`);
  }
}

process.exit(failed ? 1 : 0);
