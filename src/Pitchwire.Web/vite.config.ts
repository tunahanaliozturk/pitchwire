import { fileURLToPath, URL } from "node:url";

import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vitest/config";

// The browser talks to one origin. Everything under /api is proxied to the service here in
// development and by nginx in the container, which means there is no CORS configuration anywhere and
// the device cookie behaves the way a cookie is supposed to.
const api = process.env.VITE_API_ORIGIN ?? "http://localhost:5080";

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  server: {
    proxy: {
      "/api": {
        target: api,
        changeOrigin: true,
        // The hub needs the upgrade, and a proxy that quietly drops it turns a socket into a very
        // confusing series of failed requests.
        ws: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    coverage: {
      provider: "v8",
      reporter: ["text", "lcov"],
      include: ["src/**/*.{ts,vue}"],
      exclude: ["src/api/schema.d.ts", "src/main.ts"],
    },
  },
  build: {
    // The manifest is what the budget script reads to work out what a first visit actually costs,
    // rather than guessing from file names.
    manifest: true,
    // A budget rather than a suggestion. The CI job fails on the warning, so a dependency that
    // doubles the bundle has to be argued for rather than merged.
    chunkSizeWarningLimit: 200,
  },
});
