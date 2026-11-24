# Architecture of the web app

This is the guide for anyone adding a feature to `apps/web`. Short version: **own your feature
folder, register lazy routes, use `@tfm/ui` + `api` + `t()`, keep the initial bundle small.**

```
ClientApp/
  package.json            npm workspaces + root scripts
  tsconfig.base.json      strict TS shared by every workspace
  eslint.config.js        one ESLint config for all workspaces
  vitest.config.ts        one Vitest run for all workspaces (jsdom)
  scripts/check-chunks.mjs
  packages/
    api-client/           @tfm/api-client
    ui/                   @tfm/ui
    file-manager/         @tfm/file-manager
  apps/
    web/                  @tfm/web (the SPA)
```

Workspace packages are consumed **as TypeScript source** (`exports` points at `src/index.ts`):
no build step, Vite compiles them with the app, and Tailwind scans them (`@source`).

## 1. apps/web

```
src/
  main.tsx               mounts <App/>
  index.css              tailwindcss + @tfm/ui/styles.css
  app/
    App.tsx              providers: QueryClient, ThemeController, TooltipProvider, Router, ConfirmHost, Toaster
    routes.tsx           THE route table (imports each feature's routes.ts)
    lazyPage.ts          lazyPage(() => import('./pages/X')) helper
    RouteError.tsx       route error boundary (also handles stale chunks after a deploy)
  auth/
    gates.tsx            WebAuthGate (web password) → TelegramGate (Telegram session) → AppLayout
    WebAuthScreens.tsx   set password / login (lazy)
    TelegramLogin.tsx    phone → code → 2FA, and QR with polling + QR 2FA (lazy)
    useTelegramStatus.ts
  shell/
    AppLayout.tsx        sidebar + top bar + <Outlet/> + PlayerSlot + bottom nav + drawer
    Sidebar.tsx, ChannelList.tsx, TopBar.tsx, MobileNav.tsx
    CommandPaletteHost.tsx / CommandPaletteContent.tsx   Ctrl/Cmd+K (lazy)
    commands.ts          useRegisterCommands(): features add palette commands
    nav.ts               main navigation items
    PageHeader.tsx       standard page title (+ document.title)
    PlaceholderPage.tsx  temporary page of unimplemented features
    useChannels.ts       channel list / folders / favourite toggle queries
  lib/
    api.ts               the ApiClient singleton + errorMessage()
    queryClient.ts       TanStack Query defaults (no retry on 4xx)
    queryKeys.ts         keys shared between features
    realtime.ts          SignalR hubs (lazy), notification store, hooks
    format.ts            formatBytes, formatRelative
  stores/ui.ts           layout state (sidebar collapsed, drawer, palette, channel tab)
  i18n/                  t(), dictionaries per area in es/<area>.ts, coverage test
  features/<feature>/    one folder per feature (see below)
```

### Auth flow

1. `WebAuthGate` reads `GET /api/v1/web-auth/status`:
   - no password and no LAN bypass → **set password** screen (`POST setup`, then auto login);
   - not authenticated → **login** (`POST login {password, remember}`; 401 `invalid_credentials`,
     429 `too_many_attempts`);
   - a 404 (server without web auth) is treated as "open" so older back-ends still work in dev.
   - Every 401 from any API call fires `apiEvents` → `unauthorized`; the gate re-checks the
     status and the login screen comes back. Features never handle 401.
2. Routes from `features/setup` render here (full screen, before the Telegram login).
3. `TelegramGate` reads `GET /api/v1/auth/status`: not configured → `/setup`; not logged in →
   Telegram login; otherwise starts the realtime hubs and renders `AppLayout`. An `AuthState`
   event on `/hubs/events` refreshes it.

### Routes (basename `/app`)

| Path | Feature folder | Page(s) |
| --- | --- | --- |
| `/` | – | redirects to `/channels` |
| `/channels`, `/channels/:id/*` | `channels` | channel picker, file manager (the `*` is free for the folder path) |
| `/local/*` | `local` | local files |
| `/shared`, `/shared/:id/*` | `shared` | shared collections |
| `/transfers` | `transfers` | transfers |
| `/playlists`, `/playlists/:id` | `playlists` | playlists |
| `/library`, `/library/:section` | `library` | movies / series |
| `/search` | `search` | global search (`?q=` is set by the command palette) |
| `/chat/:id` | `chat` | chat view |
| `/settings`, `/settings/:section` | `settings` | settings (the user menu links `/settings/appearance`) |
| `/logs` | `logs` | logs |
| `/system` | `system` | system info / metrics |
| `/maintenance` | `maintenance` | DB tools, import/export |
| `/setup` | `setup` | first-run wizard (outside the Telegram gate) |
| `/apps` | `apps` | links to TFM Player / Android / Android TV (implemented) |
| `/dev/ui` | `dev-ui` | component gallery (not in the navigation) |
| `*` | – | not found |

