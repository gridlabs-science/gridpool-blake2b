import type {
  DashboardAddress,
  DashboardHistory,
  DashboardOperator,
  DashboardSummary,
  WindowKey
} from "../types";

export interface DashboardModuleContext {
  summary: DashboardSummary;
  history: DashboardHistory | null;
  operator: DashboardOperator | null;
  adminKey: string;
  window: WindowKey;
  setWindow: (window: WindowKey) => void;
  requestOperatorUnlock: () => void;
  addressResult: DashboardAddress | null;
  addressLoading: boolean;
  addressError: string;
  lookupAddress: (address: string) => Promise<void>;
}
