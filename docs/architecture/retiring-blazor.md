# Retiring the Blazor UI

The Blazor Server UI (Syncfusion + Blazor.Bootstrap) and the new web app (`/app`, React,
`TelegramDownloader/ClientApp`) currently coexist. This document lists **exactly** what to
delete and change to remove the Blazor UI, Syncfusion and Blazor.Bootstrap once the new web
app covers everything (see the [checklist](#1-before-you-start-what-the-new-web-app-must-cover)).

Phase 5 already prepared the ground:

- The service layer no longer uses Syncfusion types. `IFileService`, `IDbService`,
  `BsonFileManagerModel`, the persisted tasks, the v1 controllers, WebDAV, the mobile API and
  the resolvers work with the neutral types of `Models/FileSystem/FsEntry.cs`
  (`FsEntry`, `FsListing`, `FsMoveRequest`, `FsDeleteRequest`, `FsCreateFolderRequest`).
- The Syncfusion ⇄ neutral conversions live in `Shared/SyncfusionAdapters/`
  (`SfFileManagerMapper`, `SfFileServiceExtensions`). Only Blazor components use them.
- The folder upload lists local folders with `Services/Upload/LocalFolderLister.cs` instead of
  the EJ2 `PhysicalFileProvider` (a test proves both give the same listing).
- The EJ2 AJAX endpoints used by the Blazor file managers are isolated in
  `Controllers/Legacy/` (`FileController.Ej2.cs` is a partial of `FileController`,
  `TempController.cs` is entirely legacy).

`grep -rn Syncfusion --include=*.cs TelegramDownloader` outside `Pages/` and `Shared/`
returns only `Controllers/Legacy/*` and the license/registration lines of `Program.cs`.

---

## 1. Before you start: what the new web app must cover

Do not remove Blazor until every line is ticked (and tested on desktop and phone).

**Access and setup**
- [ ] Telegram login: phone, code, 2FA password, QR (Blazor `/`, `Index.razor` + login partials).
- [ ] Initial setup wizard: MongoDB connection, API id/hash (`/setup`, `Setup.razor`).
- [ ] Web password login and the "no password on the local network" mode.

**Channels and files**
- [ ] Channel list / sidebar, favourites, channel avatar.
- [ ] Channel file manager (`/fm/{id}`: SfFileManager on desktop, `MobileFileManager` on phone):
      browse, breadcrumbs, folder tree, sort, filter by type, search, details, multi-select.
- [ ] Create folder, rename, delete, copy, cut/paste (move) inside a channel.
- [ ] Upload from the browser into a channel folder.
- [ ] Download to the server (`DowloadFromTelegram.razor`), download to the browser.
- [ ] Preload to cache, share folder, "show in app", media URL, `.strm` export
      (`ExportStrmModal.razor`), add to playlist / save as playlist.
- [ ] Refresh channel (with live progress and cancel), channel info (`InfoPage.razor`).
- [ ] Shared collections (`/shared/{id}`, `/shared-local/{id}`), import shared data,
      import/export database (`ImportDataModal.razor`, `/api/file/export/{id}`).
- [ ] Chat view and media grid (`/filegrid/{id}`, `/fetchdata/{id}`).

**Local files**
- [ ] Local file manager (`/local-files`, `/localfm/{id}`): browse, create folder, rename,
      delete, copy/move, search, upload, download, image preview. These today go through the
      EJ2 endpoints `/api/file/FileOperations|Download|Upload|GetImage`.
- [ ] Temp folder manager (`/tempfm/{id}`) and the dropzone upload modal
      (`/api/temp/FileOperations|Download|Upload|GetImage/{id}`), or a decision to drop them.
- [ ] Upload local files/folders to Telegram with preview and duplicate detection
      (`UploadToTelegramModal.razor`).

**Media**
- [ ] Audio player (queue, playlists, gapless), video player, image viewer, PDF viewer.
- [ ] Playlists management (create, rename, reorder, download playlist to local).

**Transfers and administration**
- [ ] Transfers (`/downloads`): downloads, uploads, tasks, persisted tasks (pause, resume,
      cancel, retry, clear states) with live progress.
- [ ] Configuration (`/config`, every setting of `GeneralConfig`), logs (`/logs`, `/loglist`),
      system info and speed chart, database maintenance.
- [ ] Notifications/toasts that the Blazor UI shows today (via `NotificationModel` events and
      Blazor.Bootstrap `ToastService`) are delivered on `/hubs/events`.
- [ ] Links to the sister apps (TFM Player, Android, Android TV).

Also check that no external client (TFM Player, Android apps, Kodi `.strm` files, bookmarks)
uses a Blazor page URL; the media and stream URLs (`/api/file/GetFile*`, `/api/file/strm`,
`/api/video/*`, `/local/**`) are **not** Blazor and stay.

---

## 2. Remove the Blazor.Bootstrap leaks from the service layer

These are the only service-layer dependencies on Blazor.Bootstrap left. Fix them first, in
their own commit, while both UIs still exist:

| Where | What | Replace with |
|---|---|---|
| `Data/FileService.cs` (`_toastService`, constructor parameter, 5 `Notify` calls), `Data/FileServiceV2.cs` (constructor) | `BlazorBootstrap.ToastService` / `ToastMessage` / `ToastType` | `NotificationModel.sendEvent(new Notification(...))` (already relayed to `/hubs/events`) |
| `Data/ITelegramService.cs`, `Data/TelegramService.cs` (`getPaginatedMessages`) | `BlazorBootstrap.GridDataProviderResult<ChatMessages>` | an own `PagedResult<T> { Data, TotalCount }` (only `Pages/FileGrid.razor` calls it) |
| `Models/DownloadModel.cs` | `using BlazorBootstrap;` | remove the using (check it still builds) |

`IFileService.UploadFile(..., IBrowserFile)` uses `Microsoft.AspNetCore.Components.Forms`,
which belongs to the ASP.NET Core shared framework (no package). Only the Blazor channel file
manager calls it: delete the method (interface + `FileService`) together with the UI, or keep
it, it does not block anything.

---

## 3. Delete the Blazor UI

### 3.1 Folders and files (`TelegramDownloader/`)

| Delete | Notes |
|---|---|
| `Pages/` (whole folder) | All `.razor`, `.razor.css`, `_Host.cshtml`, `_Layout.cshtml`, `Error.cshtml(.cs)`, `Partials/`, `Modals/`. If the new app needs an error page, add a static one first. |
| `Shared/` (whole folder) | Layouts, nav menus, `MobileFileManager/`, pickers, **and `Shared/SyncfusionAdapters/`**. |
| `App.razor`, `_Imports.razor` (project root) | |
| `Controllers/Legacy/FileController.Ej2.cs` | EJ2 actions of `FileController`: `FileOperations`, `Download`, `Upload`, `GetImage`, `ToCamelCase` and the `operation` field. The `partial void OnLegacyFileManagerInit()` declaration in `Controllers/FileController.cs` can stay (no implementation = no call) or be removed with its call. Remove the `partial` keyword if you like. |
| `Controllers/Legacy/TempController.cs` | Entirely EJ2 (`/api/temp/*`). |
| `wwwroot/css/bootstrap`, `wwwroot/css/bootstrap5`, `wwwroot/css/open-iconic`, `wwwroot/css/custombuttons.css`, `wwwroot/css/site.css` | Blazor UI styles. |
| `wwwroot/js/bootstrap5`, `wwwroot/js/bshelp.js`, `wwwroot/js/drop.js`, `wwwroot/js/fileUploadVue.js`, `wwwroot/js/phonecodes.js`, `wwwroot/js/speedChart.js`, `wwwroot/js/upload.js` | JS interop of the Blazor pages (loaded only by `_Layout.cshtml`). Grep first: `grep -rn "js/<name>" TelegramDownloader --include=*.cs --include=*.html`. |
| `wwwroot/favicon.ico` | Only if the new app ships its own icon at the root. |

Keep: `wwwroot/app/` (the new app build output), `Resources/` (`app-not-built.html` is served
when the SPA is missing), `ClientApp/`.

### 3.2 Packages (`TelegramDownloader/TelegramDownloader.csproj`)

Remove:

```xml
<PackageReference Include="Blazor.Bootstrap" Version="..." />
<PackageReference Include="Syncfusion.Blazor" Version="..." />
<PackageReference Include="Syncfusion.EJ2.AspNet.Core" Version="..." />
```

(`Syncfusion.Licensing` comes transitively from `Syncfusion.Blazor`.) If the project was
created as a Razor/Blazor project, also drop any `Content`/`None` items that only exist for
`.razor` files.

### 3.3 `Program.cs`

| Remove | Why |
|---|---|
| `using Syncfusion.Blazor;` | |
| `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("...");` | Syncfusion license. |
| `builder.Services.AddSyncfusionBlazor();` | |
| `builder.Services.AddRazorPages();` | Only `_Host.cshtml`/`Error.cshtml` are Razor Pages. |
| `builder.Services.AddServerSideBlazor(...)...AddCircuitOptions(...)` (the whole chained statement) | Blazor Server circuits. |
| `builder.Services.AddBlazorBootstrap();` | |
| `TelegramDownloader.Services.WebAuth.InternalHttpClient.AddInternalHttpClient(builder.Services);` | The internal HTTP client only exists so the server-side Blazor file managers can call the legacy endpoints with `InternalToken`. Delete `Services/WebAuth/InternalHttpClient.cs` too, and the `InternalToken`/`InternalTokenHeader`/`IsInternalToken` members of `WebAuthService` plus `InternalTokenValid` in `WebAccessMiddleware`/`WebAccessPolicy` (and their tests in `Test/WebAuthTest.cs`). |
| `app.MapBlazorHub(...)` | `/_blazor` hub. |
| `app.MapFallbackToPage("/_Host");` | Replaced, see 3.4. |
| The Swagger comment "FileController and the other legacy controllers use Syncfusion types…" | Update it: after the removal `FileController` has no Syncfusion type. |

Also review any Blazor-only scoped services registered for the circuits (authentication state
providers, circuit handlers) — grep `CircuitHandler|AuthenticationStateProvider|AddScoped`.

`WebAccessPolicy`: the `BlazorHub` and `BlazorPage` path kinds and the `/_framework`,
`/_content` public prefixes become dead; remove them (and their test cases) or map them to
`NotFound`. Keep `Legacy` (`/api/file/*` streams, `/local`, `/img/telegram`).

If the `DefaultWebUi` setting (choose Blazor or the new app at `/`) exists in `GeneralConfig`
/ `ConfigController`, remove it: only one UI remains.

### 3.4 Serving the new app at `/`

Two options; **A** needs no frontend change.

**A. Keep the app under `/app` and redirect.** Replace `app.MapFallbackToPage("/_Host");` with:

```csharp
app.MapGet("/", () => Results.Redirect("/app/", permanent: false));
// Old Blazor deep links → their /app equivalents (adjust to the SPA route table).
app.MapGet("/fm/{id}", (string id) => Results.Redirect($"/app/channels/{id}"));
app.MapGet("/localfm/{id}", (string id) => Results.Redirect("/app/local"));
app.MapGet("/downloads", () => Results.Redirect("/app/transfers"));
app.MapGet("/config", () => Results.Redirect("/app/settings"));
```

The existing `app.MapGet("/app/assets/{**path}", ...)` and
`app.MapFallbackToFile("/app/{**path}", "index.html", ...)` stay as they are.

**B. Serve the app at the root.** In `ClientApp/apps/web`: Vite `base: '/'`, router
`basename: '/'`; build into `wwwroot/` (or keep `wwwroot/app` and point the static-file
provider's `RequestPath` to `""`). In `Program.cs` replace the `/app` static options and
fallbacks with:

```csharp
app.MapGet("/assets/{**path}", () => Results.NotFound());
app.MapFallbackToFile("{**path:nonfile}", "index.html", new StaticFileOptions { FileProvider = new PhysicalFileProvider(spaRoot) });
```

and add `/app/{**path}` → `/{path}` redirects so existing bookmarks keep working. Make sure the
fallback does not swallow `/api`, `/hubs`, `/webdav`, `/local`, `/img/telegram`, `/api-docs`
(they are mapped before the fallback; `{**path:nonfile}` already excludes files).

Update `WebAccessPolicy` so `/` and the SPA routes are `Public` (the SPA does its own login).

### 3.5 Tests (`Test/`)

| File | Action |
|---|---|
| `SfFileManagerAdapterTest.cs` | Delete (tests the Blazor adapter). |
| `LocalFolderListerTest.cs` | Delete the `ListsLikeThePhysicalFileProvider` cases and the `Legacy` helper (they compare with the EJ2 provider); keep the other tests. |
| `FsEntryMappingTest.cs` | Keep. |
| `WebAuthTest.cs` | Drop the internal-token and Blazor path-kind cases removed in 3.3. |

### 3.6 Other places

- `Dockerfile` / release workflows: nothing Blazor-specific is built (the Razor SDK compiles
  `.razor` inside `dotnet publish`); check that `wwwroot/app` is still copied.
- `docs/api/*.md`, `README.md`: remove references to the Blazor pages and to
  `/api/file/FileOperations`, `/api/temp/*`.
- `TelegramDownloader.styles.css` (scoped CSS bundle) disappears by itself.

---

## 4. Verify

```bash
dotnet build TelegramDownloader/TelegramDownloader.csproj
dotnet test Test/Test.csproj
grep -rn "Syncfusion\|BlazorBootstrap\|Blazor.Bootstrap" TelegramDownloader --include=*.cs --include=*.csproj --include=*.cshtml --include=*.razor   # → nothing
grep -rn "_Host\|MapBlazorHub\|AddServerSideBlazor" TelegramDownloader/Program.cs                                                                   # → nothing
```

Then, on a test instance (never on the machine with the real Telegram session unless you mean
to log in): open `/`, log in, browse a channel, upload, download, play audio/video, open
`/api-docs`, run a WebDAV client and the mobile apps against `/api/mobile` and `/api/v1`.