## 2. Adding / implementing a feature

Each feature owns **its folder** and **its dictionary** `i18n/es/<feature>.ts`. The foundation
created a placeholder for every feature:

```
features/transfers/
  routes.ts              export const routes: RouteObject[] = [{ path: 'transfers', lazy: lazyPage(() => import('./pages/TransfersPage')) }]
  pages/TransfersPage.tsx  export default function TransfersPage() { ... }   ← replace the placeholder
```

Rules:

- `routes.ts` must stay tiny and import pages **only through `lazyPage(() => import(...))`**: each
  page becomes its own chunk. Never import a page statically from outside the feature.
- Add sub-routes by adding entries to your `routes.ts` (paths are absolute from `/app`, without the
  leading slash). A brand-new feature adds one import + spread line in `app/routes.tsx`.
- Pages render inside `<main id="tfm-main">`, which is the scroll container. Use
  `<PageHeader title icon description actions/>` at the top, and a centered container such as
  `mx-auto w-full max-w-6xl p-4 sm:p-6 lg:p-8`. Full-height tools (file manager) can use `h-full`.
- Data: TanStack Query with `api.*` calls; pass the `signal` from the query function. Put shared
  keys in `lib/queryKeys.ts`, private keys starting with your feature name.
- Mutations: show results with `toast.success/error` from `@tfm/ui`; confirm destructive actions
  with `await confirm({ title, description, destructive: true })`.
- Realtime: `useEventsMessage('ChannelRefreshed', handler)`, `useTransfersMessage('TransfersSnapshot', handler)`,
  `useRealtime((s) => s.transferSummary)`. For hub methods: `(await getTransfersHub()).getSpeedHistory()`,
  `(await getEventsHub()).subscribeMetrics()` (returns the unsubscribe function).
- Command palette: `useRegisterCommands(useMemo(() => ({ id: 'transfers', heading: t('Transfers'), items: [...] }), []))`.
- Shared state across features: a zustand store in your feature folder (persist only UI prefs).
- Tests: `*.test.ts(x)` next to the code; `npm test` runs them. The setup file sets the language to
  English, so assert on the English keys.
- **Media**: play or show files only through the public modules —
  `playAudio(tracks, start)`, `enqueueAudio(tracks, 'next' | 'end')`, `openViewer(items, index)` from
  `@/features/player/public` and `openAddToPlaylist(candidates)` from `@/features/playlists/public`.
  They are tiny; the engine, the player UI, the viewers and the picker are lazy chunks.
  `features/player/PlayerSlot.tsx` (rendered by the layout between the content and the mobile
  bottom nav) mounts them: the player bar while the queue is not empty (the queue is persisted and
  comes back paused after a reload), the viewer overlay and the playlist picker while open.
  Audio URLs must be same-origin (`contentUrl`/`playbackUrl` of the v1 files, `api.local.downloadUrl`).

## 3. i18n

Same system as the TFM Player PWA. The English text is the key: `t('Delete {count} files', { count })`.
Spanish is the default; a missing translation falls back to the English key. Dictionaries:
`apps/web/src/i18n/es/<area>.ts` (`common`, `shell`, `auth`, `apps`, one per feature), merged at
startup. Changing language reloads the page (`setLanguage`). `i18n.test.ts` scans every source file
for `t('literal')` and fails if a key has no Spanish entry, or if two area files translate the same
key differently. Only literal keys are detected: write `t('Downloads')`, not `t(variable)`.

`@tfm/ui` has no i18n of its own: its few built-in labels are set once by the app through
`configureUiStrings()` (in `App.tsx`).

## 4. @tfm/ui and theming

The theme is CSS variables rewritten at runtime by `<ThemeController/>` (technique of the PWA):

- `--color-accent-50…950`: accent palette (indigo, violet, ocean, teal, emerald, rose, sunset, amber).
- `--color-neutral-50…950`: background palette (slate, graphite, midnight, AMOLED, light). The
  **light theme is the same scale reversed**, so dark-first classes flip automatically.
- Semantic tokens built on them — use these: `bg-bg`, `bg-surface`, `bg-surface-2/3`, `text-fg`,
  `text-fg-muted`, `text-fg-subtle`, `border-border`, `border-border-strong`, `bg-primary`,
  `bg-primary-soft`, `text-on-accent`, `ring-ring`, `danger/warning/success/info` (+ `-soft`).
- Effects (data attributes on `<html>`): `data-glass` (utility classes `glass`, `glass-strong`),
  `data-ambient` (the `.tfm-ambient` gradient layer), `data-motion="reduced"` (setting or OS
  preference; disables animations globally), `data-density`. `data-scheme="light|dark"` drives the
  `light:` / `dark:` variants.
- Settings live in the persisted zustand store `useAppearance()` (`accent`, `background`
  incl. `system`, `glass`, `ambient`, `motion`, `density`, `toggleLight()`); the settings feature
  builds its Appearance page on it. `useResolvedTheme()`, `useReducedMotion()`, `useMediaQuery()`.
