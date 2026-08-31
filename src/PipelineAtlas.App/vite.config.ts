import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In `patlas view` the CLI serves raw target files at /source. For local dev we
// mirror that from a target folder so the viewer's SOURCE panel works the same.
// Point it elsewhere with PATLAS_DEV_TARGET.
const appDir = fileURLToPath(new URL(".", import.meta.url));
// Defaults to the committed dev seed (fixtures/sample); override for other targets.
const devTarget = resolve(process.env.PATLAS_DEV_TARGET ?? resolve(appDir, "../../fixtures/sample"));

export default defineConfig({
  base: "./",
  plugins: [
    react(),
    {
      name: "patlas-dev-source",
      configureServer(server) {
        server.middlewares.use((req, res, next) => {
          if (!req.url || !req.url.startsWith("/source")) return next();
          const path = new URL(req.url, "http://localhost").searchParams.get("path");
          if (!path) {
            res.statusCode = 400;
            return res.end();
          }
          const full = resolve(devTarget, path);
          if (!full.startsWith(devTarget)) {
            res.statusCode = 403;
            return res.end();
          }
          try {
            res.setHeader("Content-Type", "text/plain; charset=utf-8");
            res.end(readFileSync(full, "utf8"));
          } catch {
            res.statusCode = 404;
            res.end();
          }
        });
      },
    },
  ],
  build: { outDir: "dist", emptyOutDir: true },
});
