# Files (channel storage)

These endpoints browse and manage the files a channel stores in Telegram, using
the local index. They mirror the **Remote** tab of the web file manager. All are
under:

```
/api/v1/channels/{channelId}/files
```

and require a Telegram session.

## Addressing folders

A folder can be addressed two ways, accepted interchangeably (id wins when both
are given):

- **By path** — `?path=/music/rock/`. Human-readable, always ends with `/`, `/`
  is the channel root.
- **By id** — `?folderId=694a…`. The MongoDB id of the folder document, returned
  as `currentFolderId` and on each folder item.

Files are addressed by their id (`fileId`), returned on every file item.

## The file object

Every file/folder is returned as this shape:

```json
{
  "id": "694a7ec440073c1c7e42f678",
  "name": "NIVIRO - Flashes.mp3",
  "path": "/music/rock/",
  "parentId": "6949…",
  "isFile": true,
  "hasChildren": false,
  "size": 8123456,
  "sizeText": "7.7 MB",
  "type": ".mp3",
  "category": "Audio",
  "dateCreated": "2026-05-01T10:00:00Z",
  "dateModified": "2026-05-01T10:00:00Z",
  "messageId": 88213,
  "isSplit": false,
  "md5Hash": null,
  "xxHash": null,
  "streamUrl": "http://host/api/file/GetFileStreamCached/1290586824/694a…/NIVIRO%20-%20Flashes.mp3",
  "downloadUrl": "http://host/api/file/GetFileByTfmId/NIVIRO%20-%20Flashes.mp3?idChannel=1290586824&idFile=694a…"
}
```

- **`category`** — one of `Audio`, `Video`, `Photo`, `Document`, `Archive`,
  `Application`, `Other`, `Folder`.
- **`streamUrl`** — present for audio/video. Range-capable, so it can be fed
  directly to a player. Append `&apiKey=…` when the media element can't send
  headers.
- **`downloadUrl`** — downloads the whole file, using the server cache when it
  already has it.
- **`isSplit`** — the file was uploaded as several Telegram messages (files over
  Telegram's per-message size limit). The stream/download URLs handle
  reassembly transparently.
- **`messageId`** — the backing Telegram message (null when split).

> `streamUrl`/`downloadUrl` are read-through: they pull from Telegram on demand
> and cache to disk. To pull a file onto the server as a managed, resumable
> transfer instead, use [transfers](transfers.md).

## Browse a folder

```
GET /api/v1/channels/{channelId}/files?path=/&filter=audio&sortBy=name&page=1&pageSize=100
```

| Query | Default | Notes |
| --- | --- | --- |
| `path` / `folderId` | root | Folder to list. |
| `filter` | `all` | `audio`, `video`, `photo`, `document`, `archive`, `all`. |
| `search` | – | Substring on the name, within this folder. |
| `filesOnly` | `false` | Hide folders. |
| `sortBy` | `name` | `name`, `date`, `size`, `type`. Folders always sort first. |
| `sortDescending` | `false` | |
| `page`, `pageSize` | 1, 50 | |

The payload includes the items (paged), navigation, aggregate stats and a
breadcrumb:

```json
{
  "data": {
    "channelId": "1290586824",
    "currentPath": "/music/rock/",
    "currentFolderId": "6949…",
    "parentFolderId": "6948…",
    "parentPath": "/music/",
    "folderName": "rock",
    "items": [ … ],
    "stats": {
      "folderCount": 3, "fileCount": 120,
      "audioCount": 118, "videoCount": 0, "photoCount": 1, "documentCount": 1,
      "totalSize": 934512345, "totalSizeText": "891 MB"
    },
    "breadcrumbs": [
      { "name": "Files", "path": "/", "folderId": null },
      { "name": "music", "path": "/music/", "folderId": null },
      { "name": "rock", "path": "/music/rock/", "folderId": null }
    ]
  },
  "page": { "page": 1, "pageSize": 100, "totalItems": 123, "totalPages": 2, "hasNext": true, "hasPrevious": false }
}
```

`stats` describes the **whole folder**, not just the current page.

## Search a subtree

```
GET /api/v1/channels/{channelId}/files/search?q=flashes&path=/music/&filter=audio
```

Searches file names within `path` (default: the whole channel). Returns a flat,
paged list of file objects. Same `filter`/`sortBy`/paging as browse.

## One entry

```
GET /api/v1/channels/{channelId}/files/{fileId}
```

Returns a single file object. `404 file_not_found` when unknown.

## Folder statistics

```
GET /api/v1/channels/{channelId}/files/stats?path=/music/
```

Recursive size and type breakdown of a subtree (an `ApiFolderStatsDto`). Handy
for a "folder properties" screen.

## Create a folder

```
POST /api/v1/channels/{channelId}/files/folders
{ "path": "/music/", "name": "rock" }
```

Returns `201` with the created folder. `409 conflict` when a sibling with that
name exists. The name may not contain `/` or `\`.

## Rename

```
PUT /api/v1/channels/{channelId}/files/{fileId}/name
{ "newName": "Best of Rock.mp3" }
```

Works for files and folders. Returns the updated entry.

## Delete

```
POST /api/v1/channels/{channelId}/files/delete
{ "ids": ["694a…", "694b…"] }
```

Deletes files and folders (folders recursively). **This also deletes the backing
Telegram messages** when no other indexed entry references them, so it actually
frees the channel storage. Irreversible.

```json
{ "data": { "accepted": 2, "skipped": [], "taskId": null }, "message": "2 entries deleted" }
```

`skipped` lists ids that could not be resolved or deleted.

## Copy / move

```
POST /api/v1/channels/{channelId}/files/copy
POST /api/v1/channels/{channelId}/files/move
{ "ids": ["694a…"], "targetPath": "/backup/" }   // or "targetFolderId": "…"
```

Both operate **within the same channel**. Copies are index-level: the Telegram
messages are shared between the original and the copy, so a copy consumes no
extra channel storage. `409 conflict` on a name clash in the target.

## Upload a file into the channel

Two ways to get bytes into a channel:

### A. Direct multipart upload (client → server → Telegram)

```
POST /api/v1/channels/{channelId}/files/upload
Content-Type: multipart/form-data

file=<binary>
path=/incoming/        (form field, optional)
```

The server streams the body to Telegram. It appears in the task list, is
persisted and streams progress on the hub, exactly like a web upload. Returns
`202 Accepted`.

### B. Upload a file already on the server

If the bytes are already under the server's local root (for example the client
pushed them via `POST /api/v1/local/upload` first, or they were downloaded
earlier), use the transfers endpoint instead so the bytes aren't sent twice:

```
POST /api/v1/transfers/uploads
{ "channelId": "…", "localPaths": ["incoming/song.mp3"], "targetPath": "/music/" }
```

See [transfers.md](transfers.md).

## Export / import a channel index

Move a library between server instances without re-uploading the files.

```
GET  /api/v1/channels/{channelId}/files/export            # -> application/json download
POST /api/v1/channels/{channelId}/files/import            # multipart: file=<export.json>
```

The export is a JSON description of the index (names, sizes, Telegram message
ids). Import rebuilds the index on another instance; the files are read from
Telegram, so the importing account must be a member of the channel (see
[shares](shares.md) for the flow that also handles joining). Import runs in the
background and returns `202 Accepted`.

Next: [transfers.md](transfers.md).
