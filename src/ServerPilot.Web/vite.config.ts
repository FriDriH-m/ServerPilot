import react from "@vitejs/plugin-react";
import { loadEnv } from "vite";
import { defineConfig } from "vitest/config";

export default defineConfig(({ mode }) => {
  const serverEnvironment = loadEnv(mode, ".", "SERVERPILOT_");
  const apiProxyTarget =
    serverEnvironment.SERVERPILOT_API_PROXY_TARGET ?? "http://127.0.0.1:8080";

  return {
    plugins: [react()],
    server: {
      host: "127.0.0.1",
      port: 5173,
      strictPort: true,
      proxy: {
        "/api": {
          target: apiProxyTarget,
          changeOrigin: false,
        },
      },
    },
    test: {
      environment: "jsdom",
      setupFiles: "./src/test/setup.ts",
      css: true,
      pool: "threads",
      fileParallelism: false,
      maxWorkers: 1,
    },
  };
});
