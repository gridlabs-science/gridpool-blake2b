import { expect, test } from "@playwright/test";

test.beforeEach(async ({ request }) => {
  await request.post("/__sim/api/v1/scenarios/healthy-mesh/load");
});

test("desktop controls update two synchronized dashboard observers", async ({
  browser,
  page
}, testInfo) => {
  test.skip(testInfo.project.name !== "desktop", "Desktop control test.");
  const second = await browser.newPage();
  await Promise.all([
    page.goto("/dashboard/"),
    second.goto("/dashboard/")
  ]);
  await expect(page.locator(".truth-bar")).toContainText("4 peers");
  await expect(second.locator(".truth-bar")).toContainText("4 peers");

  const response = await page.request.post("/__sim/api/v1/actions", {
    data: { action: "peer.disconnect", peer: "dallas" }
  });
  expect(response.ok()).toBeTruthy();

  await expect(page.locator(".truth-bar")).toContainText("3 peers");
  await expect(second.locator(".truth-bar")).toContainText("3 peers");
  await second.close();
});

test("control lab embeds the real dashboard", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "desktop", "Desktop control test.");
  await page.goto("/__sim/");
  await expect(page.getByRole("heading", { name: "Dashboard state laboratory" })).toBeVisible();
  const frame = page.frameLocator('iframe[title="Synthetic GridPool dashboard"]');
  await expect(frame.getByRole("img", { name: "GridPool node wiring diagram" })).toBeVisible();
  await page.getByLabel("Scenario").selectOption("stale-local-node");
  await expect(frame.locator(".truth-state")).toContainText("unsafe");
});

test("living minute animates a stepped journal event", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "desktop", "Desktop animation test.");
  await page.request.post("/__sim/api/v1/scenarios/living-minute-c/load");
  await page.request.post("/__sim/api/v1/timeline/pause");
  await page.request.post("/__sim/api/v1/timeline/reset");
  await page.goto("/dashboard/");
  await expect(page.getByText("897 / 897 proofs")).toBeVisible();

  const response = await page.request.post("/__sim/api/v1/timeline/step");
  expect(response.ok()).toBeTruthy();
  const marker = page.locator('[data-route="peer-grid-rail-rank"]');
  await expect(marker).toHaveCount(1);
  await expect(marker).toHaveAttribute("data-route", "peer-grid-rail-rank");
  const start = await marker.getAttribute("transform");
  await page.waitForTimeout(250);
  await expect(marker).not.toHaveAttribute("transform", start ?? "");
});

test("living minute routes each event through the system diagram", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "desktop", "Desktop animation test.");
  test.setTimeout(75_000);
  await page.request.post("/__sim/api/v1/scenarios/living-minute-c/load");
  await page.request.post("/__sim/api/v1/timeline/pause");
  await page.request.post("/__sim/api/v1/timeline/reset");
  await page.goto("/dashboard/");
  await expect(page.getByText("897 / 897 proofs")).toBeVisible();

  const stepAndExpectRoutes = async (expected: string[]) => {
    await page.request.post("/__sim/api/v1/timeline/step");
    const markers = page.locator(".event-marker");
    await expect(markers).toHaveCount(expected.length);
    expect(await markers.evaluateAll((items) => items.map((item) => item.getAttribute("data-route")))).toEqual(expected);
    await expect(markers).toHaveCount(0, { timeout: 3_000 });
  };

  await stepAndExpectRoutes(["peer-grid-rail-rank", "rail-evict"]);
  await stepAndExpectRoutes(["miner-generator-rejected"]);
  await stepAndExpectRoutes(["peer-grid"]);
  await stepAndExpectRoutes(["miner-generator", "miner-generator", "miner-generator"]);
  await stepAndExpectRoutes(["grid-peer-transport"]);

  await page.request.post("/__sim/api/v1/timeline/step");
  await expect(page.locator(".foundation-unsafe")).toHaveCount(1);
  await page.waitForTimeout(2_100);
  await page.request.post("/__sim/api/v1/timeline/step");
  await expect(page.locator(".foundation-unsafe")).toHaveCount(0);
  await page.waitForTimeout(2_100);

  await stepAndExpectRoutes(["peer-grid-rail-rank", "rail-evict"]);
  await stepAndExpectRoutes([
    "miner-generator-grid-peer",
    "miner-generator-grid-peer",
    "miner-generator-grid-peer",
    "miner-generator-rail-rank",
    "rail-evict"
  ]);
  await stepAndExpectRoutes(["peer-grid-bitcoin-rejected"]);
  await stepAndExpectRoutes(["peer-grid-disconnect"]);
  await stepAndExpectRoutes(["grid-peer-connect"]);
  await stepAndExpectRoutes(["peer-grid-rail-sibling"]);
  await stepAndExpectRoutes(["grid-peer-state"]);
  await stepAndExpectRoutes([
    "miner-generator-grid-peer",
    "miner-generator-grid-peer",
    "miner-generator-grid-peer"
  ]);
  await stepAndExpectRoutes(["grid-peer-state"]);

  await stepAndExpectRoutes(["peer-grid-bitcoin"]);
  await stepAndExpectRoutes(["bitcoin-peer-disconnect"]);
  await stepAndExpectRoutes(["bitcoin-peer-connect"]);
  await page.request.post("/__sim/api/v1/timeline/step");
  const boundaryMarkers = page.locator(".event-marker");
  await expect(boundaryMarkers).toHaveCount(4);
  expect(await boundaryMarkers.evaluateAll((items) => items.map((item) => item.getAttribute("data-route")))).toEqual([
    "bitcoin-grid-peer",
    "bitcoin-grid-peer",
    "bitcoin-grid-peer",
    "bitcoin-rail"
  ]);
  await expect(page.locator(".snapshot-flash")).toHaveAttribute("style", /opacity:/);
  await expect(boundaryMarkers).toHaveCount(0, { timeout: 3_000 });

  await stepAndExpectRoutes(["rail-bitcoin-reorg", "bitcoin-rail-replacement"]);

  await stepAndExpectRoutes([
    "miner-generator-grid-peer",
    "miner-generator-grid-peer",
    "miner-generator-grid-peer",
    "miner-generator-rail-rank",
    "miner-generator-bitcoin-block",
    "rail-evict"
  ]);
  await page.request.post("/__sim/api/v1/timeline/step");
  await expect(page.locator(".paid-snapshot-drain")).toHaveCount(1);
  await expect(page.locator(".event-marker")).toHaveCount(0, { timeout: 3_000 });
  await stepAndExpectRoutes([
    "miner-generator-grid-peer",
    "miner-generator-grid-peer",
    "miner-generator-grid-peer"
  ]);
});

test("mobile observer is responsive and has no simulator controls", async ({
  page
}, testInfo) => {
  test.skip(testInfo.project.name !== "mobile-observer", "Mobile observer test.");
  await page.goto("/dashboard/");
  await expect(page.locator(".truth-bar")).toBeVisible();
  await expect(page.getByRole("img", { name: "GridPool node wiring diagram" })).toBeVisible();
  await expect(page.getByText("Dashboard state laboratory")).toHaveCount(0);
});
