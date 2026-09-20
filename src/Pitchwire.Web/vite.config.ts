import { fileURLToPath, URL } from "node:url";

import vue from "@vitejs/plugin-vue";
import { VitePWA } from "vite-plugin-pwa";
import { defineConfig } from "vitest/config";

// The browser talks to one origin. Everything under /api is proxied to the service here in
// development and by nginx in the container, which means there is no CORS configuration anywhere and
// the device cookie behaves the way a cookie is supposed to.
const api = process.env.VITE_API_ORIGIN ?? "http://localhost:5080";

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      // injectManifest rather than generateSW: the push and notification click handlers are the
      // reason this worker exists, and a generated one would precache the build and do neither.
      strategies: "injectManifest",
      srcDir: "src",
      filename: "sw.ts",
      registerType: "autoUpdate",
      injectManifest: {
        globPatterns: ["**/*.{js,css,html,svg}"],
      },
      manifest: {
        name: "pitchwire",
        short_name: "pitchwire",
        description: "Live football scores, fixtures and tables.",
        start_url: "/",
        display: "standalone",
        background_color: "#0e1116",
        theme_color: "#0e1116",
        icons: [{ src: "/icon.svg", sizes: "any", type: "image/svg+xml", purpose: "any" }],
      },
      devOptions: { enabled: false },
    }),
  ],
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
