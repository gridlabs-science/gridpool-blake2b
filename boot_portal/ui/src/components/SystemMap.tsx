import { useEffect, useMemo, useRef, useState } from "react";
import { formatAge } from "../format";
import type {
  DashboardDiagram,
  DiagramHistory,
  DiagramEvent,
  DiagramMiner,
  DiagramPeer,
  DiagramProof
} from "../types";

interface Point {
  x: number;
  y: number;
}

interface Layout {
  width: number;
  height: number;
  grid: Point;
  bitcoin: Point;
  rail: Point;
  generator: Point;
  railStart: number;
  railEnd: number;
  railY: number;
}

const desktop: Layout = {
  width: 1200,
  height: 760,
  grid: { x: 335, y: 185 },
  bitcoin: { x: 865, y: 185 },
  rail: { x: 600, y: 390 },
  generator: { x: 600, y: 545 },
  railStart: 320,
  railEnd: 1159,
  railY: 390
};

const portrait: Layout = {
  width: 720,
  height: 980,
  grid: { x: 190, y: 190 },
  bitcoin: { x: 530, y: 190 },
  rail: { x: 360, y: 500 },
  generator: { x: 360, y: 660 },
  railStart: 195,
  railEnd: 689.5,
  railY: 500
};

type Selection =
  | { kind: "proof"; value: DiagramProof }
  | { kind: "peer"; value: DiagramPeer }
  | { kind: "miner"; value: DiagramMiner }
  | { kind: "grid" | "bitcoin" | "generator" };

