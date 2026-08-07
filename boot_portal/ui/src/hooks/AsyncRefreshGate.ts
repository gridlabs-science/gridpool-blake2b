export class AsyncRefreshGate {
  private readonly action: () => Promise<void>;
  private readonly minimumIntervalMs: number;
  private timer: number | null = null;
  private running = false;
  private pending = false;
  private lastStartedAt = Number.NEGATIVE_INFINITY;
  private disposed = false;

  constructor(action: () => Promise<void>, minimumIntervalMs: number) {
    this.action = action;
    this.minimumIntervalMs = Math.max(0, minimumIntervalMs);
  }

  request(immediate = false) {
    if (this.disposed) return;
    this.pending = true;
    if (this.running || this.timer !== null) return;
    const elapsed = performance.now() - this.lastStartedAt;
    const delay = immediate ? 0 : Math.max(0, this.minimumIntervalMs - elapsed);
    this.timer = window.setTimeout(() => {
      this.timer = null;
      void this.run();
    }, delay);
  }

  dispose() {
    this.disposed = true;
    this.pending = false;
    if (this.timer !== null) window.clearTimeout(this.timer);
    this.timer = null;
  }

  private async run() {
    if (this.disposed || this.running || !this.pending) return;
    this.pending = false;
    this.running = true;
    this.lastStartedAt = performance.now();
    try {
      await this.action();
    } finally {
      this.running = false;
      if (this.pending && !this.disposed) this.request();
    }
  }
}
