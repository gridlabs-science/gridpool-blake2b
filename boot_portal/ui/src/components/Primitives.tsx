import type { PropsWithChildren, ReactNode } from "react";

export function Card({
  title,
  eyebrow,
  action,
  className = "",
  children
}: PropsWithChildren<{
  title: string;
  eyebrow?: string;
  action?: ReactNode;
  className?: string;
}>) {
  return (
    <section className={`card ${className}`}>
      <header className="card-header">
        <div>
          {eyebrow ? <p className="eyebrow">{eyebrow}</p> : null}
          <h2>{title}</h2>
        </div>
        {action ? <div className="card-action">{action}</div> : null}
      </header>
      {children}
    </section>
  );
}

export function Metric({
  label,
  value,
  detail,
  tone = "neutral"
}: {
  label: string;
  value: ReactNode;
  detail?: ReactNode;
  tone?: "neutral" | "good" | "warn" | "bad" | "testnet";
}) {
  return (
    <div className={`metric metric-${tone}`}>
      <span className="metric-label">{label}</span>
      <strong>{value}</strong>
      {detail ? <span className="metric-detail">{detail}</span> : null}
    </div>
  );
}

export function StatusDot({
  status
}: {
  status: "ready" | "degraded" | "unsafe" | "unknown";
}) {
  return <span className={`status-dot status-${status}`} aria-hidden="true" />;
}

export function HashValue({ value }: { value: string | null | undefined }) {
  if (!value) {
    return <span className="hash muted">--</span>;
  }
  const compact = value.length > 18 ? `${value.slice(0, 9)}…${value.slice(-7)}` : value;
  return (
    <span className="hash" title={value}>
      {compact}
    </span>
  );
}

export function Progress({
  value,
  maximum,
  label
}: {
  value: number;
  maximum: number;
  label: string;
}) {
  const ratio = maximum > 0 ? Math.min(1, Math.max(0, value / maximum)) : 0;
  return (
    <div className="progress-wrap">
      <div className="progress-label">
        <span>{label}</span>
        <span>{Math.round(ratio * 100)}%</span>
      </div>
      <div
        className="progress-track"
        role="progressbar"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={maximum}
        aria-valuenow={value}
      >
        <span style={{ width: `${ratio * 100}%` }} />
      </div>
    </div>
  );
}

export function EmptyState({ children }: PropsWithChildren) {
  return <div className="empty-state">{children}</div>;
}
