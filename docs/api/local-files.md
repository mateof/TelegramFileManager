# Local files (server storage)

The server has a **local root** — the folder downloads land in and uploads are
taken from. These endpoints browse and manage it, mirroring the **Local** tab of
the web file manager. Base path: `/api/v1/local`.

Reading local files does **not** require a Telegram session.

## Paths and safety

Every path is **relative to the local root**. An empty path is the root.

```
""              -> the local root
"music"         -> <root>/music
"music/rock"    -> <root>/music/rock
```

All paths are validated against directory traversal: absolute paths, and `..`
segments that would escape the root, are rejected with `400 invalid_request`.
The returned file objects use the same [`ApiFileDto`](files.md#the-file-object)
shape as the channel files, where `id` is the relative path.

## Browse

```
GET /api/v1/local?path=music&filter=audio&sortBy=name&page=1&pageSize=100
```

Same query parameters as [channel browse](files.md#browse-a-folder)
(`path`, `filter`, `search`, `filesOnly`, `sortBy`, `sortDescending`, paging).
Response is an `ApiFolderContentsDto` with items, stats and breadcrumbs.

Local files carry usable media URLs:

- Audio → `streamUrl` points at `/local/<path>` (static, range-capable).
- Video → `streamUrl` points at `/api/localvideo/stream?path=…`.
- `downloadUrl` → `/local/<path>`.

Append `?apiKey=…` to those URLs when a media element cannot send the header.
(The `/local/*` static route itself is not behind the API key, so those URLs
work as-is inside `<audio>`/`<video>`.)

## Info & size

```
GET /api/v1/local/info?path=music/rock/song.mp3    # one entry's metadata
GET /api/v1/local/size?path=music                  # recursive size + type breakdown
```

`size` returns a `DirectorySizeModel`:

```json
{
  "data": {
    "sizeBytes": 934512345,
    "sizeWithSuffix": "891 MB",
    "totalElements": 120,
    "filesByType": [
      { "extension": ".mp3", "category": "Audio", "icon": "bi-music-note-beamed",
        "count": 118, "sizeBytes": 930000000, "sizeWithSuffix": "887 MB" }
    ]
  }
}
```

## Mutations

```
POST /api/v1/local/folders   { "path": "music", "name": "rock" }         # create a directory -> 201
POST /api/v1/local/rename    { "path": "music/old.mp3", "newName": "new.mp3" }
POST /api/v1/local/delete    { "paths": ["music/a.mp3", "old-folder"] }   # recursive; irreversible
```

`delete` returns `{ accepted, skipped }`. Names may not contain path separators.
Name clashes return `409 conflict`.

## Download a local file

```
GET /api/v1/local/download?path=music/rock/song.mp3
```

Streams the file with HTTP range support and the right content type. Behind the
API key, so use `?apiKey=…` when embedding.

`disposition=inline` serves it for viewing in the browser instead of saving it
(this is the `contentUrl` of local entries):

```
GET /api/v1/local/download?path=music/song.mp3&disposition=inline
```

## Copy / move

```
POST /api/v1/local/copy
POST /api/v1/local/move
{ "paths": ["music/rock", "notes.txt"], "targetPath": "backup/2026", "overwrite": false }
```

- Every source and the target are checked against the local root like the other
  endpoints (`400` when `targetPath` escapes it); sources that escape it are
  skipped.
- The target folder is created when missing. Folders are copied recursively;
  a move across volumes falls back to copy + delete.
- An entry whose name already exists in the target is skipped unless
  `overwrite` is true (then the existing entry is replaced).
- Copying/moving an entry onto itself, or a folder into itself or one of its
  sub-folders, skips that entry.

Answers `{ accepted, skipped }` (the paths not processed).

## Upload into local storage

```
POST /api/v1/local/upload
Content-Type: multipart/form-data

file=<binary>
path=incoming        (form field, optional destination folder)
```

Stores the file under the local root and returns `201` with its file object.
This does **not** push to Telegram — it just stages bytes on the server. To then
push them, call:

```
POST /api/v1/transfers/uploads
{ "channelId": "…", "localPaths": ["incoming/song.mp3"], "targetPath": "/music/" }
```

This two-step (stage locally, then upload) avoids sending the bytes twice and
gives you a managed, resumable, hub-tracked upload.

To see first what that upload would do (new, replaced and skipped files, bytes
to send, folders to create, optionally files whose content is already in the
channel), send the same body to
[`POST /api/v1/transfers/uploads/preview`](transfers.md#preview-an-upload-dry-run).

## Clear the streaming cache

```
POST /api/v1/local/cache/clear
```

Empties the temporary cache the app fills when streaming files from Telegram for
playback. Frees disk space; the next playback re-downloads what it needs.

Next: [playlists.md](playlists.md).
