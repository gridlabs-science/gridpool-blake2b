import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  root: "simulator",
  base: "/__sim/",
  plugins: [react()],
  build: {
    outDir: "../../../tools/GridPool.DashboardSimulator/wwwroot/sim",
    emptyOutDir: true,
    sourcemap: true,
    target: "es2022"
  },
  server: {
    host: "127.0.0.1",
    port: 5174,
    proxy: {
      "/__sim/api": "http://127.0.0.1:5099"
    }
  }
});
