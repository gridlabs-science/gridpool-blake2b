import { HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr";
import { useEffect, useEffectEvent, useState } from "react";
import { dashboardApi } from "../api";
import type {
  DashboardChanged,
  DashboardState,
  WindowKey
} from "../types";

const initialState: DashboardState = {
  summary: null,
  history: null,
  operator: null,
  loading: true,
  stale: false,
  error: "",
  lastUpdatedUtc: null
};

export function useDashboard(windowKey: WindowKey, adminKey: string) {
  const [state, setState] = useState<DashboardState>(initialState);

  const refresh = useEffectEvent(async (includeHistory = false) => {
    try {
      const [summary, history, operator] = await Promise.all([
        dashboardApi.summary(windowKey),
        includeHistory ? dashboardApi.history(windowKey) : Promise.resolve(state.history),
        adminKey ? dashboardApi.operator(adminKey) : Promise.resolve(null)
      ]);
      setState((current) => ({
        ...current,
        summary,
        history,
        operator,
        loading: false,
        stale: false,
        error: "",
        lastUpdatedUtc: new Date().toISOString()
      }));
    } catch (error) {
      setState((current) => ({
        ...current,
        loading: false,
        stale: current.summary !== null,
        error: error instanceof Error ? error.message : "Dashboard data is unavailable."
      }));
    }
  });

  useEffect(() => {
    void refresh(true);
    const poll = window.setInterval(() => void refresh(false), 15_000);
    const connection = new HubConnectionBuilder()
      .withUrl("/dashboardHub")
      .withAutomaticReconnect([0, 1000, 3000, 10_000])
      .build();
    connection.on("DashboardChanged", (_change: DashboardChanged) => {
      void refresh(false);
    });
    connection.onreconnecting(() => {
      setState((current) => ({ ...current, stale: current.summary !== null }));
    });
    connection.onreconnected(() => void refresh(true));
    void connection.start().catch(() => {
      setState((current) => ({ ...current, stale: current.summary !== null }));
    });

    return () => {
      window.clearInterval(poll);
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop();
      }
    };
  }, [windowKey, adminKey]);

  return {
    ...state,
    refresh: () => refresh(true)
  };
}
