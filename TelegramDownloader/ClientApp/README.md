# TFM web app (ClientApp)

The new React interface of Telegram File Manager, served by the .NET server at **`/app`**
(the classic Blazor UI keeps working at `/`). This folder is an **npm workspaces** monorepo:

| Workspace | Package | What it is |
| --- | --- | --- |
| `apps/web` | `@tfm/web` | The SPA (React 19, React Router 7, TanStack Query, zustand). Builds into `../wwwroot/app`. |
| `packages/ui` | `@tfm/ui` | Design system: theme engine (CSS variables) + accessible components on Radix primitives. |
| `packages/api-client` | `@tfm/api-client` | Typed client of the REST API v1 + SignalR hub helpers (`@tfm/api-client/signalr`). |
| `packages/file-manager` | `@tfm/file-manager` | Reusable file manager (headless core + default UI) fed by a `FileSystemProvider`. |

Read [ARCHITECTURE.md](ARCHITECTURE.md) before adding a feature.

## Requirements

- Node 22+ (npm 10+). No pnpm/yarn.
- For `npm run dev`: the .NET server running (default `http://localhost:5000`).

## Commands (run in this folder)

```bash
npm install          # once
npm run dev          # Vite dev server at http://localhost:5173/app/ (proxies the API to the .NET server)
npm run typecheck    # tsc in every workspace
npm run lint         # ESLint (typescript-eslint + react-hooks + react-refresh)
npm test             # Vitest (all workspaces, jsdom)
npm run build        # typecheck + vite build into ../wwwroot/app + chunk check
npm run preview      # serve the production build at http://localhost:4173/app/
```

### Dev server and the API

`apps/web/vite.config.ts` proxies `/api`, `/hubs` (WebSockets), `/local`, `/img`, `/webdav` and
`/swagger` to the .NET server. Point it elsewhere with `apps/web/.env.local`:

```
TFM_API_URL=http://192.168.1.50:5000
```

The dev server is same-origin from the browser's point of view, so the web session cookie works as
in production. Log in through the app (web password, then Telegram).

### Build output

`npm run build` writes `TelegramDownloader/wwwroot/app/` (emptied first). That folder is
git-ignored (only `.gitkeep` is kept, copied from `apps/web/public`). ASP.NET serves it with
`MapFallbackToFile("/app/{**path}", "app/index.html")`.

After the build, `scripts/check-chunks.mjs` fails if two chunks import each other statically (a
chunk cycle once shipped a blank page in the TFM Player PWA) and prints the gzip size of the initial
JS; it fails above **250 KB gzip**. Do not add `manualChunks`.

## Installable app (PWA)

The web app can be installed (Chrome/Edge "Install app", Android "Add to Home screen", iOS
Share → Add to Home Screen). The build emits `manifest.webmanifest`, the service worker `sw.js`
(scope `/app/`) and the icons in `public/icons` next to `index.html`.

- **What is cached**: only the app shell, precached at install time — `index.html`, every hashed
  JS/CSS chunk of the build (lazy routes included, ~1.8 MB raw), fonts, the favicon and the icons.
  Navigations inside `/app/` go to the network first and fall back to the cached `index.html` when
  the server cannot be reached (offline shell: the app opens and shows its "cannot reach the
  server" state).
- **What is never cached or intercepted**: the API (`/api/**`), SignalR (`/hubs/**`), the classic
  UI (`/`, `/_blazor/**`, `/api-docs`), user files and media (`/local/**`, `/img/**`, streams,
  Range requests, thumbnails, avatars), WebDAV and any non-GET request. No route of the worker
  matches them, so they behave as if there were no service worker: a 401 still reaches the app
  (login screen), and signing out leaves no private data in the caches. The worker is hand-written
  ([apps/web/sw/sw.ts](apps/web/sw/sw.ts), built by `vite-plugin-pwa` with `injectManifest`) to
  keep it that small.
- **Updates**: a new deploy installs a new worker in the background, which waits. The app shows a
  "New version available → Reload" toast; Reload activates it and reloads the page. Open apps check
  for a new version hourly and when they come back to the foreground. The registration code
  (`src/pwa/register.ts` + workbox-window) is a lazy chunk loaded after the page; only the
  `beforeinstallprompt` capture (`src/pwa/install.ts`) is in the initial bundle.
- **Install UX**: "Install app" in the account menu and an "Install this web app" card on the Apps
  page, hidden when already running installed; on iOS they explain Share → Add to Home Screen.
- **HTTPS is required** (service workers and installation only work in a secure context):
  serve the server over HTTPS (e.g. behind a reverse proxy) or open it as `http://localhost`.
  On a plain-HTTP LAN address the app works normally but cannot be installed, and the Apps page
  says so.
- **Server side**: `sw.js`, `manifest.webmanifest` and `index.html` are served `no-cache`, hashed
  assets for a year (`SpaCachePolicy`); `.webmanifest` is `application/manifest+json`; every
  `/app/**` file is public (the browser fetches the manifest without credentials), and a missing
  build file (`/app/sw.js`, `/app/icons/…`, `/app/assets/…`) is a 404, never `index.html`
  (`SpaHosting.IsMissingAppFile`).
- **Icons and screenshots**: `npm run pwa:icons` regenerates `apps/web/public/icons` from the
  brand mark (sharp). The manifest screenshots (`apps/web/public/screenshots`, used by Chrome's
  rich install dialog) are captures of the app (1280×800 wide, 390×844 @2x narrow).
- Testing locally: `npm run build && npm run preview`, then open http://localhost:4173/app/ (the
  worker is not registered by `npm run dev`).

## Conventions in one minute

- Features live in `apps/web/src/features/<feature>/` and export lazy routes from `routes.ts`.
- UI strings: `t('English text')` from `@/i18n`; add the Spanish and Galician texts to
  `apps/web/src/i18n/es/<area>.ts` and `apps/web/src/i18n/gl/<area>.ts`. `npm test` fails on a
  missing translation.
- Server calls: `api.<module>.<call>()` from `@/lib/api` inside TanStack Query hooks.
- Realtime: `useEventsMessage()` / `useTransfersMessage()` / `useRealtime()` from `@/lib/realtime`.
- Components: import from `@tfm/ui`; see them live at `/app/dev/ui`.
- Styling: Tailwind 4 utilities with the semantic tokens (`bg-surface`, `text-fg-muted`,
  `border-border`, `bg-primary`…), never hard-coded greys.
