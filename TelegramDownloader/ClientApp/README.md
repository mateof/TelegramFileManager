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

## Conventions in one minute

- Features live in `apps/web/src/features/<feature>/` and export lazy routes from `routes.ts`.
- UI strings: `t('English text')` from `@/i18n`; add the Spanish text to
  `apps/web/src/i18n/es/<area>.ts`. `npm test` fails on a missing translation.
- Server calls: `api.<module>.<call>()` from `@/lib/api` inside TanStack Query hooks.
- Realtime: `useEventsMessage()` / `useTransfersMessage()` / `useRealtime()` from `@/lib/realtime`.
- Components: import from `@tfm/ui`; see them live at `/app/dev/ui`.
- Styling: Tailwind 4 utilities with the semantic tokens (`bg-surface`, `text-fg-muted`,
  `border-border`, `bg-primary`…), never hard-coded greys.