export function SystemMap({
  diagram,
  history = null,
  historyWindow = "24h",
  onHistoryWindowChange = () => undefined,
  activeEvent,
  onEventComplete,
  operatorUnlocked
}: {
  diagram: DashboardDiagram;
  history?: DiagramHistory | null;
  historyWindow?: "24h" | "7d";
  onHistoryWindowChange?: (value: "24h" | "7d") => void;
  activeEvent: DiagramEvent | null;
  onEventComplete: () => void;
  operatorUnlocked: boolean;
}) {
  const [narrow, setNarrow] = useState(() => window.innerWidth < 720);
  const [help, setHelp] = useState(false);
  const [selectedRank, setSelectedRank] = useState(1);
  const [selection, setSelection] = useState<Selection | null>(null);
  const [railMode, setRailMode] = useState<"auto" | "skyline" | "chase">("auto");
  const [focus, setFocus] = useState<"none" | "slot0" | "pool" | "bitcoin" | "rank">("none");
  const [command, setCommand] = useState("");
  const [commandOutput, setCommandOutput] = useState("");
  const [pendingShares, setPendingShares] = useState<Array<{ sequence: number; rank: number }>>([]);
  const previousSequence = useRef(diagram.latestSequence);
  const layout = narrow ? portrait : desktop;

  useEffect(() => {
    if (!window.matchMedia) return;
    const query = window.matchMedia("(max-width: 719px)");
    const update = () => setNarrow(query.matches);
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);

  useEffect(() => {
    if (!activeEvent) return;
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    const timer = window.setTimeout(onEventComplete, reduced ? 180 : 1900);
    return () => window.clearTimeout(timer);
  }, [activeEvent, onEventComplete]);

  useEffect(() => {
    if (diagram.latestSequence < previousSequence.current) {
      setPendingShares([]);
    }
    previousSequence.current = diagram.latestSequence;
  }, [diagram.latestSequence]);

  useEffect(() => {
    if (!activeEvent) return;
    if (activeEvent.kind === "boundary-validated") {
      const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
      const timer = window.setTimeout(() => setPendingShares([]), reduced ? 0 : 900);
      return () => window.clearTimeout(timer);
    }
    if (activeEvent.kind !== "proof-admitted" || !activeEvent.rank) return;
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    const timer = window.setTimeout(() => {
      setPendingShares((current) => [
        ...current.filter((item) => item.sequence !== activeEvent.sequence),
        { sequence: activeEvent.sequence, rank: activeEvent.rank! }
      ].slice(-12));
    }, reduced ? 0 : 1450);
    return () => window.clearTimeout(timer);
  }, [activeEvent]);

  useEffect(() => {
    setSelectedRank((rank) => Math.min(Math.max(1, rank), Math.max(1, diagram.workSet.length)));
  }, [diagram.workSet.length]);

  const peers = useMemo(
    () => placePeers(diagram.peers, layout),
    [diagram.peers, layout]
  );
  const bitcoinPeers = useMemo(
    () => placeBitcoinPeers(diagram.bitcoin.peers, layout),
    [diagram.bitcoin.peers, layout]
  );
  const miners = useMemo(
    () => placeMiners(diagram.miners, layout),
    [diagram.miners, layout]
  );
  const selectedProof = diagram.workSet[selectedRank - 1] ?? null;
  const hashrateCeiling = Math.max(
    1,
    diagram.grid.hashrateThs ?? 0,
    diagram.workGenerator.hashrateThs ?? 0,
    ...diagram.miners.map((miner) => miner.hashrateThs ?? 0)
  );
  const targetX = rankX(activeEvent?.rank ?? 1, layout);
  const source = eventSource(activeEvent, peers, bitcoinPeers, miners, layout);

  const selectProof = (proof: DiagramProof) => {
    setSelectedRank(proof.rank);
    setSelection({ kind: "proof", value: proof });
  };

  const navigateRail = (event: React.KeyboardEvent<SVGGElement>) => {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const maximum = Math.max(1, diagram.workSet.length);
    const next = event.key === "Home"
      ? 1
      : event.key === "End"
        ? maximum
        : Math.min(maximum, Math.max(1, selectedRank + (event.key === "ArrowRight" ? 1 : -1)));
    setSelectedRank(next);
    const proof = diagram.workSet[next - 1];
    if (proof) setSelection({ kind: "proof", value: proof });
  };

  return (
    <section className="map-stage" aria-label="Live GridPool system map">
      <div className="map-tools">
        <button type="button" onClick={() => setHelp((value) => !value)} aria-pressed={help}>
          {help ? "Hide labels" : "Help"}
        </button>
        <span>{diagram.workSet.length} / 897 proofs</span>
        <span>{historyWindow} proof window</span>
        <span>{diagram.redacted ? "public view" : "operator detail"}</span>
      </div>

      <svg
        className="system-map"
        viewBox={`0 0 ${layout.width} ${layout.height}`}
        role="img"
        aria-labelledby="system-map-title system-map-description"
      >
        <title id="system-map-title">GridPool node wiring diagram</title>
        <desc id="system-map-description">
          GridPool peers, the local Bitcoin node, one work generator, local miners,
          the provisional Work Set, and the locked payout snapshot.
        </desc>

        <g className={diagram.bitcoin.miningSafe ? "map-foundation" : "map-foundation foundation-unsafe"}>
          <line x1={layout.grid.x} y1={layout.grid.y} x2={layout.bitcoin.x} y2={layout.bitcoin.y} />
          <line x1={layout.grid.x} y1={layout.grid.y} x2={layout.generator.x} y2={layout.generator.y} />
          <line x1={layout.bitcoin.x} y1={layout.bitcoin.y} x2={layout.generator.x} y2={layout.generator.y} />
          <line x1={layout.grid.x} y1={layout.grid.y} x2={layout.rail.x} y2={layout.rail.y} />
          <line x1={layout.bitcoin.x} y1={layout.bitcoin.y} x2={layout.rail.x} y2={layout.rail.y} />
          <line x1={layout.rail.x} y1={layout.rail.y} x2={layout.generator.x} y2={layout.generator.y} />
        </g>

        <HashrateArc
          point={layout.grid}
          value={diagram.grid.hashrateThs}
          ceiling={hashrateCeiling}
          maximumRadius={22}
          inferred
        />
        <HashrateArc
          point={layout.generator}
          value={diagram.workGenerator.hashrateThs}
          ceiling={hashrateCeiling}
          maximumRadius={18}
        />
        <TargetAperture point={layout.bitcoin} difficulty={diagram.bitcoin.networkDifficulty} />

        <g className="peer-constellation" aria-label={`${diagram.peers.length} peers`}>
          {peers.map(({ peer, point }) => (
            <g key={peer.visualId}>
              <line
                className={`map-link ${peer.connected ? "link-live" : "link-dormant"} state-${peer.stateRelation} transport-${peer.transport || "unknown"}`}
                x1={layout.grid.x}
                y1={layout.grid.y}
                x2={point.x}
                y2={point.y}
                style={{ "--link-strength": peer.connected ? 1 : 0.16 } as React.CSSProperties}
              />
              {peer.stateRelation === "divergent" ? (
                <line
                  className="map-link state-shadow"
                  x1={layout.grid.x}
                  y1={layout.grid.y + 4}
                  x2={point.x}
                  y2={point.y + 4}
                />
              ) : null}
              <circle
                className={peer.connected ? "map-dot peer-dot" : "map-dot peer-dot dot-dormant"}
                cx={point.x}
                cy={point.y}
                r={peer.connected ? 4 : 3}
                onClick={() => setSelection({ kind: "peer", value: peer })}
              />
              <text
                className="peer-label"
                x={point.x < layout.grid.x ? point.x - 9 : point.x + 9}
                y={point.y < layout.grid.y ? point.y - 7 : point.y + 14}
                textAnchor={point.x < layout.grid.x ? "end" : "start"}
              >
                {compactPeerLabel(peer.displayName || peer.nodeId)}
              </text>
            </g>
          ))}
        </g>

        <g className="bitcoin-peer-constellation" aria-label={`${diagram.bitcoin.peerCount} Bitcoin peers`}>
          {bitcoinPeers.map(({ peer, point }) => (
            <g key={peer.visualId}>
              <line
                className="map-link bitcoin-peer-link link-live"
                x1={layout.bitcoin.x}
                y1={layout.bitcoin.y}
                x2={point.x}
                y2={point.y}
                style={{ "--link-strength": 0.58 } as React.CSSProperties}
              />
              <circle className="map-dot bitcoin-peer-dot" cx={point.x} cy={point.y} r={3} />
            </g>
          ))}
        </g>

        <g className="miner-constellation" aria-label={`${diagram.miners.length} local miners`}>
          {miners.map(({ miner, point }) => (
            <g key={miner.visualId}>
              <line
                className={`map-link link-live miner-link ${isRecent(miner.lastRejectedUtc, 600) ? "miner-link-rejected" : ""}`}
                x1={layout.generator.x}
                y1={layout.generator.y}
                x2={point.x}
                y2={point.y}
                style={{ "--link-strength": freshness(miner.lastShareUtc) } as React.CSSProperties}
              />
              <HashrateArc
                point={point}
                value={miner.hashrateThs}
                ceiling={hashrateCeiling}
                maximumRadius={12}
              />
              <circle
                className="map-dot miner-dot"
                cx={point.x}
                cy={point.y}
                r={4}
                onClick={() => setSelection({ kind: "miner", value: miner })}
              />
              {miner.hashrateDisplay !== "--" ? (
                <text className="hashrate-label" textAnchor="middle" x={point.x} y={point.y + 23}>
                  {help && miner.username
                    ? `${miner.username} · ${miner.hashrateDisplay}`
                    : miner.hashrateDisplay}
                </text>
              ) : null}
            </g>
          ))}
        </g>

        <WorkSetRail
          diagram={diagram}
          history={history}
          layout={layout}
          selectedRank={selectedRank}
          railMode={railMode}
          focus={focus}
          pendingShares={pendingShares}
          onKeyDown={navigateRail}
          onSelect={selectProof}
        />

        <NodeMark
          point={layout.grid}
          label="GridPool"
          help={help}
          onClick={() => setSelection({ kind: "grid" })}
        />

        {diagram.grid.hashrateDisplay !== "--" ? (
          <text className="hashrate-label node-metric" x={layout.grid.x + 12} y={layout.grid.y - 10}>
            {gridHashrateLabel(diagram)}
          </text>
        ) : null}
        {diagram.bitcoin.networkDifficultyDisplay !== "--" ? (
          <text className="hashrate-label node-metric" textAnchor="end" x={layout.bitcoin.x - 12} y={layout.bitcoin.y - 10}>
            {diagram.bitcoin.networkHashrateDisplay !== "--" ? `${diagram.bitcoin.networkHashrateDisplay} · ` : ""}
            {diagram.bitcoin.networkDifficultyDisplay} diff
          </text>
        ) : null}
        {diagram.workGenerator.hashrateDisplay !== "--" ? (
          <text className="hashrate-label node-metric" x={layout.generator.x + 17} y={layout.generator.y + 25}>
            {diagram.workGenerator.hashrateDisplay} local
          </text>
        ) : null}
        <NodeMark
          point={layout.bitcoin}
          label="Bitcoin"
          help={help}
          onClick={() => setSelection({ kind: "bitcoin" })}
        />
        <NodeMark
          point={layout.generator}
          label={diagram.workGenerator.displayName || "Work generator"}
          help={help}
          onClick={() => setSelection({ kind: "generator" })}
        />

        {activeEvent ? (
          <EventGlyph
            key={activeEvent.sequence}
            event={activeEvent}
            source={source}
            target={{ x: targetX, y: layout.railY }}
            grid={layout.grid}
            bitcoin={layout.bitcoin}
            rail={layout.rail}
            generator={layout.generator}
            peers={peers.filter(({ peer }) => peer.connected).map(({ point }) => point)}
            railStart={layout.railStart}
            railEnd={layout.railEnd}
          />
        ) : null}
      </svg>

      <MapCommandLine
        value={command}
        output={commandOutput}
        onChange={setCommand}
        onRun={(value) => runMapCommand(
          value,
          diagram,
          history,
          setCommandOutput,
          setRailMode,
          setFocus,
          onHistoryWindowChange,
          (rank) => {
            const proof = diagram.workSet[rank - 1];
            if (proof) selectProof(proof);
          }
        )}
      />

      <MapDetail
        selection={selection}
        diagram={diagram}
        operatorUnlocked={operatorUnlocked}
        onClose={() => setSelection(null)}
      />

      <p className="map-live-region" aria-live="polite">
        {activeEvent ? describeEvent(activeEvent) : ""}
      </p>
    </section>
  );
}

function WorkSetRail({
  diagram,
  history,
  layout,
  selectedRank,
  railMode,
  focus,
  pendingShares,
  onKeyDown,
  onSelect
}: {
  diagram: DashboardDiagram;
  history: DiagramHistory | null;
  layout: Layout;
  selectedRank: number;
  railMode: "auto" | "skyline" | "chase";
  focus: "none" | "slot0" | "pool" | "bitcoin" | "rank";
  pendingShares: Array<{ sequence: number; rank: number }>;
  onKeyDown: (event: React.KeyboardEvent<SVGGElement>) => void;
  onSelect: (proof: DiagramProof) => void;
}) {
  const difficulties = diagram.workSet
    .map((proof) => proof.difficulty ?? 0)
    .filter((value) => value > 0);
  const poolBest = difficulties[0] ?? 1;
  const floor = difficulties.at(-1) ?? 1;
  const network = diagram.bitcoin.networkDifficulty ?? Math.max(poolBest * 10, floor * 10);
  const domainMin = Math.max(Number.MIN_VALUE, floor / Math.pow(10, 0.25));
  const domainMax = Math.max(network, poolBest, domainMin * 10);
  const top = layout.railY - 112;
  const yFor = (difficulty: number) => logPosition(difficulty, domainMin, domainMax, layout.railY, top);
  const skyline = diagram.workSet
    .map((proof) => `${rankX(proof.rank, layout)},${yFor(proof.difficulty ?? domainMin)}`)
    .join(" ");
  const slotProofs = diagram.slotZero.verified
    ? diagram.workSet.filter((proof) =>
        proof.address.toLowerCase() === diagram.slotZero.address.toLowerCase())
    : [];
  const slotAnchor = slotProofs[0]
    ? rankX(slotProofs[0].rank, layout)
    : layout.railStart;
  const selected = diagram.workSet[selectedRank - 1];
  const chaseVisible = railMode === "chase" || (railMode === "auto" && focus !== "none");
  const chaseValues = [
    { label: "local best", value: history?.bestDifficulty ?? null, kind: "local" },
    { label: "rank 897", value: floor, kind: "floor" },
    { label: "rank 300", value: diagram.workSet[299]?.difficulty ?? null, kind: "cutoff" },
    { label: "pool best", value: poolBest, kind: "pool" },
    { label: "network", value: diagram.bitcoin.networkDifficulty, kind: "network" }
  ]
    .filter((item): item is { label: string; value: number; kind: string } => item.value != null && item.value > 0)
    .sort((left, right) => left.value - right.value);
  const chaseY = layout.railY + 74;
  const chaseX = (difficulty: number) => logPosition(
    difficulty,
    domainMin,
    domainMax,
    layout.railStart,
    layout.railEnd
  );
  const inspectByPointer = (event: React.MouseEvent<SVGRectElement>) => {
    const bounds = event.currentTarget.getBoundingClientRect();
    const ratio = Math.min(1, Math.max(0, (event.clientX - bounds.left) / Math.max(1, bounds.width)));
    const diagramX = layout.railStart + ratio * (layout.railEnd - layout.railStart);
    const rank = diagramX <= layout.rail.x
      ? 1 + Math.round(((diagramX - layout.railStart) / Math.max(1, layout.rail.x - layout.railStart)) * 299)
      : 300 + Math.round(((diagramX - layout.rail.x) / Math.max(1, layout.railEnd - layout.rail.x)) * 597);
    const boundedRank = Math.min(diagram.workSet.length, Math.max(1, rank));
    const proof = diagram.workSet[boundedRank - 1];
    if (proof) onSelect(proof);
  };

  return (
    <g
      className="workset-rail"
      role="listbox"
      aria-label="Provisional unpaid Work Set difficulty skyline, ranked strongest to weakest"
      aria-activedescendant={selected ? `proof-${selected.visualId}` : undefined}
      tabIndex={0}
      onKeyDown={onKeyDown}
    >
      <line className="network-difficulty-line" x1={layout.railStart} y1={top} x2={layout.railEnd} y2={top} />
      <line className="network-difficulty-link" x1={layout.railEnd} y1={top} x2={layout.bitcoin.x} y2={layout.bitcoin.y} />
      <text className="difficulty-label" textAnchor="end" x={layout.railEnd - 5} y={top - 7}>network difficulty</text>
      <polyline className="workset-skyline" points={skyline} />
      <line className="rail-line" x1={layout.railStart} y1={layout.railY} x2={layout.railEnd} y2={layout.railY} />
      <line className="snapshot-line" x1={layout.railStart} y1={layout.railY - 16} x2={layout.railEnd} y2={layout.railY - 16} />
      <line className="prospective-boundary" x1={rankX(300, layout)} y1={top} x2={rankX(300, layout)} y2={layout.railY + 8} />
      <text className="closest-label" x={layout.railStart + 7} y={yFor(poolBest) - 8}>closest observed proof</text>
      {slotProofs.map((proof) => {
        const x = rankX(proof.rank, layout);
        const y = yFor(proof.difficulty ?? domainMin);
        return proof.blockQuality ? (
          <rect className="slot-zero-proof slot-zero-block" key={proof.visualId} x={x - 4} y={y - 4} width={8} height={8} />
        ) : (
          <line className={`slot-zero-proof ${proof.rank <= 300 ? "slot-zero-top300" : ""}`} key={proof.visualId} x1={x} y1={y - 7} x2={x} y2={y + 7} />
        );
      })}
      {selected ? (
        <line
          id={`proof-${selected.visualId}`}
          className="proof-selected skyline-selection"
          x1={rankX(selected.rank, layout)}
          y1={yFor(selected.difficulty ?? domainMin) - 12}
          x2={rankX(selected.rank, layout)}
          y2={layout.railY + 10}
        />
      ) : null}
      <rect
        className="skyline-hit-area"
        x={layout.railStart}
        y={top}
        width={layout.railEnd - layout.railStart}
        height={layout.railY - top + 12}
        onMouseMove={inspectByPointer}
        onClick={inspectByPointer}
      />
      <text className="rank-label" x={layout.railStart} y={layout.railY + 24}>1</text>
      <text className="rank-label" textAnchor="middle" x={rankX(300, layout)} y={layout.railY + 24}>300</text>
      <text className="rank-label" textAnchor="end" x={layout.railEnd} y={layout.railY + 24}>897</text>
      <line className="slot-zero-tick" x1={slotAnchor} y1={layout.railY - 7} x2={slotAnchor} y2={layout.railY + 7} />
      <text
        className="slot-zero-label"
        x={slotAnchor + 8}
        y={layout.railY + 35}
        transform={`rotate(22 ${slotAnchor + 8} ${layout.railY + 35})`}
      >
        Slot 0 · {diagram.slotZero.verified ? compactAddress(diagram.slotZero.address) : "awaiting verified local proof"}
      </text>
      {pendingShares.map((share) => {
        const x = rankX(share.rank, layout);
        return <line key={share.sequence} className="pending-share" x1={x} y1={layout.railY - 30} x2={x} y2={layout.railY - 18} />;
      })}
      {chaseVisible ? (
        <g className="difficulty-chase" aria-label="Focused logarithmic difficulty comparison">
          <line className="chase-line" x1={layout.railStart} y1={chaseY} x2={layout.railEnd} y2={chaseY} />
          {(history?.proofs ?? []).slice(0, 64).map((proof) => {
            const x = chaseX(proof.difficulty);
            return proof.blockQuality
              ? <rect className="chase-observation chase-block" key={proof.proofId} x={x - 2.5} y={chaseY - 2.5} width={5} height={5} />
              : <line className="chase-observation" key={proof.proofId} x1={x} y1={chaseY - 3} x2={x} y2={chaseY + 3} />;
          })}
          {chaseValues.map((item, index) => {
            const x = chaseX(item.value);
            const above = index % 2 === 0;
            return (
              <g key={item.kind} className={`chase-marker chase-${item.kind}`}>
                <line x1={x} y1={chaseY - 8} x2={x} y2={chaseY + 8} />
                <text textAnchor={x > layout.railEnd - 100 ? "end" : x < layout.railStart + 100 ? "start" : "middle"} x={x} y={chaseY + (above ? -13 : 23)}>
                  {item.label}
                </text>
              </g>
            );
          })}
        </g>
      ) : null}
    </g>
  );
}

function logPosition(value: number, minimum: number, maximum: number, start: number, end: number) {
  const safe = Math.min(maximum, Math.max(minimum, value));
  const span = Math.log10(maximum) - Math.log10(minimum);
  const ratio = span <= 0 ? 0 : (Math.log10(safe) - Math.log10(minimum)) / span;
  return start + ratio * (end - start);
}

function MapCommandLine({
  value,
  output,
  onChange,
  onRun
}: {
  value: string;
  output: string;
  onChange: (value: string) => void;
  onRun: (value: string) => void;
}) {
  return (
    <form
      className="map-command-line"
      onSubmit={(event) => {
        event.preventDefault();
        onRun(value);
        onChange("");
      }}
    >
      <span aria-hidden="true">gp&gt;</span>
      <input
        aria-label="System map command"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        autoComplete="off"
        spellCheck={false}
        placeholder="help"
      />
      <output aria-live="polite">{output}</output>
    </form>
  );
}

function runMapCommand(
  raw: string,
  diagram: DashboardDiagram,
  history: DiagramHistory | null,
  setOutput: (value: string) => void,
  setRailMode: (value: "auto" | "skyline" | "chase") => void,
  setFocus: (value: "none" | "slot0" | "pool" | "bitcoin" | "rank") => void,
  setWindow: (value: "24h" | "7d") => void,
  selectRank: (rank: number) => void
) {
  const parts = raw.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const [command, arg, value] = parts;
  if (!command) return;
  if (command === "help") {
    setOutput("window 24h|7d · focus slot0|pool|bitcoin|rank N · inspect rank N · history slot0 · rail auto|skyline|chase · export json|csv · clear");
    return;
  }
  if (command === "clear") {
    setOutput("");
    setFocus("none");
    return;
  }
  if (command === "window" && (arg === "24h" || arg === "7d")) {
    setWindow(arg);
    setOutput(`history window ${arg}`);
    return;
  }
  if (command === "rail" && (arg === "auto" || arg === "skyline" || arg === "chase")) {
    setRailMode(arg);
    setOutput(`rail ${arg}`);
    return;
  }
  if (command === "focus" && ["slot0", "pool", "bitcoin"].includes(arg)) {
    setFocus(arg as "slot0" | "pool" | "bitcoin");
    setOutput(`focus ${arg}`);
    return;
  }
  if ((command === "focus" || command === "inspect") && arg === "rank") {
    const rank = Number(value);
    if (Number.isInteger(rank) && rank >= 1 && rank <= Math.min(897, diagram.workSet.length)) {
      selectRank(rank);
      setFocus("rank");
      const proof = diagram.workSet[rank - 1];
      setOutput(`rank ${rank} · ${proof.difficultyDisplay} · ${proof.address}`);
      return;
    }
  }
  if (command === "history" && arg === "slot0") {
    setFocus("slot0");
    setOutput(history
      ? `${history.window} · ${history.proofs.length} retained observations · best ${history.bestDifficultyDisplay}`
      : "local proof history is still loading");
    return;
  }
  if (command === "export" && (arg === "json" || arg === "csv")) {
    exportSafeDiagram(arg, diagram, history);
    setOutput(`exported ${arg}`);
    return;
  }
  setOutput(`unknown command: ${raw.trim()}`);
}

function exportSafeDiagram(format: "json" | "csv", diagram: DashboardDiagram, history: DiagramHistory | null) {
  const workSet = diagram.workSet.map((proof) => ({
    proofId: proof.proofId,
    rank: proof.rank,
    address: proof.address,
    difficulty: proof.difficulty,
    firstSeenUtc: proof.firstSeenUtc,
    locked: proof.locked
  }));
  const localHistory = (history?.proofs ?? []).map((proof) => ({
    proofId: proof.proofId,
    address: proof.address,
    proofClass: proof.proofClass,
    difficulty: proof.difficulty,
    timestampUtc: proof.timestampUtc,
    enteredWorkSet: proof.enteredWorkSet,
    blockQuality: proof.blockQuality
  }));
  let contents: string;
  let mime: string;
  if (format === "json") {
    contents = JSON.stringify({ exportedAtUtc: new Date().toISOString(), workSet, localHistory }, null, 2);
    mime = "application/json";
  } else {
    const rows = [
      "collection,proofId,rank,address,difficulty,timestampUtc,enteredWorkSet,blockQuality",
      ...workSet.map((proof) => ["workset", proof.proofId, proof.rank, proof.address, proof.difficulty ?? "", proof.firstSeenUtc ?? "", true, false].map(csvCell).join(",")),
      ...localHistory.map((proof) => ["history", proof.proofId, "", proof.address, proof.difficulty, proof.timestampUtc, proof.enteredWorkSet, proof.blockQuality].map(csvCell).join(","))
    ];
    contents = rows.join("\n");
    mime = "text/csv";
  }
  const url = URL.createObjectURL(new Blob([contents], { type: mime }));
  const link = document.createElement("a");
  link.href = url;
  link.download = `gridpool-map-${Date.now()}.${format}`;
  link.click();
  URL.revokeObjectURL(url);
}

function csvCell(value: unknown) {
  const text = String(value ?? "");
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function compactAddress(value: string) {
  return value.length <= 28 ? value : `${value.slice(0, 14)}…${value.slice(-8)}`;
}

function NodeMark({
  point,
  label,
  help,
  onClick
}: {
  point: Point;
  label: string;
  help: boolean;
  onClick: () => void;
}) {
  return (
    <g className="node-mark" onClick={onClick}>
      <circle className="node-hit" cx={point.x} cy={point.y} r={16} />
      {help ? <text x={point.x + 14} y={point.y - 12}>{label}</text> : null}
    </g>
  );
}

function HashrateArc({
  point,
  value,
  ceiling,
  maximumRadius,
  inferred = false
}: {
  point: Point;
  value: number | null;
  ceiling: number;
  maximumRadius: number;
  inferred?: boolean;
}) {
  if (value == null || value <= 0) return null;
  const radius = 5 + Math.sqrt(Math.min(1, value / ceiling)) * (maximumRadius - 5);
  return (
    <path
      className={`hashrate-arc ${inferred ? "hashrate-inferred" : "hashrate-reported"}`}
      d={arcPath(point, radius, -142, 142)}
    />
  );
}

function TargetAperture({ point, difficulty }: { point: Point; difficulty: number | null }) {
  if (difficulty == null || difficulty <= 0) return null;
  const size = Math.max(8, Math.min(15, 17 - Math.log10(difficulty) / 3));
  const arm = 4;
  const corners = [
    `M ${point.x - size + arm} ${point.y - size} H ${point.x - size} V ${point.y - size + arm}`,
    `M ${point.x + size - arm} ${point.y - size} H ${point.x + size} V ${point.y - size + arm}`,
    `M ${point.x - size + arm} ${point.y + size} H ${point.x - size} V ${point.y + size - arm}`,
    `M ${point.x + size - arm} ${point.y + size} H ${point.x + size} V ${point.y + size - arm}`
  ];
  return <path className="target-aperture" d={corners.join(" ")} />;
}

function EventGlyph({
  event,
  source,
  target,
  grid,
  bitcoin,
  rail,
  generator,
  peers,
  railStart,
  railEnd
}: {
  event: DiagramEvent;
  source: Point;
  target: Point;
  grid: Point;
  bitcoin: Point;
  rail: Point;
  generator: Point;
  peers: Point[];
  railStart: number;
  railEnd: number;
}) {
  const progress = useAnimatedProgress(event.sequence);
  const traces = eventTraces(event, source, target, grid, bitcoin, rail, generator, peers, railEnd);
  const flashOpacity = event.kind === "boundary-validated"
    ? snapshotFlashOpacity(progress)
    : 0;
  return (
    <g className={`event-glyph event-${event.kind}`}>
      {traces.map((trace, index) => (
        <TraceMarker key={`${trace.shape}-${index}`} trace={trace} progress={progress} />
      ))}
      {event.kind === "peer-connection" && progress > 0.58 ? (
        <circle
          className={`event-ripple ${event.connected ? "event-connected" : "event-disconnected"}`}
          cx={event.connected ? source.x : grid.x}
          cy={event.connected ? source.y : grid.y}
          r={4.8 + connectionRippleProgress(progress) * 18}
          style={{ opacity: 1 - connectionRippleProgress(progress) }}
        />
      ) : null}
      {flashOpacity > 0 ? (
        <line
          className="snapshot-flash"
          x1={railStart}
          y1={rail.y}
          x2={railEnd}
          y2={rail.y}
          style={{ opacity: flashOpacity }}
        />
      ) : null}
      {event.kind === "boundary-validated" && event.boundaryKind === "gridpool-paid" ? (
        <line
          className="paid-snapshot-drain"
          x1={railStart + (railEnd - railStart) * progress}
          y1={rail.y - 16}
          x2={railEnd}
          y2={rail.y - 16}
          style={{ opacity: 1 - progress }}
        />
      ) : null}
    </g>
  );
}

type TraceShape = "share" | "pulse" | "tip" | "connection";

interface TraceSpec {
  points: Point[];
  milestones: number[];
  shape: TraceShape;
  route: string;
  className?: string;
  window?: [number, number];
  fadeOut?: boolean;
}

function eventTraces(
  event: DiagramEvent,
  source: Point,
  target: Point,
  grid: Point,
  bitcoin: Point,
  rail: Point,
  generator: Point,
  peers: Point[],
  railEnd: number
): TraceSpec[] {
  const peerTargets = peers.length ? peers : [grid];
  const otherPeers = peerTargets.filter((peer) => Math.hypot(peer.x - source.x, peer.y - source.y) > 1);
  const fromMiner = event.sourceKind === "miner";
  const localOrigin = fromMiner ? source : generator;
  if (event.kind === "peer-connection") {
    return [{
      points: event.connected ? [grid, source] : [source, grid],
      milestones: [0, 1],
      shape: "connection",
      route: event.connected ? "grid-peer-connect" : "peer-grid-disconnect",
      className: event.connected ? "event-connected" : "event-disconnected",
      window: [0, 0.76],
      fadeOut: !event.connected
    }];
  }
  if (event.kind === "local-miner-activity") {
    const packetCount = Math.min(3, Math.max(1, event.acceptedShareDelta ?? 1));
    return Array.from({ length: packetCount }, (_, index) => ({
      points: [source, generator],
      milestones: [0, 1],
      shape: "share" as const,
      route: "miner-generator",
      className: "event-vardiff",
      window: [index * 0.12, 0.66 + index * 0.12] as [number, number]
    }));
  }
  if (event.kind === "proof-rejected") {
    return [{
      points: event.sourceKind === "peer" ? [source, grid] : [source, generator],
      milestones: [0, 1],
      shape: "share",
      route: event.sourceKind === "peer" ? "peer-grid-rejected" : "miner-generator-rejected",
      className: "event-rejected",
      fadeOut: true
    }];
  }
  if (event.kind === "peer-transport" || event.kind === "peer-state") {
    return [{
      points: [grid, source],
      milestones: [0, 1],
      shape: "connection",
      route: event.kind === "peer-transport" ? "grid-peer-transport" : "grid-peer-state",
      className: event.kind === "peer-state" && event.currentValue === "divergent" ? "event-rejected" : "event-state"
    }];
  }
  if (event.kind === "peer-header-rejected") {
    return [{
      points: [source, grid, bitcoin],
      milestones: [0, 0.48, 1],
      shape: "tip",
      route: "peer-grid-bitcoin-rejected",
      className: "event-rejected",
      fadeOut: true
    }];
  }
  if (event.kind === "sibling-merge") {
    return [{
      points: [source, grid, rail],
      milestones: [0, 0.5, 1],
      shape: "share",
      route: "peer-grid-rail-sibling",
      className: "event-sibling"
    }];
  }
  if (event.kind === "chain-reorganization") {
    return [
      { points: [rail, bitcoin], milestones: [0, 1], shape: "tip", route: "rail-bitcoin-reorg", className: "event-rejected", fadeOut: true },
      { points: [bitcoin, rail], milestones: [0, 1], shape: "tip", route: "bitcoin-rail-replacement", className: "event-state", window: [0.48, 1] }
    ];
  }
  if (event.kind === "bitcoin-peer-connection") {
    return [{
      points: event.connected ? [bitcoin, source] : [source, bitcoin],
      milestones: [0, 1],
      shape: "connection",
      route: event.connected ? "bitcoin-peer-connect" : "bitcoin-peer-disconnect",
      className: event.connected ? "event-connected" : "event-disconnected",
      fadeOut: !event.connected
    }];
  }
  if (event.kind === "mining-safety") {
    return [];
  }
  if (event.kind === "pulse-accepted") {
    if (event.sourceKind === "peer") {
      return [{ points: [source, grid], milestones: [0, 1], shape: "pulse", route: "peer-grid" }];
    }
    return peerTargets.map((peer) => fromMiner
      ? { points: [localOrigin, generator, grid, peer], milestones: [0, 0.25, 0.58, 1], shape: "pulse", route: "miner-generator-grid-peer" }
      : { points: [generator, grid, peer], milestones: [0, 0.52, 1], shape: "pulse", route: "generator-grid-peer" });
  }
  if (event.kind === "proof-admitted") {
    const proofWindow: [number, number] = [0, event.displacedVisualId || event.displacedProofId ? 0.74 : 1];
    const ejection: TraceSpec[] = event.displacedVisualId || event.displacedProofId
      ? [{
        points: [{ x: railEnd, y: rail.y }, { x: railEnd + 38, y: rail.y }],
        milestones: [0, 1],
        shape: "share",
        route: "rail-evict",
        className: "event-evicted",
        window: [0.75, 1],
        fadeOut: true
      }]
      : [];
    if (event.sourceKind === "peer") {
      const admitted: TraceSpec[] = [{
        points: [source, grid, rail, target],
        milestones: [0, 0.28, 0.67, 1],
        shape: "share",
        route: "peer-grid-rail-rank",
        window: proofWindow
      }];
      if (event.blockQuality) {
        admitted.push({
          points: [source, grid, bitcoin],
          milestones: [0, 0.46, 1],
          shape: "share",
          route: "peer-grid-bitcoin-block",
          className: "event-block-quality",
          window: proofWindow
        });
        admitted.push(...otherPeers.map((peer) => ({
          points: [source, grid, peer],
          milestones: [0, 0.46, 1],
          shape: "share" as const,
          route: "peer-grid-peer-block",
          className: "event-block-quality",
          window: proofWindow
        })));
      }
      return [...admitted, ...ejection];
    }
    const relay = peerTargets.map((peer) => fromMiner
      ? { points: [localOrigin, generator, grid, peer], milestones: [0, 0.22, 0.58, 1], shape: "share" as const, route: "miner-generator-grid-peer", window: proofWindow }
      : { points: [generator, grid, peer], milestones: [0, 0.55, 1], shape: "share" as const, route: "generator-grid-peer", window: proofWindow });
    const retained: TraceSpec = fromMiner
      ? { points: [localOrigin, generator, rail, target], milestones: [0, 0.22, 0.58, 1], shape: "share", route: "miner-generator-rail-rank", window: proofWindow }
      : { points: [generator, rail, target], milestones: [0, 0.46, 1], shape: "share", route: "generator-rail-rank", window: proofWindow };
    const blockRoute: TraceSpec[] = event.blockQuality
      ? [{
          points: fromMiner ? [localOrigin, generator, bitcoin] : [generator, bitcoin],
          milestones: fromMiner ? [0, 0.22, 1] : [0, 1],
          shape: "share",
          route: fromMiner ? "miner-generator-bitcoin-block" : "generator-bitcoin-block",
          className: "event-block-quality",
          window: proofWindow
        }]
      : [];
    return [...relay, retained, ...blockRoute, ...ejection];
  }
  if (event.kind === "peer-header") {
    return [{ points: [source, grid, bitcoin], milestones: [0, 0.48, 1], shape: "tip", route: "peer-grid-bitcoin" }];
  }
  if (event.kind === "boundary-validated") {
    return [
      ...peerTargets.map((peer) => ({
        points: [bitcoin, grid, peer],
        milestones: [0, 0.42, 1],
        shape: "tip" as const,
        route: "bitcoin-grid-peer"
      })),
      { points: [bitcoin, rail], milestones: [0, 1], shape: "tip", route: "bitcoin-rail" }
    ];
  }
  return [];
}

function TraceMarker({ trace, progress }: { trace: TraceSpec; progress: number }) {
  const traceProgress = progressInWindow(progress, trace.window);
  const position = pointAlongMilestones(trace.points, trace.milestones, traceProgress);
  const className = `event-marker event-marker-${trace.shape} ${trace.className ?? ""}`.trim();
  const hiddenBeforeWindow = trace.window ? progress < trace.window[0] : false;
  const style = hiddenBeforeWindow
    ? { opacity: 0 }
    : trace.fadeOut
      ? { opacity: 1 - traceProgress }
      : undefined;
  if (trace.shape === "pulse") {
    return <circle className={className} data-route={trace.route} cx={position.x} cy={position.y} r={4.5} style={style} />;
  }
  if (trace.shape === "tip") {
    return <rect className={className} data-route={trace.route} x={-5} y={-5} width={10} height={10} transform={`translate(${position.x} ${position.y})`} style={style} />;
  }
  if (trace.shape === "connection") {
    return <circle className={className} data-route={trace.route} cx={position.x} cy={position.y} r={2.75} style={style} />;
  }
  if (trace.className?.includes("event-block-quality")) {
    return <rect className={className} data-route={trace.route} x={-5} y={-5} width={10} height={10} transform={`translate(${position.x} ${position.y})`} style={style} />;
  }
  return (
    <line
      className={className}
      data-route={trace.route}
      x1={-7}
      y1={0}
      x2={7}
      y2={0}
      transform={`translate(${position.x} ${position.y}) rotate(${position.angle + 90})`}
      style={style}
    />
  );
}

function progressInWindow(progress: number, window?: [number, number]) {
  if (!window) return progress;
  const duration = window[1] - window[0];
  if (duration <= 0) return 1;
  return Math.min(1, Math.max(0, (progress - window[0]) / duration));
}

function useAnimatedProgress(sequence: number) {
  const [progress, setProgress] = useState(0);
  useEffect(() => {
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    if (reduced) {
      setProgress(1);
      return;
    }
    setProgress(0);
    const started = performance.now();
    let frame = 0;
    const animate = (now: number) => {
      const elapsed = Math.min(1, (now - started) / 1450);
      setProgress(1 - Math.pow(1 - elapsed, 2));
      if (elapsed < 1) frame = window.requestAnimationFrame(animate);
    };
    frame = window.requestAnimationFrame(animate);
    return () => window.cancelAnimationFrame(frame);
  }, [sequence]);
  return progress;
}

function pointAlongMilestones(points: Point[], milestones: number[], progress: number) {
  if (points.length < 2 || points.length !== milestones.length) {
    const point = points[0] ?? { x: 0, y: 0 };
    return { ...point, angle: 0 };
  }
  const bounded = Math.min(1, Math.max(0, progress));
  let segment = milestones.length - 2;
  for (let index = 0; index < milestones.length - 1; index++) {
    if (bounded <= milestones[index + 1]) {
      segment = index;
      break;
    }
  }
  const start = points[segment];
  const end = points[segment + 1];
  const duration = milestones[segment + 1] - milestones[segment];
  const ratio = duration <= 0 ? 1 : (bounded - milestones[segment]) / duration;
  return {
    x: start.x + (end.x - start.x) * ratio,
    y: start.y + (end.y - start.y) * ratio,
    angle: Math.atan2(end.y - start.y, end.x - start.x) * 180 / Math.PI
  };
}

function snapshotFlashOpacity(progress: number) {
  if (progress < 0.58) return 0;
  const phase = (progress - 0.58) / 0.42;
  return Math.max(0, Math.sin(phase * Math.PI * 4) * (1 - phase));
}

function connectionRippleProgress(progress: number) {
  return Math.min(1, Math.max(0, (progress - 0.58) / 0.42));
}

function arcPath(point: Point, radius: number, startDegrees: number, endDegrees: number) {
  const polar = (degrees: number) => {
    const radians = (degrees - 90) * Math.PI / 180;
    return {
      x: point.x + radius * Math.cos(radians),
      y: point.y + radius * Math.sin(radians)
    };
  };
  const start = polar(endDegrees);
  const end = polar(startDegrees);
  const largeArc = endDegrees - startDegrees <= 180 ? 0 : 1;
  return `M ${start.x} ${start.y} A ${radius} ${radius} 0 ${largeArc} 0 ${end.x} ${end.y}`;
}

function compactPeerLabel(value: string) {
  if (!value) return "peer";
  return value.length <= 24 ? value : `${value.slice(0, 14)}…${value.slice(-7)}`;
}

function gridHashrateLabel(diagram: DashboardDiagram) {
  const uncertainty = diagram.grid.relativeStandardErrorPercent;
  return uncertainty == null
    ? `${diagram.grid.hashrateDisplay} remote`
    : `${diagram.grid.hashrateDisplay} remote ±${uncertainty.toFixed(1)}%`;
}

function MapDetail({
  selection,
  diagram,
  operatorUnlocked,
  onClose
}: {
  selection: Selection | null;
  diagram: DashboardDiagram;
  operatorUnlocked: boolean;
  onClose: () => void;
}) {
  if (!selection) return null;
  let title = "";
  let rows: Array<[string, string]> = [];
  if (selection.kind === "proof") {
    title = `Work proof · rank ${selection.value.rank}`;
    rows = [
      ["Proof", selection.value.proofId],
      ["Payout", selection.value.address],
      ["Difficulty", selection.value.difficultyDisplay],
      ["Received", formatAge(selection.value.firstSeenUtc)],
      ["Snapshot", selection.value.locked ? "locked + provisional" : "provisional"]
    ];
  } else if (selection.kind === "peer") {
    title = selection.value.displayName || selection.value.nodeId || "GridPool peer";
    rows = [
      ["Status", selection.value.connected ? "connected" : "disconnected"],
      ["Node ID", selection.value.nodeId],
      ["Latency", selection.value.latencyMs == null ? "--" : `${selection.value.latencyMs.toFixed(0)} ms`],
      ["Transport", selection.value.transport || "unknown"],
      ["State", selection.value.stateRelation]
    ];
    if (operatorUnlocked) rows.push(["Endpoint", selection.value.endpoint || "Not published"]);
  } else if (selection.kind === "miner") {
    title = selection.value.username || "Local miner";
    rows = [
      ["Hashrate", selection.value.hashrateDisplay],
      ["Accepted", selection.value.acceptedCount.toString()],
      ["Rejected", selection.value.rejectedCount.toString()]
    ];
    if (operatorUnlocked) {
      rows.push(
        ["Payout", selection.value.address],
        ["Source", selection.value.source],
        ["Last share", formatAge(selection.value.lastShareUtc)]
      );
      if (selection.value.lastRejectionReason) rows.push(["Last rejection", selection.value.lastRejectionReason]);
    }
  } else if (selection.kind === "bitcoin") {
    title = "Local Bitcoin node";
    rows = [
      ["Tip", diagram.bitcoin.tipHeight?.toString() ?? "--"],
      ["RPC", diagram.bitcoin.reachable ? "reachable" : "unreachable"],
      ["Chain", diagram.bitcoin.synced ? "synchronized" : "not synchronized"],
      ["Difficulty", diagram.bitcoin.networkDifficultyDisplay],
      ["Network rate", diagram.bitcoin.networkHashrateDisplay],
      ["Peers", `${diagram.bitcoin.peerCount} (${diagram.bitcoin.inboundPeerCount} in / ${diagram.bitcoin.outboundPeerCount} out)`],
      ["ZMQ", diagram.bitcoin.zmqHealthy ? "healthy" : "degraded"],
      ["Mining", diagram.bitcoin.miningSafe ? "safe" : "unsafe"]
    ];
  } else if (selection.kind === "generator") {
    title = diagram.workGenerator.displayName;
    rows = [
      ["Miners", diagram.workGenerator.minerCount.toString()],
      ["Hashrate", diagram.workGenerator.hashrateDisplay],
      ["State", diagram.workGenerator.connected ? "connected" : "idle"]
    ];
  } else {
    title = "Local GridPool node";
    rows = [
      ["Remote rate", gridHashrateLabel(diagram)],
      ["Confidence", diagram.grid.confidence],
      ["Peers", diagram.peers.filter((peer) => peer.connected).length.toString()],
      ["Work Set", `${diagram.workSet.length} / 897`],
      ["Journal", `${diagram.latestSequence}`]
    ];
  }
  return (
    <aside className="map-detail" aria-label={title}>
      <button type="button" className="map-detail-close" onClick={onClose} aria-label="Close details">×</button>
      <p className="eyebrow">Live evidence</p>
      <h2>{title}</h2>
      <dl>
        {rows.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value || "--"}</dd></div>)}
      </dl>
    </aside>
  );
}

