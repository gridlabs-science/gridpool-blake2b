import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./simulator/e2e",
  fullyParallel: false,
  reporter: "list",
  use: {
    baseURL: process.env.GRIDPOOL_SIM_URL ?? "http://127.0.0.1:5099",
    trace: "retain-on-failure",
    screenshot: "only-on-failure"
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    { name: "mobile-observer", use: { ...devices["Pixel 7"] } }
  ]
});
