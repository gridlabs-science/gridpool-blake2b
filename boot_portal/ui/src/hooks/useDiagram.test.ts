import { act, renderHook } from "@testing-library/react";
import { diagramFixture, summaryFixture } from "../test/fixture";
import type { DashboardChanged } from "../types";

const signalR = vi.hoisted(() => {
  const handlers = new Map<string, (change: DashboardChanged) => void>();
  const connection = {
    state: "Disconnected",
    on: vi.fn((name: string, handler: (change: DashboardChanged) => void) => handlers.set(name, handler)),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    start: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined)
  };
  return { handlers, connection };
});

const api = vi.hoisted(() => ({
  summary: vi.fn(),
  diagram: vi.fn(),
  diagramHistory: vi.fn(),
  diagramEvents: vi.fn()
}));

vi.mock("@microsoft/signalr", () => ({
  HubConnectionState: { Disconnected: "Disconnected" },
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() { return signalR.connection; }
  }
}));

vi.mock("../api", () => ({ dashboardApi: api }));

import { useDiagram } from "./useDiagram";

describe("useDiagram request coordination", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    signalR.handlers.clear();
    signalR.connection.on.mockClear();
    signalR.connection.start.mockClear();
    api.summary.mockReset().mockResolvedValue(summaryFixture);
    api.diagram.mockReset().mockResolvedValue(diagramFixture);
    api.diagramHistory.mockReset().mockResolvedValue({
      schemaVersion: 1,
      window: "24h",
      generatedAtUtc: "2026-07-29T12:00:00Z",
      redacted: true,
      slotZeroAddress: "tb1qhome",
      bestDifficulty: null,
      bestDifficultyDisplay: "--",
      proofs: []
    });
    api.diagramEvents.mockReset().mockResolvedValue({
      schemaVersion: 2,
      generatedAtUtc: "2026-07-29T12:00:00Z",
      redacted: true,
      oldestSequence: 9,
      latestSequence: 8,
      nextSequence: 8,
      hasMore: false,
      gap: false,
      events: []
    });
  });

  afterEach(() => vi.useRealTimers());

  it("uses one connection and coalesces invalidation storms below the read limit", async () => {
    const hook = renderHook(() => useDiagram(""));
    await act(async () => Promise.resolve());
    await act(async () => Promise.resolve());
    await act(async () => vi.advanceTimersByTimeAsync(0));

    expect(signalR.connection.start).toHaveBeenCalledOnce();
    expect(api.diagram).toHaveBeenCalledOnce();
    expect(api.diagramHistory).toHaveBeenCalledOnce();
    expect(api.summary).toHaveBeenCalledOnce();

    api.diagram.mockClear();
    api.diagramHistory.mockClear();
    api.summary.mockClear();
    api.diagramEvents.mockClear();
    const changed = signalR.handlers.get("DashboardChanged");
    expect(changed).toBeDefined();

    act(() => {
      for (let index = 0; index < 30; index++) {
        changed?.({ revision: index + 1, timestampUtc: "2026-07-29T12:00:00Z", topics: ["miners", "diagram"] });
      }
    });
    await act(async () => vi.advanceTimersByTimeAsync(1_000));

    expect(api.diagramEvents).toHaveBeenCalledOnce();
    expect(api.diagram).toHaveBeenCalledOnce();
    expect(api.summary).toHaveBeenCalledOnce();
    expect(api.diagramHistory).not.toHaveBeenCalled();

    act(() => {
      for (let index = 0; index < 30; index++) changed?.({
        revision: 100 + index,
        timestampUtc: "2026-07-29T12:00:01Z",
        topics: ["miners", "diagram"]
      });
    });
    await act(async () => vi.advanceTimersByTimeAsync(1_000));

    expect(api.diagramEvents).toHaveBeenCalledTimes(2);
    expect(api.diagram).toHaveBeenCalledTimes(2);
    expect(api.summary).toHaveBeenCalledOnce();
    expect(api.diagramHistory).not.toHaveBeenCalled();
    hook.unmount();
  });
});
