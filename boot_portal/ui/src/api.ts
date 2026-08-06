import type {
  DashboardAddress,
  DashboardDiagram,
  DiagramEventPage,
  DashboardHistory,
  DashboardOperator,
  DashboardSummary,
  WindowKey
} from "./types";

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(path: string, adminKey?: string): Promise<T> {
  const headers = new Headers({ Accept: "application/json" });
  if (adminKey) {
    headers.set("X-Boot-Admin-Key", adminKey);
  }
  const response = await fetch(path, {
    headers,
    cache: "no-store",
    credentials: "same-origin"
  });
  if (!response.ok) {
    let reason = `${response.status} ${response.statusText}`;
    try {
      const payload = (await response.json()) as { reason?: string; message?: string };
      reason = payload.reason ?? payload.message ?? reason;
    } catch {
      // Keep the HTTP status when the response is not JSON.
    }
    throw new ApiError(response.status, reason);
  }
  return (await response.json()) as T;
}

export const dashboardApi = {
  summary: (window: WindowKey) =>
    request<DashboardSummary>(`/api/dashboard/v1/summary?window=${window}`),
  history: (window: WindowKey) =>
    request<DashboardHistory>(`/api/dashboard/v1/history?window=${window}`),
  address: (address: string) =>
    request<DashboardAddress>(`/api/dashboard/v1/address/${encodeURIComponent(address)}`),
  operator: (adminKey: string) =>
    request<DashboardOperator>("/api/dashboard/v1/operator", adminKey),
  diagram: (adminKey?: string) =>
    adminKey
      ? request<DashboardDiagram>("/api/dashboard/v1/diagram/operator", adminKey)
      : request<DashboardDiagram>("/api/dashboard/v1/diagram"),
  diagramEvents: (after: number, adminKey?: string) =>
    adminKey
      ? request<DiagramEventPage>(
          `/api/dashboard/v1/diagram/operator/events?after=${after}&limit=256`,
          adminKey
        )
      : request<DiagramEventPage>(
          `/api/dashboard/v1/diagram/events?after=${after}&limit=256`
        ),
  raw: <T>(path: string, adminKey?: string) => request<T>(path, adminKey)
};
