import { render, screen, within } from "@testing-library/react";
import { dashboardModules } from ".";
import { summaryFixture } from "../test/fixture";
import type { DashboardModuleContext } from "./context";

const context: DashboardModuleContext = {
  summary: summaryFixture,
  history: null,
  operator: null,
  adminKey: "",
  window: "24h",
  setWindow: () => undefined,
  requestOperatorUnlock: () => undefined,
  addressResult: null,
  addressLoading: false,
  addressError: "",
  lookupAddress: async () => undefined
};

function renderModule(id: string) {
  const module = dashboardModules.find((candidate) => candidate.id === id);
  if (!module) throw new Error(`Missing module ${id}`);
  return render(<>{module.render(context)}</>);
}

describe("V2.2 dashboard language", () => {
  it("reserves locked terminology for the active payout snapshot", () => {
    renderModule("snapshot");
    expect(screen.getByText("Active payout snapshot")).toBeInTheDocument();
    expect(screen.getByText("Locked for current templates")).toBeInTheDocument();
  });

  it("describes the Work Set as provisional", () => {
    const view = renderModule("reserve");
    expect(screen.getByText("Unpaid Work Set")).toBeInTheDocument();
    expect(screen.getByText("Provisional / difficulty ranked")).toBeInTheDocument();
    expect(within(view.container).queryByText(/^locked$/i)).not.toBeInTheDocument();
  });

  it("labels the order-statistic estimate as non-consensus", () => {
    renderModule("work-rate");
    expect(screen.getByText("Observed team work rate")).toBeInTheDocument();
    expect(screen.getByText("Order statistic / non-consensus")).toBeInTheDocument();
    expect(screen.getByText("±3.3% RSE")).toBeInTheDocument();
  });
});
