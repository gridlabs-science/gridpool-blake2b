import { fireEvent, render, screen } from "@testing-library/react";
import { SystemMap } from "./SystemMap";
import { diagramFixture } from "../test/fixture";
import type { DiagramEvent } from "../types";

const proofEvent: DiagramEvent = {
  sequence: 91,
  timestampUtc: "2026-07-29T16:00:05Z",
  kind: "proof-admitted",
  sourceKind: "peer",
  sourceId: "",
  sourceVisualId: diagramFixture.peers[0].visualId,
  visualId: diagramFixture.peers[0].visualId,
  transport: "",
  proofId: "",
  address: "",
  difficulty: null,
  blockQuality: false,
  receivedUtc: "2026-07-29T16:00:05Z",
  validatedUtc: "2026-07-29T16:00:05Z",
  mutatedUtc: "2026-07-29T16:00:05Z",
  rank: 120,
  displacedProofId: "",
  displacedVisualId: "",
  connected: null,
  latencyMs: null,
  acceptedShareDelta: null,
  hashrateThs: null,
  blockHash: "",
  blockHeight: null,
  snapshotId: "",
  lockedProofIds: [],
  lockedVisualIds: []
};

describe("GridPool system map", () => {
  it("renders the exact rail and verified slot zero evidence", () => {
    const { container } = render(
      <SystemMap
        diagram={diagramFixture}
        activeEvent={null}
        onEventComplete={() => undefined}
        operatorUnlocked
      />
    );

    expect(container.querySelectorAll(".proof-tick")).toHaveLength(897);
    expect(screen.getByText(/Slot 0 · tb1qhome/)).toBeInTheDocument();
    expect(container.querySelector(".rail-line")).toHaveAttribute("x1", "320");
    expect(container.querySelector(".rail-line")).toHaveAttribute("x2", "1159");
    expect(container.querySelector(".snapshot-line")).toHaveAttribute("x2", "600");
    expect(container.querySelectorAll(".proof-tick")[299]).toHaveAttribute("x1", "600");
    expect(screen.getByText(/Slot 0 · tb1qhome/)).toHaveAttribute("transform", expect.stringContaining("rotate(90"));
  });

  it("navigates proof ranks as one keyboard composite", () => {
    render(
      <SystemMap
        diagram={diagramFixture}
        activeEvent={null}
        onEventComplete={() => undefined}
        operatorUnlocked
      />
    );

    const rail = screen.getByRole("listbox");
    fireEvent.keyDown(rail, { key: "End" });
    expect(rail).toHaveAttribute("aria-activedescendant", "proof-proof-897");
    fireEvent.keyDown(rail, { key: "Home" });
    expect(rail).toHaveAttribute("aria-activedescendant", "proof-proof-1");
  });

  it("reveals learning labels only when help is active", () => {
    render(
      <SystemMap
        diagram={diagramFixture}
        activeEvent={null}
        onEventComplete={() => undefined}
        operatorUnlocked
      />
    );

    expect(screen.queryByText("Bitcoin")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Help" }));
    expect(screen.getByText("Bitcoin")).toBeInTheDocument();
  });

  it("shows compact honest hashrate cues and public peer names without help", () => {
    const { container } = render(
      <SystemMap
        diagram={diagramFixture}
        activeEvent={null}
        onEventComplete={() => undefined}
        operatorUnlocked={false}
      />
    );

    expect(screen.getByText("Dallas")).toBeInTheDocument();
    expect(screen.getByText(/1.2 PH\/s remote/)).toBeInTheDocument();
    expect(screen.getByText("1.2 PH/s local")).toBeInTheDocument();
    expect(screen.getByText("400 TH/s")).toBeInTheDocument();
    expect(screen.getByText("129 T diff")).toBeInTheDocument();
    expect(container.querySelectorAll(".hashrate-arc")).toHaveLength(3);
    expect(container.querySelector(".target-aperture")).toBeInTheDocument();
    expect(screen.getByText(/1.2 PH\/s remote/)).toHaveAttribute("y", "175");
    expect(screen.getByText("129 T diff")).toHaveAttribute("y", "175");
  });

  it("uses unmarked convergence points and renders journal motion itself", () => {
    const { container } = render(
      <SystemMap
        diagram={diagramFixture}
        activeEvent={proofEvent}
        onEventComplete={() => undefined}
        operatorUnlocked={false}
      />
    );

    expect(container.querySelectorAll(".node-hit")).toHaveLength(3);
    expect(container.querySelector(".node-mark rect")).toBeNull();
    expect(container.querySelector(".event-marker")).toBeInTheDocument();
    expect(container.querySelector(".event-marker")).toHaveAttribute("data-route", "peer-grid-rail-rank");
    expect(container.querySelector(".event-marker")?.tagName).toBe("line");
    expect(container.querySelector("animateMotion")).toBeNull();
  });

  it("branches block-quality work to Bitcoin and relays it to other peers", () => {
    const blockDiagram = {
      ...diagramFixture,
      peers: [
        ...diagramFixture.peers,
        { ...diagramFixture.peers[0], visualId: "peer-detroit", nodeId: "detroit", latencyMs: 22 }
      ]
    };
    const { container } = render(
      <SystemMap
        diagram={blockDiagram}
        activeEvent={{ ...proofEvent, blockQuality: true, displacedVisualId: "evicted-proof" }}
        onEventComplete={() => undefined}
        operatorUnlocked={false}
      />
    );

    const routes = Array.from(container.querySelectorAll(".event-marker"), (marker) => marker.getAttribute("data-route"));
    expect(routes).toContain("peer-grid-rail-rank");
    expect(routes).toContain("peer-grid-bitcoin-block");
    expect(routes).toContain("peer-grid-peer-block");
    expect(routes).toContain("rail-evict");
    expect(container.querySelectorAll(".event-block-quality").length).toBeGreaterThan(1);
    expect(Array.from(container.querySelectorAll(".event-block-quality")).every((marker) => marker.tagName === "rect")).toBe(true);
    expect(container.querySelector('[data-route="peer-grid-rail-rank"]')?.tagName).toBe("line");
  });

  it("uses one latency scale so faster GridPool peers sit closer", () => {
    const { container } = render(
      <SystemMap
        diagram={{
          ...diagramFixture,
          peers: [
            { ...diagramFixture.peers[0], visualId: "fast", latencyMs: 10 },
            { ...diagramFixture.peers[0], visualId: "slow", nodeId: "slow", latencyMs: 100 }
          ]
        }}
        activeEvent={null}
        onEventComplete={() => undefined}
        operatorUnlocked={false}
      />
    );

    const links = Array.from(container.querySelectorAll(".peer-constellation .map-link"));
    const length = (link: Element) => Math.hypot(
      Number(link.getAttribute("x2")) - Number(link.getAttribute("x1")),
      Number(link.getAttribute("y2")) - Number(link.getAttribute("y1"))
    );
    expect(length(links[0])).toBeLessThan(length(links[1]));
  });
});
