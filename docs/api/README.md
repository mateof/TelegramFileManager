# TelegramFileManager API v1

A modular REST + SignalR API that exposes the **complete feature set of the web
application** so it can be driven from a mobile app (or any other client): sign
in to Telegram, browse and manage the files stored in your channels, browse the
server's local storage, move bytes in and out of Telegram with **live progress
over SignalR**, manage playlists, share libraries, and read/change the server
configuration.

This API lives side by side with the app's existing surfaces:

| Surface | Base path | Purpose |
| --- | --- | --- |
| **API v1** (this document) | `/api/v1` | Full-featured, versioned API for app clients. |
| Legacy mobile API | `/api/mobile` | The narrower API the current audio player uses. Untouched. |
| Web UI | `/` | The Blazor Server application. |

---

## Documentation index

| Document | What it covers |
| --- | --- |
| [getting-started.md](getting-started.md) | Base URL, API keys, the response envelope, error codes, paging, a first end-to-end walkthrough. |
| [authentication.md](authentication.md) | Phone login, QR login, 2FA, sessions, signing out. |
| [channels.md](channels.md) | Listing chats, folders, favourites, creating/leaving channels, indexing (refresh), message history, avatars, invitations. |
| [files.md](files.md) | Browsing, searching, folder/rename/delete/copy/move, direct upload, export/import of a channel index. |
| [transfers.md](transfers.md) | Downloads, uploads, message downloads, queue control, persisted tasks. The heart of the API. |
| [signalr.md](signalr.md) | The `/hubs/transfers` real-time hub: messages, client methods, reconnection, sample clients. |
| [local-files.md](local-files.md) | Browsing and managing the server's local storage, the streaming cache. |
| [playlists.md](playlists.md) | Playlists mixing Telegram and local tracks, reordering, bulk download. |
| [shares.md](shares.md) | Sharing a channel folder and importing a share, `.strm` export for media servers. |
| [system-and-config.md](system-and-config.md) | Health, metrics, logs, database maintenance, application settings, WebDAV bridge. |
| [reference.md](reference.md) | Full endpoint table and the data models returned by the API. |

## Interactive documentation (Swagger / OpenAPI)

The server serves a live, browsable OpenAPI document. With the app running:

- Swagger UI: **`/api-docs`** — pick **"TFM API v1 (full)"** from the definition selector.
- Raw OpenAPI JSON: **`/swagger/api-v1/swagger.json`**

The XML doc comments on every endpoint and DTO are compiled into that document,
so the descriptions you see there match this written documentation. You can feed
`/swagger/api-v1/swagger.json` to a code generator (`openapi-generator`,
`NSwag`, `swagger-codegen`, Kiota…) to produce a typed client for your mobile
platform.

## The 30-second tour

```
# 1. Is the server up and is my key valid?
GET  /api/v1/system/ping                     -> "pong"

# 2. What state is everything in?
GET  /api/v1/system/info                     -> versions, setup, auth, hub path

# 3. Sign in (once; the session is shared and long-lived)
POST /api/v1/auth/login  {phone}             -> step "vc"
POST /api/v1/auth/login  {code}              -> step "ok"

# 4. Find a channel and browse it
GET  /api/v1/channels?onlySaved=true
GET  /api/v1/channels/{id}/files?path=/

# 5. Download a file to the server, and watch it live
POST /api/v1/transfers/downloads  {channelId, fileIds}
     (connect to the SignalR hub /hubs/transfers for progress)
```

## Design principles

- **One envelope everywhere.** Every JSON response is
  `{ success, data, error, message, page }`. See
  [getting-started.md](getting-started.md#response-envelope).
- **Machine-readable errors.** `error.code` is a stable slug
  (`channel_not_found`, `not_logged_in`, …) you can branch on.
- **Non-blocking transfers.** Anything that moves bytes returns immediately with
  `202 Accepted`; progress arrives on the SignalR hub.
- **Paths, not opaque handles.** Folders are addressed by human-readable path
  (`/music/rock/`) as well as by id, so URLs are debuggable.
- **Shared state.** The Telegram session, the transfer queue and the
  configuration are the same objects the web UI uses. A file downloaded from the
  API shows up in the web UI and vice versa.
