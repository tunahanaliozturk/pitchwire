import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
    testDir: "./e2e",
    testMatch: "*.pw.ts",
    timeout: 30_000,
    expect: { timeout: 10_000 },
    workers: 1,
    retries: process.env.CI ? 1 : 0,
    reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
    use: {
        ...devices["Desktop Chrome"],
        baseURL: process.env.PITCHWIRE_WEB_URL ?? "http://127.0.0.1:5082",
        screenshot: "only-on-failure",
        trace: "retain-on-failure",
    },
});
