export type WindowKey = "6h" | "24h" | "7d";
export type HealthState = "ready" | "degraded" | "unsafe" | "unknown";

export interface DashboardSummary {
  schemaVersion: number;
  revision: number;
  generatedAtUtc: string;
  node: {
    nodeId: string;
    displayName: string;
    region: string;
    role: string;
    publicEndpoint: string;
    networkId: string;
    bitcoinNetwork: string;
    releaseVersion: string;
    consensusVersion: number;
    protocolVersion: number;
    httpApiVersion: number;
    serviceStartedUtc: string;
  };
  health: {
    status: HealthState;
    miningWorkSafe: boolean;
    miningWorkSafetyReason: string;
    peerCount: number;
    peerLoopsHealthy: boolean;
    outboundRelayHealthy: boolean;
    bitcoinNotificationMode: string;
    bitcoinAuthorityClass: string;
    bitcoinRpcReachable: boolean;
    bitcoinRpcSynced: boolean;
    bitcoinInitialBlockDownload: boolean;
    currentTipBlockHash: string;
    currentTipBlockHeight: number | null;
    provisionalTipBlockHash: string;
    lastPeerPollCompletedUtc: string | null;
  };
  snapshot: {
    roundNumber: number;
    currentStateId: string;
    candidateStateId: string;
    activeSnapshotId: string;
    activeSnapshotFamilyId: string;
    lockedPayoutCount: number;
    lockedProofCount: number;
    reserveCount: number;
    reserveLimit: number;
    reserveFloorDifficulty: number | null;
    reserveFloorDifficultyDisplay: string;
    lastRotationUtc: string | null;
    familyMemberCount: number;
    familyUnionProofCount: number;
    reconciliation: Record<string, number>;
  };
  workRate: WorkRate;
  pulse: {
    enabled: boolean;
    acceptedTotal: number;
    acceptedInWindow: number;
    acceptedPerMinute: number;
    lastAcceptedUtc: string | null;
    lastSuccessfulOutboundRelayUtc: string | null;
    outboundRelayHealthy: boolean;
    targetIntervalSeconds: number;
    relayTtl: number;
    interpretation: string;
  };
  capabilities: {
    webUiEnabled: boolean;
    legacyUiEnabled: boolean;
    operatorApiAvailable: boolean;
    addressLookupAvailable: boolean;
    workRateTelemetryAvailable: boolean;
    pulseTelemetryAvailable: boolean;
    watchtowerAvailable: boolean;
    modules: string[];
  };
}

export interface WorkRate {
  window: WindowKey;
  windowSeconds: number;
  windowStartUtc: string;
  windowEndUtc: string;
  observationCount: number;
  retainedOrderStatisticCount: number;
  estimateThs: number | null;
  estimateDisplay: string;
  orderStatisticDifficulty: number | null;
  orderStatisticDifficultyDisplay: string;
  effectiveAdmissionFloorDifficulty: number;
  effectiveAdmissionFloorDisplay: string;
  relativeStandardErrorPercent: number | null;
  confidence: "collecting" | "low" | "medium" | "high";
  warmup: boolean;
  completeWindow: boolean;
  method: string;
  note: string;
}

export interface DashboardHistory {
  schemaVersion: number;
  window: WindowKey;
  windowSeconds: number;
  generatedAtUtc: string;
  points: Array<{
    timestampUtc: string;
    workRateThs: number | null;
    workObservationCount: number;
    relativeStandardErrorPercent: number | null;
    pulseCount: number;
  }>;
}

export interface DashboardAddress {
  schemaVersion: number;
  address: string;
  found: boolean;
  lockedSlotCount: number;
  lockedValueSats: number;
  lockedPositions: number[];
  provisionalPositionCount: number;
  provisionalPositions: number[];
  bestProvisionalDifficulty: number | null;
  bestProvisionalDifficultyDisplay: string;
  reserveFloorDifficulty: number | null;
  reserveFloorDifficultyDisplay: string;
  estimatedTop300SurvivalProbability: number | null;
  interpretation: string;
}

export interface LocalMiningSource {
  source: string;
  displayName: string;
  activeMinerCount: number;
  recentAcceptedShareCount: number;
  hashrateSampleCount: number;
  currentHashrateThs: number | null;
  currentHashrateDisplay: string;
  estimationMethod: string;
  lastShareUtc: string | null;
}

export interface PeerStatus {
  endpoint: string;
  nodeId: string;
  status: string;
  transport?: string;
  lastSuccessUtc?: string | null;
  latencyMs?: number | null;
}

export interface DashboardOperator {
  schemaVersion: number;
  generatedAtUtc: string;
  localMiningSources: LocalMiningSource[];
  localMiners: unknown[];
  peers: PeerStatus[];
  bitcoinNotification: Record<string, unknown>;
  datumDiagnostics: Record<string, unknown>;
  coinbaserDiagnostics: Record<string, unknown>;
  peerLoopFaults: Record<string, unknown>;
}

export interface DashboardChanged {
  revision: number;
  timestampUtc: string;
  topics: string[];
}

export interface DashboardState {
  summary: DashboardSummary | null;
  history: DashboardHistory | null;
  operator: DashboardOperator | null;
  loading: boolean;
  stale: boolean;
  error: string;
  lastUpdatedUtc: string | null;
}