function placePeers(peers: DiagramPeer[], layout: Layout) {
  const baseRadius = layout.width < 800 ? 105 : 150;
  return peers.map((peer, index) => {
    const span = peers.length <= 1 ? 0 : index / (peers.length - 1);
    const angle = (-155 + span * 125) * Math.PI / 180;
    const latencyFactor = peer.latencyMs == null
      ? 0.4
      : Math.sqrt(Math.min(1, Math.max(0, peer.latencyMs / 2500)));
    const radius = baseRadius * (0.55 + latencyFactor * 0.67);
    return {
      peer,
      point: {
        x: layout.grid.x + Math.cos(angle) * radius,
        y: layout.grid.y + Math.sin(angle) * radius
      }
    };
  });
}

function placeBitcoinPeers(peers: DashboardDiagram["bitcoin"]["peers"], layout: Layout) {
  const baseRadius = layout.width < 800 ? 102 : 145;
  return peers.map((peer, index) => {
    const span = peers.length <= 1 ? 0.5 : index / (peers.length - 1);
    const angle = (-25 + span * 125) * Math.PI / 180;
    const latencyFactor = peer.latencyMs == null
      ? 0.4
      : Math.sqrt(Math.min(1, Math.max(0, peer.latencyMs / 2500)));
    const radius = baseRadius * (0.55 + latencyFactor * 0.67);
    return {
      peer,
      point: {
        x: layout.bitcoin.x + Math.cos(angle) * radius,
        y: layout.bitcoin.y + Math.sin(angle) * radius
      }
    };
  });
}

