import { startTransition, useEffect, useEffectEvent, useState } from "react";
import type {
  AdapterControl,
  PeerControl,
  Scenario,
  SimulatorAction,
  SimulatorState
} from "./types";

const exampleTimeline = `version: 1
name: peer-tip-recovery
seed: 42
initialScenario: healthy-mesh
events:
  - at: 5s
    action: peer.disconnect
    peer: dallas
  - at: 8s
    action: chain.peer-header
  - at: 10s
    action: chain.local-validate
  - at: 14s
    action: peer.reconnect
    peer: dallas
`;

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/__sim/api/v1${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers
    },
    cache: "no-store"
  });
  if (!response.ok) {
    const payload = await response.json().catch(() => null) as { reason?: string } | null;
    throw new Error(payload?.reason ?? `${response.status} ${response.statusText}`);
  }
  return await response.json() as T;
}

export default function SimulatorLab() {
  const [state, setState] = useState<SimulatorState | null>(null);
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [timeline, setTimeline] = useState(exampleTimeline);
  const [proofAddress, setProofAddress] = useState(
    "tb1qexampleminer000000000000000000000000"
  );
  const [testPeer, setTestPeer] = useState("");
  const [testMiner, setTestMiner] = useState("");
  const [proofRank, setProofRank] = useState(620);
  const [previewWidth, setPreviewWidth] = useState(62);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = useEffectEvent(async () => {
    try {
      const next = await api<SimulatorState>("/state");
      startTransition(() => setState(next));
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Simulator unavailable.");
    }
  });

  useEffect(() => {
    void Promise.all([
      refresh(),
      api<Scenario[]>("/scenarios").then(setScenarios)
    ]);
    const poll = window.setInterval(() => void refresh(), 1_000);
    return () => window.clearInterval(poll);
  }, []);

  const replace = async (next: SimulatorState) => {
    setState(next);
    try {
      await api<SimulatorState>("/state", {
        method: "PUT",
        body: JSON.stringify(next)
      });
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "State update failed.");
    }
  };

  const mutate = (change: (draft: SimulatorState) => void) => {
    if (!state) return;
    const draft = structuredClone(state);
    change(draft);
    void replace(draft);
  };

  const action = async (value: SimulatorAction) => {
    setBusy(true);
    try {
      setState(await api<SimulatorState>("/actions", {
        method: "POST",
        body: JSON.stringify(value)
      }));
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Action failed.");
    } finally {
      setBusy(false);
    }
  };

  const post = async (path: string) => {
    setBusy(true);
    try {
      setState(await api<SimulatorState>(path, { method: "POST" }));
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Operation failed.");
    } finally {
      setBusy(false);
    }
  };

  const importTimeline = async () => {
    setBusy(true);
    try {
      const response = await fetch("/__sim/api/v1/import", {
        method: "POST",
        headers: { "Content-Type": "application/yaml", Accept: "application/json" },
        body: timeline
      });
      if (!response.ok) {
        const payload = await response.json() as { reason?: string };
        throw new Error(payload.reason ?? "Timeline import failed.");
      }
      setState(await response.json() as SimulatorState);
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Timeline import failed.");
    } finally {
      setBusy(false);
    }
  };

  if (!state) {
    return <main className="lab-loading">Starting synthetic GridPool node…</main>;
  }

  const connectedPeers = state.peers.filter((peer) => peer.connected).length;
  const localHashrate = state.adapters
    .filter((adapter) => adapter.connected)
    .reduce((total, adapter) => total + adapter.hashrateThs, 0);
  const miners = state.adapters.flatMap((adapter) => adapter.miners);
  const selectedPeer = state.peers.find((peer) => peer.id === testPeer) ?? state.peers[0];
  const selectedMiner = miners.find((miner) => miner.id === testMiner) ?? miners[0];
  const selectedBitcoinPeer = state.bitcoinPeers[0];

  return (
    <main className="lab">
      <header className="lab-header">
        <div>
          <p className="kicker">Development-only synthetic node</p>
          <h1>Dashboard state laboratory</h1>
        </div>
        <div className="lab-readout">
          <span>{state.scenario}</span>
          <span>{connectedPeers} peers</span>
          <span>{formatHashrate(localHashrate)} local</span>
          <span>{new Date(state.virtualTimeUtc).toLocaleTimeString()}</span>
        </div>
      </header>

      {error ? <div className="lab-error" role="alert">{error}</div> : null}

      <div
        className="lab-layout"
        style={{ gridTemplateColumns: `${100 - previewWidth}% ${previewWidth}%` }}
      >
        <aside className="controls">
          <section className="control-section scenario-section">
            <div className="section-heading">
              <div>
                <p className="kicker">State presets</p>
                <h2>Scenarios</h2>
              </div>
              <button type="button" className="ghost" onClick={() => post("/reset")}>Reset</button>
            </div>
            <select
              aria-label="Scenario"
              value={state.scenario}
              onChange={(event) => void post(`/scenarios/${event.target.value}/load`)}
            >
              {scenarios.map((scenario) => (
                <option value={scenario.id} key={scenario.id}>{scenario.name}</option>
              ))}
            </select>
            <p className="description">
              {scenarios.find((scenario) => scenario.id === state.scenario)?.description}
            </p>
          </section>

          <section className="control-section">
            <p className="kicker">Virtual clock</p>
            <h2>Playback</h2>
            <div className="button-row">
              <button type="button" onClick={() => post(state.playing ? "/timeline/pause" : "/timeline/play")}>
                {state.playing ? "Pause" : "Play"}
              </button>
              <button type="button" className="ghost" onClick={() => post("/timeline/step")}>Step</button>
              <button type="button" className="ghost" onClick={() => post("/timeline/reset")}>Rewind</button>
            </div>
            <ControlRange
              label="Speed"
              value={state.speed}
              min={0.25}
              max={20}
              step={0.25}
              suffix="×"
              onChange={(value) => mutate((draft) => { draft.speed = value; })}
            />
            <ControlCheck
              label="Loop timeline"
              checked={state.loopTimeline}
              onChange={(checked) => mutate((draft) => { draft.loopTimeline = checked; })}
            />
            <ControlCheck
              label="Advanced contradictory states"
              checked={state.advancedOverrides}
              onChange={(checked) => mutate((draft) => { draft.advancedOverrides = checked; })}
            />
          </section>

          <section className="control-section">
            <p className="kicker">Bitcoin authority</p>
            <h2>Node safety</h2>
            <div className="check-grid">
              {([
                ["RPC reachable", "rpcReachable"],
                ["RPC synchronized", "rpcSynced"],
                ["Initial block download", "initialBlockDownload"],
                ["ZMQ healthy", "zmqHealthy"],
                ["Mining work safe", "miningSafe"],
                ["Peer loops healthy", "peerLoopsHealthy"],
                ["Outbound relay healthy", "outboundRelayHealthy"],
                ["Version compatible", "versionCompatible"]
              ] as const).map(([label, key]) => (
                <ControlCheck
                  key={key}
                  label={label}
                  checked={state.node[key]}
                  onChange={(checked) => mutate((draft) => { draft.node[key] = checked; })}
                />
              ))}
            </div>
          </section>

          <section className="control-section">
            <p className="kicker">Difficulty telemetry</p>
            <h2>Work and reserve</h2>
            <ControlNumber
              label="Team work rate (TH/s)"
              value={state.work.poolHashrateThs}
              onChange={(value) => mutate((draft) => { draft.work.poolHashrateThs = value; })}
            />
            <ControlNumber
              label="Order-statistic observations"
              value={state.work.observationCount}
              onChange={(value) => mutate((draft) => { draft.work.observationCount = Math.round(value); })}
            />
            <ControlNumber
              label="Admission floor difficulty"
              value={state.work.admissionFloorDifficulty}
              onChange={(value) => mutate((draft) => { draft.work.admissionFloorDifficulty = value; })}
            />
            <label className="control-number">
              <span>Proof / payout address</span>
              <input
                type="text"
                value={proofAddress}
                onChange={(event) => setProofAddress(event.target.value)}
              />
            </label>
            <ControlNumber
              label="Deterministic target rank"
              value={proofRank}
              onChange={(value) => setProofRank(Math.min(897, Math.max(1, Math.round(value))))}
            />
            <div className="stat-strip">
              <span>reserve <strong>{state.reserve.length}/897</strong></span>
              <span>locked <strong>{state.lockedPayouts.length}</strong></span>
              <span>paid once <strong>{state.chain.paidProofRemovals}</strong></span>
            </div>
          </section>

          <section className="control-section">
            <p className="kicker">Local mining</p>
            <h2>Adapters</h2>
            <div className="entity-list">
              {state.adapters.map((adapter) => (
                <AdapterRow
                  adapter={adapter}
                  key={adapter.id}
                  update={(change) => mutate((draft) => {
                    const target = draft.adapters.find((item) => item.id === adapter.id)!;
                    Object.assign(target, change);
                  })}
                />
              ))}
            </div>
          </section>

          <section className="control-section">
            <p className="kicker">Network topology</p>
            <div className="section-heading">
              <h2>Peers</h2>
              <button
                type="button"
                className="ghost"
                onClick={() => mutate((draft) => {
                  const index = draft.peers.length + 1;
                  draft.peers.push({
                    id: `peer-${index}`,
                    endpoint: `https://peer-${index}.example`,
                    connected: true,
                    http: true,
                    webSocket: true,
                    udp: false,
                    latencyMs: 45,
                    currentStateId: draft.chain.currentStateId,
                    candidateStateId: draft.chain.candidateStateId,
                    compatible: true
                  });
                })}
              >
                Add peer
              </button>
            </div>
            <div className="entity-list">
              {state.peers.map((peer) => (
                <PeerRow
                  peer={peer}
                  key={peer.id}
                  busy={busy}
                  setConnected={(connected) => void action({
                    action: connected ? "peer.reconnect" : "peer.disconnect",
                    peer: peer.id
                  })}
                  update={(change) => mutate((draft) => {
                    const target = draft.peers.find((item) => item.id === peer.id)!;
                    Object.assign(target, change);
                  })}
                />
              ))}
            </div>
          </section>

          <section className="control-section">
            <p className="kicker">Liveness only</p>
            <h2>Pulse proofs</h2>
            <ControlCheck
              label="Pulse generation enabled"
              checked={state.pulse.enabled}
              onChange={(checked) => mutate((draft) => { draft.pulse.enabled = checked; })}
            />
            <ControlNumber
              label="Target interval (seconds)"
              value={state.pulse.targetIntervalSeconds}
              onChange={(value) => mutate((draft) => { draft.pulse.targetIntervalSeconds = Math.max(1, Math.round(value)); })}
            />
            <ControlNumber
              label="Relay TTL"
              value={state.pulse.relayTtl}
              onChange={(value) => mutate((draft) => { draft.pulse.relayTtl = Math.max(0, Math.round(value)); })}
            />
            <div className="inline-fields">
              <label>
                accepted
                <input
                  type="number"
                  min="0"
                  value={state.pulse.accepted}
                  onChange={(event) => mutate((draft) => {
                    draft.pulse.accepted = Number(event.target.value);
                  })}
                />
              </label>
              <label>
                rejected
                <input
                  type="number"
                  min="0"
                  value={state.pulse.rejected}
                  onChange={(event) => mutate((draft) => {
                    draft.pulse.rejected = Number(event.target.value);
                  })}
                />
              </label>
            </div>
            <div className="button-row">
              <button type="button" onClick={() => action({ action: "pulse.emit" })}>Emit pulse</button>
              <span className="inline-note">{state.pulse.accepted} accepted</span>
            </div>
          </section>

          <section className="control-section action-grid-section">
            <p className="kicker">Implemented motion</p>
            <h2>Animation checks</h2>
            <label className="control-number">
              <span>Test peer</span>
              <select
                value={selectedPeer?.id ?? ""}
                onChange={(event) => setTestPeer(event.target.value)}
              >
                {state.peers.map((peer) => (
                  <option value={peer.id} key={peer.id}>{peer.id}</option>
                ))}
              </select>
            </label>
            <label className="control-number">
              <span>Test miner</span>
              <select
                value={selectedMiner?.id ?? ""}
                onChange={(event) => setTestMiner(event.target.value)}
              >
                {miners.map((miner) => (
                  <option value={miner.id} key={miner.id}>{miner.username || miner.id}</option>
                ))}
              </select>
            </label>
            <p className="description">
              Proof admissions use the selected rank and displace rank 897 from the full Work Set.
            </p>
            <div className="action-grid">
              <button type="button" onClick={() => action({
                action: "proof.top897", address: proofAddress, rank: proofRank
              })} disabled={busy}>Generator proof</button>
              <button type="button" onClick={() => action({
                action: "proof.top897", address: proofAddress, peer: selectedPeer?.id, rank: proofRank
              })} disabled={busy || !selectedPeer}>Peer proof</button>
              <button type="button" onClick={() => action({
                action: "proof.top897", address: proofAddress, miner: selectedMiner?.id, rank: proofRank
              })} disabled={busy || !selectedMiner}>Miner proof</button>
              <button type="button" onClick={() => action({
                action: "proof.block", address: proofAddress, rank: 1
              })} disabled={busy}>Generator block proof</button>
              <button type="button" onClick={() => action({
                action: "proof.block", address: proofAddress, peer: selectedPeer?.id, rank: 1
              })} disabled={busy || !selectedPeer}>Peer block proof</button>
              <button type="button" onClick={() => action({
                action: "proof.block", address: proofAddress, miner: selectedMiner?.id, rank: 1
              })} disabled={busy || !selectedMiner}>Miner block proof</button>
              <button type="button" onClick={() => action({
                action: "miner.activity", miner: selectedMiner?.id, count: 3
              })} disabled={busy || !selectedMiner}>Miner vardiff shares</button>
              <button type="button" onClick={() => action({ action: "pulse.emit" })} disabled={busy}>
                Generator pulse
              </button>
              <button type="button" onClick={() => action({
                action: "pulse.emit", peer: selectedPeer?.id
              })} disabled={busy || !selectedPeer}>Peer pulse</button>
              <button type="button" onClick={() => action({
                action: "pulse.emit", miner: selectedMiner?.id
              })} disabled={busy || !selectedMiner}>Miner pulse</button>
              <button type="button" onClick={() => action({
                action: "peer.disconnect", peer: selectedPeer?.id
              })} disabled={busy || !selectedPeer?.connected}>Disconnect peer</button>
              <button type="button" onClick={() => action({
                action: "peer.reconnect", peer: selectedPeer?.id
              })} disabled={busy || !selectedPeer || selectedPeer.connected}>Reconnect peer</button>
              <button type="button" onClick={() => action({
                action: "chain.peer-header", peer: selectedPeer?.id
              })} disabled={busy || !selectedPeer}>Peer header</button>
              <button type="button" onClick={() => action({ action: "chain.local-validate" })} disabled={busy}>
                Validate local tip
              </button>
              <button type="button" onClick={() => action({ action: "snapshot.regular" })} disabled={busy}>
                Regular boundary
              </button>
            </div>
          </section>

          <section className="control-section action-grid-section">
            <p className="kicker">Operator health motion</p>
            <h2>Extended animation checks</h2>
            <p className="description">Each action emits the same typed diagram event consumed by the production map.</p>
            <div className="action-grid">
              <button type="button" onClick={() => action({ action: "proof.reject", miner: selectedMiner?.id })} disabled={busy || !selectedMiner}>Reject miner proof</button>
              <button type="button" onClick={() => action({ action: "proof.reject", peer: selectedPeer?.id })} disabled={busy || !selectedPeer}>Reject peer proof</button>
              <button type="button" onClick={() => action({ action: "chain.invalid-header", peer: selectedPeer?.id })} disabled={busy || !selectedPeer}>Reject header</button>
              <button type="button" onClick={() => action({ action: "snapshot.gridpool-paid" })} disabled={busy}>GridPool paid block</button>
              <button type="button" onClick={() => action({ action: "snapshot.sibling-merge", peer: selectedPeer?.id, count: 12 })} disabled={busy || !selectedPeer}>Sibling merge</button>
              <button type="button" onClick={() => action({ action: "state.diverge", peer: selectedPeer?.id })} disabled={busy || !selectedPeer}>Diverge peer</button>
              <button type="button" onClick={() => action({ action: "state.converge", peer: selectedPeer?.id })} disabled={busy || !selectedPeer}>Converge states</button>
              <button type="button" onClick={() => action({ action: "chain.reorg", count: 1 })} disabled={busy}>Shallow reorg</button>
              <button type="button" onClick={() => action({ action: "peer.transport", peer: selectedPeer?.id, transport: "udp", value: 0 })} disabled={busy || !selectedPeer?.udp}>Transport fallback</button>
              <button type="button" onClick={() => action({ action: "node.safety", value: state.node.miningSafe ? 0 : 1 })} disabled={busy}>{state.node.miningSafe ? "Lose mining safety" : "Recover mining safety"}</button>
              <button type="button" onClick={() => action({ action: selectedBitcoinPeer?.connected ? "bitcoin.peer-disconnect" : "bitcoin.peer-connect", peer: selectedBitcoinPeer?.id })} disabled={busy || !selectedBitcoinPeer}>{selectedBitcoinPeer?.connected ? "Disconnect Bitcoin peer" : "Reconnect Bitcoin peer"}</button>
            </div>
          </section>

          <section className="control-section">
            <p className="kicker">Transport faults</p>
            <h2>Dashboard delivery</h2>
            <ControlNumber
              label="API latency (ms)"
              value={state.faults.apiLatencyMs}
              onChange={(value) => mutate((draft) => { draft.faults.apiLatencyMs = Math.max(0, Math.round(value)); })}
            />
            <ControlCheck
              label="Fail dashboard APIs"
              checked={state.faults.apiFailure}
              onChange={(checked) => mutate((draft) => { draft.faults.apiFailure = checked; })}
            />
            <ControlCheck
              label="Drop SignalR invalidations"
              checked={state.faults.signalRDrop}
              onChange={(checked) => mutate((draft) => { draft.faults.signalRDrop = checked; })}
            />
          </section>

          <section className="control-section timeline-section">
            <p className="kicker">Deterministic replay</p>
            <h2>YAML timeline</h2>
            <textarea
              aria-label="YAML timeline"
              value={timeline}
              onChange={(event) => setTimeline(event.target.value)}
              spellCheck={false}
            />
            <div className="button-row">
              <button type="button" disabled={busy} onClick={importTimeline}>Load timeline</button>
              <a className="button-link" href="/__sim/api/v1/export" download="gridpool-timeline.yaml">
                Export actions
              </a>
            </div>
          </section>

          <section className="control-section event-section">
            <p className="kicker">Latest mutations</p>
            <h2>Event log</h2>
            <ol>
              {state.events.slice(-10).reverse().map((event) => (
                <li key={`${event.sequence}-${event.action}`}>
                  <code>{event.action}</code>
                  <span>{new Date(event.timestampUtc).toLocaleTimeString()}</span>
                </li>
              ))}
            </ol>
          </section>
        </aside>

        <section className="preview">
          <div className="preview-bar">
            <div>
              <span className="preview-light" />
              <strong>Live observer</strong>
              <span>real HTTP + SignalR</span>
            </div>
            <label>
              preview width
              <input
                type="range"
                min="45"
                max="75"
                value={previewWidth}
                onChange={(event) => setPreviewWidth(Number(event.target.value))}
              />
            </label>
            <a href="/dashboard/" target="_blank" rel="noreferrer">Open separately</a>
          </div>
          <iframe title="Synthetic GridPool dashboard" src="/dashboard/" />
        </section>
      </div>
    </main>
  );
}

