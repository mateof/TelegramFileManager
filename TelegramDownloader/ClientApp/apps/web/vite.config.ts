import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';
import { VitePWA } from 'vite-plugin-pwa';

const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf-8')) as { version: string };

// Dark "slate" background of the default theme (packages/ui/src/theme/palettes.ts)
const THEME_DARK = '#0b1120';

/**
 * Installable PWA (manifest + service worker at /app/sw.js, scope /app/).
 * The worker is hand-written (sw/sw.ts, built with injectManifest): it
 * precaches the shell and serves it offline, and never touches the API, hubs,
 * media or any other path of the server. See the PWA section of the README.
 */
function pwa() {
  const icon = (src: string, size: number, purpose: 'any' | 'maskable') => ({ src, sizes: `${size}x${size}`, type: 'image/png', purpose });
  const shortcutIcons = [{ src: 'icons/icon-192.png', sizes: '192x192', type: 'image/png' }];
  return VitePWA({
    strategies: 'injectManifest',
    srcDir: 'sw',
    filename: 'sw.ts',
    registerType: 'prompt',
    // Registered by src/pwa/register.ts after the page has loaded (lazy chunk)
    injectRegister: false,
    scope: '/app/',
    base: '/app/',
    manifestFilename: 'manifest.webmanifest',
    manifest: {
      id: '/app/',
      name: 'Telegram File Manager',
      short_name: 'TFM',
      description: 'Browse, stream and manage the files of your Telegram channels, from your own server.',
      start_url: '/app/',
      scope: '/app/',
      display: 'standalone',
      orientation: 'any',
      theme_color: THEME_DARK,
      background_color: THEME_DARK,
      categories: ['productivity', 'utilities'],
      icons: [
        icon('icons/icon-192.png', 192, 'any'),
        icon('icons/icon-512.png', 512, 'any'),
        icon('icons/maskable-192.png', 192, 'maskable'),
        icon('icons/maskable-512.png', 512, 'maskable')
      ],
      shortcuts: [
        { name: 'Channels', short_name: 'Channels', url: '/app/channels', icons: shortcutIcons },
        { name: 'Transfers', short_name: 'Transfers', url: '/app/transfers', icons: shortcutIcons },
        { name: 'Search', short_name: 'Search', url: '/app/search', icons: shortcutIcons },
        { name: 'Playlists', short_name: 'Playlists', url: '/app/playlists', icons: shortcutIcons }
      ],
      // Rich install UI (Chrome). Generated from the QA mock, see the README.
      screenshots: [
        { src: 'screenshots/wide.webp', sizes: '1280x800', type: 'image/webp', form_factor: 'wide', label: 'Channels and the file manager' },
        { src: 'screenshots/narrow.webp', sizes: '780x1688', type: 'image/webp', form_factor: 'narrow', label: 'File manager on a phone' }
      ]
    },
    injectManifest: {
      // The shell: index.html, every hashed chunk (lazy routes included), CSS,
      // fonts and icons. Screenshots are only for the install dialog.
      globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
      globIgnores: ['screenshots/**'],
      maximumFileSizeToCacheInBytes: 3 * 1024 * 1024
    },
    devOptions: { enabled: false }
  });
}

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
    plugins: [react(), tailwindcss(), pwa()],
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
