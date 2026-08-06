import { HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr";
import { useEffect, useEffectEvent, useRef, useState } from "react";
import { dashboardApi } from "../api";
import type { DashboardChanged, DashboardDiagram, DiagramEvent } from "../types";

const maximumQueuedEvents = 40;

export function useDiagram(adminKey: string) {
  const [diagram, setDiagram] = useState<DashboardDiagram | null>(null);
  const [events, setEvents] = useState<DiagramEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [stale, setStale] = useState(false);
  const [error, setError] = useState("");
  const cursor = useRef(0);

  const refreshSnapshot = useEffectEvent(async () => {
    try {
      const snapshot = await dashboardApi.diagram(adminKey || undefined);
      cursor.current = snapshot.latestSequence;
      setDiagram(snapshot);
      setLoading(false);
      setStale(false);
      setError("");
    } catch (reason) {
      setLoading(false);
      setStale(diagram !== null);
      setError(reason instanceof Error ? reason.message : "Diagram data is unavailable.");
    }
  });

  const drainEvents = useEffectEvent(async () => {
    try {
      let pages = 0;
      let page = await dashboardApi.diagramEvents(cursor.current, adminKey || undefined);
      if (page.gap) {
        setEvents([]);
        await refreshSnapshot();
        return;
      }
      const incoming: DiagramEvent[] = [];
      while (true) {
        incoming.push(...page.events);
        cursor.current = page.nextSequence;
        pages++;
        if (!page.hasMore || pages >= 4) break;
        page = await dashboardApi.diagramEvents(cursor.current, adminKey || undefined);
        if (page.gap) {
          setEvents([]);
          await refreshSnapshot();
          return;
        }
      }
      if (incoming.length) {
        setEvents((current) => [...current, ...incoming].slice(-maximumQueuedEvents));
        setDiagram(await dashboardApi.diagram(adminKey || undefined));
      }
      if (page.hasMore) {
        setEvents([]);
        await refreshSnapshot();
      }
      setStale(false);
      setError("");
    } catch (reason) {
      setStale(diagram !== null);
      setError(reason instanceof Error ? reason.message : "Live diagram events are unavailable.");
    }
  });

  useEffect(() => {
    cursor.current = 0;
    setEvents([]);
    void refreshSnapshot();
    const poll = window.setInterval(() => {
      void drainEvents();
    }, 15_000);
    const connection = new HubConnectionBuilder()
      .withUrl("/dashboardHub")
      .withAutomaticReconnect([0, 1000, 3000, 10_000])
      .build();
    connection.on("DashboardChanged", (change: DashboardChanged) => {
      if (change.topics.includes("diagram")) void drainEvents();
      else if (change.topics.some((topic) =>
        ["status", "snapshot", "reserve", "network", "miners", "scenario", "timeline"].includes(topic))) {
        void refreshSnapshot();
      }
    });
    connection.onreconnecting(() => setStale(diagram !== null));
    connection.onreconnected(() => void refreshSnapshot());
    void connection.start().catch(() => setStale(diagram !== null));
    return () => {
      window.clearInterval(poll);
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop();
    };
  }, [adminKey]);

  return {
    diagram,
    events,
    activeEvent: events[0] ?? null,
    acknowledgeEvent: () => setEvents((current) => current.slice(1)),
    loading,
    stale,
    error,
    refresh: refreshSnapshot
  };
}