- Animations: `animate-fade-in`, `animate-scale-in`, `animate-pop-in`, `animate-slide-in-*`,
  `animate-shimmer`, eases `ease-spring`, `ease-out-soft`; shadows `shadow-soft`,
  `shadow-elevated`, `shadow-glow`.

Components (one module each, all exported from `@tfm/ui`): `Button`, `IconButton` (label required),
`Input` (leading/trailing), `Textarea`, `Field` (label/hint/error + aria wiring), `Label`, `Select`,
`Switch`, `Checkbox`, `RadioGroup`, `Slider`, `SegmentedControl`, `Dialog`/`DialogContent`
(title, description, footer, size), `Sheet`/`SheetContent` (right/left/bottom drawer),
`AlertDialog`, `confirm()` + `ConfirmHost`, `DropdownMenu*`, `ContextMenu*`, `Tabs*` (pill/underline),
`Tooltip`, `Popover*`, `Toaster` + `toast`, `Progress`, `Skeleton`, `Badge`, `CountBadge`, `Card*`,
`ScrollArea`, `Breadcrumb`, `EmptyState`, `Kbd`, `Spinner`, `Avatar`, `CommandPalette`, `cn()`.
Prefer composing these over new primitives; if something is generic, add it to `@tfm/ui`.

## 5. @tfm/api-client

- `createApiClient()` → `{ http, auth, webAuth, channels, files, transfers, local, deleteOperations,
  playlists, shares, config, system, library, search }`; the app's instance is `api` in `@/lib/api`.
- DTO interfaces mirror the C# classes in `Models/Api/*.cs` with the same names (camelCase
  properties). String-constant classes are `const` objects + union types (`AuthStep`, `TransferKind`…).
- Calls return the unwrapped `data`; paged endpoints return `{ items, page }`; failures throw
  `ApiError { code, status, message, detail }` (`isApiError()`). Non-GET requests send
  `X-Requested-With: tfm`; all requests are same-origin with credentials. Every GET takes an
  `AbortSignal` as its last argument.
- Streams/downloads are not fetched: use the `...Url()` helpers for `<img>/<audio>/<a href>`.
- `@tfm/api-client/signalr`: `createTransfersConnection()`, `createEventsConnection()` →
  `TypedHub` with typed `on()`, `onStatus()`, endless reconnect (also of the first start), and typed
  hub methods. In the app, use `lib/realtime.ts` instead (it lazy-loads SignalR).
- Provisional parts (aligned when the API agent's work is merged): the `Metrics` payload and
  `UnsubscribeMetrics` of `/hubs/events`. (`search.global` → `ApiGlobalSearchResultDto` and the
  `LibraryScan` payload already follow the server.)

## 6. @tfm/file-manager

Reusable file manager (see [packages/file-manager/README.md](packages/file-manager/README.md)):
headless core (`useFileManager`, pure selection/history/clipboard/keyboard/upload-queue modules) +
default UI (`<FileManager provider=... actions=... strings=.../>`), fed by a `FileSystemProvider`
with capability flags and extended through the actions registry (`FileAction`). The library does
not import TFM code. TFM providers and actions live in the app:

- `features/files/`: shared by the three file manager features (no routes): DTO → entry mapping,
  translated labels (`fileManagerStrings()`), XHR upload with progress, browser downloads, player /
  viewer / playlist actions (`openMedia`, `mediaActions`), local folder picker, "download to the
  server" dialog, import progress, `useRoutePath()` (the route splat is the folder path, so deep
  links like `/channels/42/music/rock` and `/local/downloads` open that folder).
  `files/deletes/`: deletes with progress. The providers call `startDelete()` (`deleteTracker.ts`):
  background operation (`files.deleteOperations` capability) followed through `DeleteProgress` on
  /hubs/events with a 1 s poll fallback, or the old synchronous endpoint on older servers. The
  entries are hidden from the listings while the delete runs (`withoutPendingDeletes()` in the
  providers' `list`) and the listings are invalidated when it ends. `<DeleteProgressHost/>` (layout,
  bottom left of the content) lazy-loads the stacked cards; the transfers popover lists the running
  ones under "Other operations".
- `features/channels/`: channel provider (`createChannelProvider`, also used read only for shared
  collections with `collectionId`), TFM actions, header (refresh with live progress, export/import
  index, WebDAV, statistics, delete index, link to the chat view).
- `features/local/`: local files provider + "upload to Telegram" with the upload preview.
- `features/shared/`: shared collections list/import and read-only browsing.

## 7. Performance budget

Initial JS < 250 KB gzip (checked by `scripts/check-chunks.mjs`; ~205 KB with the shell today).
What is already lazy: every feature page, the login screens, the command palette, SignalR. Keep it
that way: heavy libraries (charts, video, PDF, markdown…) only inside lazy pages or `import()`.
