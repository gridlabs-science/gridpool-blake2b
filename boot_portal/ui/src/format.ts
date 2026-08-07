export function formatDate(value: string | null | undefined): string {
  if (!value) return "--";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "--";
  return new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(date);
}

export function formatAge(value: string | null | undefined): string {
  if (!value) return "--";
  const seconds = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
  return `${Math.floor(seconds / 86400)}d ago`;
}

export function formatPercent(value: number | null | undefined, digits = 1): string {
  return value == null || !Number.isFinite(value) ? "--" : `${(value * 100).toFixed(digits)}%`;
}

export function formatUncertainty(value: number | null | undefined): string {
  return value == null || !Number.isFinite(value) ? "--" : `±${value.toFixed(1)}% RSE`;
}

export function formatSats(value: number): string {
  return new Intl.NumberFormat().format(value);
}