function placeMiners(miners: DiagramMiner[], layout: Layout) {
  const radius = layout.width < 800 ? 150 : 165;
  return miners.map((miner, index) => {
    const span = miners.length <= 1 ? 0.5 : index / (miners.length - 1);
    const angle = (55 + span * 70) * Math.PI / 180;
    return {
      miner,
      point: {
        x: layout.generator.x + Math.cos(angle) * radius,
        y: layout.generator.y + Math.sin(angle) * radius
      }
    };
  });
}

function rankX(rank: number, layout: Layout) {
  const bounded = Math.min(897, Math.max(1, rank));
  if (bounded <= 300) {
    return layout.railStart + ((bounded - 1) / 299) * (layout.rail.x - layout.railStart);
  }
  return layout.rail.x + ((bounded - 300) / 597) * (layout.railEnd - layout.rail.x);
}

function eventSource(
  event: DiagramEvent | null,
  peers: ReturnType<typeof placePeers>,
  bitcoinPeers: ReturnType<typeof placeBitcoinPeers>,
  miners: ReturnType<typeof placeMiners>,
  layout: Layout
) {
  if (!event) return layout.grid;
  if (event.sourceKind === "peer") {
    return peers.find(({ peer }) =>
      peer.visualId === event.sourceVisualId ||
      peer.visualId === event.visualId ||
      peer.nodeId === event.sourceId ||
      peer.endpoint === event.sourceId
    )?.point ?? peers[0]?.point ?? layout.grid;
  }
  if (event.sourceKind === "bitcoin-peer") {
    return bitcoinPeers.find(({ peer }) => peer.visualId === event.sourceVisualId)?.point ?? layout.bitcoin;
  }
  if (event.sourceKind === "miner") {
    return miners.find(({ miner }) =>
      miner.visualId === event.sourceVisualId ||
      miner.visualId === event.visualId ||
      miner.username === event.sourceId
    )?.point ?? layout.generator;
  }
  return layout.generator;
}

