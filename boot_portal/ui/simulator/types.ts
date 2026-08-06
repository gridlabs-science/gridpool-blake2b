export interface SimulatorState {
  schemaVersion: number;
  seed: number;
  sequence: number;
  virtualTimeUtc: string;
  playing: boolean;
  speed: number;
  loopTimeline: boolean;
  advancedOverrides: boolean;
  scenario: string;
  node: {
    displayName: string;
    region: string;
    networkId: string;
    bitcoinNetwork: string;
    releaseVersion: string;
    consensusVersion: number;
    protocolVersion: number;
    ready: boolean;
    miningSafe: boolean;
    safetyReason: string;
    rpcReachable: boolean;
    rpcSynced: boolean;
    initialBlockDownload: boolean;
    zmqHealthy: boolean;
    peerLoopsHealthy: boolean;
    outboundRelayHealthy: boolean;
    versionCompatible: boolean;
  };
  chain: {
    height: number;
    tipHash: string;
    provisionalTipHash: string;
    round: number;
    currentStateId: string;
    candidateStateId: string;
    activeSnapshotId: string;
    snapshotFamilyId: string;
    lastRotationUtc: string | null;
    familyMembers: number;
    familyUnionProofs: number;
    siblingAdmissions: number;
    unionAdditions: number;
    convergences: number;
    paidProofRemovals: number;
    reorganizations: number;
  };
  work: {
    poolHashrateThs: number;
    observationCount: number;
    reserveLimit: number;
    admissionFloorDifficulty: number;
    window: string;
  };
  pulse: {
    enabled: boolean;
    targetIntervalSeconds: number;
    relayTtl: number;
    accepted: number;
    rejected: number;
    lastAcceptedUtc: string | null;
    lastRelayUtc: string | null;
    secondsUntilNext: number;
  };
  faults: {
    apiLatencyMs: number;
    apiFailure: boolean;
    signalRDrop: boolean;
  };
  peers: PeerControl[];
  adapters: AdapterControl[];
  slotZeroAddress: string;
  slotZeroObservedUtc: string | null;
  reserve: Array<{ id: string; address: string; difficulty: number; firstSeenUtc: string }>;
  lockedPayouts: Array<{ proofId: string; address: string; position: number; valueSats: number }>;
  events: Array<{
    sequence: number;
    timestampUtc: string;
    action: string;
    summary: string;
    arguments: Record<string, string>;
  }>;
  timeline: unknown;
  timelineCursor: number;
  timelineElapsedSeconds: number;
}

export interface PeerControl {
  id: string;
  endpoint: string;
  connected: boolean;
  http: boolean;
  webSocket: boolean;
  udp: boolean;
  latencyMs: number;
  currentStateId: string;
  candidateStateId: string;
  compatible: boolean;
}

export interface AdapterControl {
  id: string;
  kind: string;
  displayName: string;
  connected: boolean;
  clientCount: number;
  hashrateThs: number;
  acceptedShares: number;
  lastShareUtc: string | null;
  miners: MinerControl[];
}

export interface MinerControl {
  id: string;
  username: string;
  address: string;
  hashrateThs: number;
  acceptedShares: number;
  lastShareUtc: string | null;
}

export interface Scenario {
  id: string;
  name: string;
  description: string;
}

export interface SimulatorAction {
  action: string;
  peer?: string;
  adapter?: string;
  miner?: string;
  address?: string;
  transport?: string;
  value?: number;
  count?: number;
  rank?: number;
}
