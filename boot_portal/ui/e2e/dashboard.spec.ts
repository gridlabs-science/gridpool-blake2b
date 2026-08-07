import { expect, test } from "@playwright/test";

test("renders the live system map and preserves diagnostic details", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("link", { name: "GridPool system map" })).toBeVisible();
  await expect(page.getByRole("img", { name: "GridPool node wiring diagram" })).toBeVisible();
  await expect(page.getByRole("listbox", { name: /Work Set/i })).toBeVisible();

  await page.goto("/details");
  await expect(page.getByText("Active payout snapshot")).toBeVisible();
  await expect(page.getByText("Unpaid Work Set")).toBeVisible();
  await expect(page.getByText("Observed team work rate")).toBeVisible();
});

test("works at a mobile viewport", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("main")).toBeVisible();
  await expect(page.getByRole("button", { name: /theme/i })).toBeVisible();
});

test("switches theme and validates address input", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: /Switch to light theme/i }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");

  await page.goto("/details");
  await page.getByLabel("Bitcoin payout address").fill("not-a-bitcoin-address");
  await page.getByRole("button", { name: "Locate" }).click();
  await expect(page.locator(".notice-bad")).toBeVisible();
});

test("does not persist operator credentials", async ({ page }) => {
  await page.goto("/");
  await page.locator(".operator-button").click();
  await page.getByLabel("Admin API key").fill("browser-only-secret");
  await page.getByRole("button", { name: "Unlock", exact: true }).click();
  await expect(page.locator(".operator-unlocked")).toBeVisible();

  const storage = await page.evaluate(() => ({ ...window.localStorage }));
  expect(JSON.stringify(storage)).not.toContain("browser-only-secret");
  expect(page.url()).not.toContain("browser-only-secret");
});