function PeerRow({ peer, busy, setConnected, update }: {
  peer: PeerControl;
  busy: boolean;
  setConnected: (connected: boolean) => void;
  update: (change: Partial<PeerControl>) => void;
}) {
  return (
    <article className={peer.connected ? "entity" : "entity entity-offline"}>
      <div className="entity-title">
        <strong>{peer.id}</strong>
        <label className="switch">
          <input
            type="checkbox"
            checked={peer.connected}
            disabled={busy}
            onChange={(event) => setConnected(event.target.checked)}
          />
          <span>{peer.connected ? "online" : "offline"}</span>
        </label>
      </div>
      <div className="transport-row">
        {(["http", "webSocket", "udp"] as const).map((transport) => (
          <label key={transport}>
            <input
              type="checkbox"
              checked={peer[transport]}
              onChange={(event) => update({ [transport]: event.target.checked })}
            />
            {transport === "webSocket" ? "WS" : transport.toUpperCase()}
          </label>
        ))}
      </div>
      <label className="compact-number">
        latency
        <input
          type="number"
          min="0"
          value={peer.latencyMs}
          onChange={(event) => update({ latencyMs: Number(event.target.value) })}
        />
        ms
      </label>
    </article>
  );
}

function AdapterRow({ adapter, update }: {
  adapter: AdapterControl;
  update: (change: Partial<AdapterControl>) => void;
}) {
  return (
    <article className={adapter.connected ? "entity" : "entity entity-offline"}>
      <div className="entity-title">
        <strong>{adapter.displayName}</strong>
        <label className="switch">
          <input
            type="checkbox"
            checked={adapter.connected}
            onChange={(event) => update({ connected: event.target.checked })}
          />
          <span>{adapter.connected ? "live" : "down"}</span>
        </label>
      </div>
      <div className="inline-fields">
        <label>
          clients
          <input
            type="number"
            min="0"
            value={adapter.clientCount}
            onChange={(event) => update({ clientCount: Number(event.target.value) })}
          />
        </label>
        <label>
          TH/s
          <input
            type="number"
            min="0"
            value={adapter.hashrateThs}
            onChange={(event) => update({ hashrateThs: Number(event.target.value) })}
          />
        </label>
      </div>
    </article>
  );
}

function ControlCheck({ label, checked, onChange }: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="control-check">
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

function ControlNumber({ label, value, onChange }: {
  label: string;
  value: number;
  onChange: (value: number) => void;
}) {
  return (
    <label className="control-number">
      <span>{label}</span>
      <input type="number" value={value} onChange={(event) => onChange(Number(event.target.value))} />
    </label>
  );
}

function ControlRange({ label, value, min, max, step, suffix, onChange }: {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  suffix: string;
  onChange: (value: number) => void;
}) {
  return (
    <label className="control-range">
      <span>{label}<strong>{value}{suffix}</strong></span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  );
}

function formatHashrate(ths: number) {
  return ths >= 1000 ? `${(ths / 1000).toFixed(2)} PH/s` : `${ths.toFixed(1)} TH/s`;
}
