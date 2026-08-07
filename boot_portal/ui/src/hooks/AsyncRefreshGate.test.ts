import { act } from "@testing-library/react";
import { AsyncRefreshGate } from "./AsyncRefreshGate";

describe("AsyncRefreshGate", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("coalesces bursts and respects the minimum interval", async () => {
    const action = vi.fn(async () => undefined);
    const gate = new AsyncRefreshGate(action, 1_000);

    gate.request();
    gate.request();
    gate.request();
    await act(async () => vi.advanceTimersByTimeAsync(0));
    expect(action).toHaveBeenCalledTimes(1);

    gate.request();
    gate.request();
    await act(async () => vi.advanceTimersByTimeAsync(999));
    expect(action).toHaveBeenCalledTimes(1);
    await act(async () => vi.advanceTimersByTimeAsync(1));
    expect(action).toHaveBeenCalledTimes(2);
    gate.dispose();
  });

  it("runs once more when requests arrive during an in-flight refresh", async () => {
    let release!: () => void;
    const action = vi.fn(() => new Promise<void>((resolve) => { release = resolve; }));
    const gate = new AsyncRefreshGate(action, 100);

    gate.request();
    await act(async () => vi.advanceTimersByTimeAsync(0));
    gate.request();
    gate.request();
    release();
    await act(async () => Promise.resolve());
    await act(async () => vi.advanceTimersByTimeAsync(100));
    expect(action).toHaveBeenCalledTimes(2);
    gate.dispose();
  });
});
