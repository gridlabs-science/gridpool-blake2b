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
  await expect(frame.getByText("Verifiable work. No pool wallet.")).toBeVisible();
  await page.getByRole("combobox").selectOption("stale-local-node");
  await expect(frame.locator(".hero-state")).toContainText("unsafe");
});

test("mobile observer is responsive and has no simulator controls", async ({
  page
}, testInfo) => {
  test.skip(testInfo.project.name !== "mobile-observer", "Mobile observer test.");
  await page.goto("/dashboard/");
  await expect(page.locator(".truth-bar")).toBeVisible();
  await expect(page.getByText("Verifiable work. No pool wallet.")).toBeVisible();
  await expect(page.getByText("Dashboard state laboratory")).toHaveCount(0);
});
