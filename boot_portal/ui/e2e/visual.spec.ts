import { expect, test } from "@playwright/test";
import { diagramFixture, summaryFixture } from "../src/test/fixture";

test.beforeEach(async ({ page }) => {
  await page.route("**/api/dashboard/v1/summary**", (route) =>
    route.fulfill({ json: summaryFixture }));
  await page.route("**/api/dashboard/v1/history**", (route) =>
    route.fulfill({
      json: {
        schemaVersion: 1,
        window: "24h",
        windowSeconds: 86_400,
        generatedAtUtc: "2026-07-29T12:00:00Z",
        points: []
      }
    }));
  await page.route("**/api/dashboard/v1/diagram/events**", (route) =>
    route.fulfill({
      json: {
        schemaVersion: 1,
        generatedAtUtc: "2026-07-29T12:00:00Z",
        redacted: true,
        oldestSequence: 9,
        latestSequence: 8,
        nextSequence: 8,
        hasMore: false,
        gap: false,
        events: []
      }
    }));
  await page.route("**/api/dashboard/v1/diagram", (route) =>
    route.fulfill({ json: { ...diagramFixture, redacted: true } }));
});

test("dark dashboard visual", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("link", { name: "GridPool system map" })).toBeVisible();
  await expect(page).toHaveScreenshot("dashboard-dark.png", {
    animations: "disabled",
    fullPage: true
  });
});

test("light dashboard visual", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: /Switch to light theme/i }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  await expect(page).toHaveScreenshot("dashboard-light.png", {
    animations: "disabled",
    fullPage: true
  });
});
