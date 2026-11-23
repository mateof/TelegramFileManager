import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';

const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf-8')) as { version: string };

// The .NET server the dev server proxies to. Override with TFM_API_URL in
// apps/web/.env.local (e.g. TFM_API_URL=http://192.168.1.50:5000).
const DEFAULT_API = 'http://localhost:5000';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'TFM_');
  const target = env.TFM_API_URL || DEFAULT_API;
  const proxied = { target, changeOrigin: false, secure: false };

  return {
    // Served by ASP.NET at /app (MapFallbackToFile("/app/{**path}", "app/index.html"))
    base: '/app/',
    define: {
      'import.meta.env.APP_VERSION': JSON.stringify(pkg.version),
      'import.meta.env.BUILD_TIMESTAMP': JSON.stringify(new Date().toISOString().replace('T', ' ').slice(0, 19))
    },
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url))
      }
    },
    build: {
      outDir: fileURLToPath(new URL('../../../wwwroot/app', import.meta.url)),
      emptyOutDir: true,
      target: 'es2022',
      // No manualChunks: splitting vendors by hand created a circular import
      // between chunks in the TFM Player PWA (blank page). Feature routes are
      // lazy; scripts/check-chunks.mjs fails the build on static chunk cycles.
      // The initial-size budget (250 KB gzip) is enforced by check-chunks.mjs
      chunkSizeWarningLimit: 800,
      rollupOptions: {
        onwarn(warning, warn) {
          // @microsoft/signalr ships /*#__PURE__*/ comments Rollup cannot place: harmless noise
          if (warning.code === 'INVALID_ANNOTATION' && warning.id?.includes('@microsoft/signalr')) return;
          warn(warning);
        }
      }
    },
    server: {
      port: 5173,
      proxy: {
        '/api': proxied,
        '/hubs': { ...proxied, ws: true },
        '/local': proxied,
        '/img': proxied,
        '/webdav': proxied,
        '/swagger': proxied
      }
    },
    preview: {
      port: 4173
    }
  };
});