function freshness(timestamp: string | null) {
  if (!timestamp) return 0.28;
  const ageSeconds = Math.max(0, (Date.now() - Date.parse(timestamp)) / 1000);
  return Math.max(0.24, Math.exp(-ageSeconds / 180));
}

function isRecent(timestamp: string | null, maximumAgeSeconds: number) {
  if (!timestamp) return false;
  return Date.now() - Date.parse(timestamp) <= maximumAgeSeconds * 1000;
}

function describeEvent(event: DiagramEvent) {
  switch (event.kind) {
    case "proof-admitted": return `${event.sourceKind === "peer" ? "Peer" : "Local"} ${event.blockQuality ? "block-quality " : ""}proof admitted at Work Set rank ${event.rank ?? "unknown"}${event.displacedVisualId ? "; rank 897 displaced" : ""}.`;
    case "local-miner-activity": return "Periodic local vardiff share reached the work generator.";
    case "peer-connection": return `Peer ${event.connected ? "connected" : "disconnected"}.`;
    case "peer-header": return "Provisional peer header received; awaiting local Bitcoin validation.";
    case "boundary-validated": return `Local Bitcoin boundary ${event.blockHeight ?? ""} validated and snapshot activated.`;
    case "pulse-accepted": return event.sourceKind === "peer"
      ? "Peer pulse reached the local GridPool node."
      : "Local pulse relayed to connected GridPool peers.";
    case "proof-rejected": return `${event.sourceKind === "peer" ? "Peer" : "Local"} proof rejected${event.category ? `: ${event.category}` : "."}`;
    case "peer-transport": return `Peer transport changed from ${event.previousValue || "unknown"} to ${event.currentValue || "unknown"}.`;
    case "peer-state": return `Peer state relation changed to ${event.currentValue || "unknown"}.`;
    case "peer-header-rejected": return "Peer header was rejected by local validation.";
    case "sibling-merge": return `Compatible sibling work merged into the local reserve (${event.count || 0} proofs observed).`;
    case "chain-reorganization": return `Bitcoin reorganization replaced ${event.count || 1} block boundary.`;
    case "mining-safety": return event.safe ? "Local Bitcoin authority recovered; mining work is safe." : "Local Bitcoin authority degraded; fresh mining work is unsafe.";
    case "bitcoin-peer-connection": return `Bitcoin peer ${event.connected ? "connected" : "disconnected"}.`;
  }
}
