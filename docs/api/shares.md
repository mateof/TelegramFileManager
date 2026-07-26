# Shares & STRM export

## Sharing a library between instances

A **share** is a portable description of a channel folder — the files (names,
sizes, Telegram message ids) plus an invitation to the channel that holds them.
The bytes never leave Telegram: importing a share rebuilds the index on the
receiving instance and, when needed, joins the channel. Base path:
`/api/v1/shares`. All endpoints require a Telegram session.

### Export a share

```
GET /api/v1/shares/export?channelId=1290586824&folderId=…&name=My%20Music
```

`folderId` is optional (omit to share the whole channel). Returns a
`ShareFilesModel` — the payload the receiving instance imports:

```json
{
  "data": {
    "id": "1290586824",
    "name": "My Music",
    "chatName": "Fresh Electronic Music | EDM",
    "fileName": "My Music",
    "invitation": { "invitationHash": "AbC…", "invitationLink": "https://t.me/+AbC…" },
    "files": [ … ]
  }
}
```

Persist or transmit this JSON however you like (a file, a QR, a link).

### Import a share

```
POST /api/v1/shares/import
{ "share": { …the ShareFilesModel from an export… } }
```

The account joins the channel automatically when it is not a member yet and the
share carries an `invitationHash`. Import runs in the background and returns
`202 Accepted`. The imported files then appear under the shared collections and
can be downloaded with
`POST /api/v1/transfers/downloads` using `sharedCollectionId`.

### Manage stored shares

```
GET    /api/v1/shares            # list stored shared collections (paged; optional ?filter=)
GET    /api/v1/shares/{id}       # one shared collection
DELETE /api/v1/shares/{id}       # remove a shared collection from this server
```

A shared collection:

```json
{
  "id": "6650…",
  "name": "My Music",
  "description": null,
  "channelId": "1290586824",
  "collectionId": "shared_1290586824_…",
  "dateCreated": "…",
  "dateModified": "…"
}
```

## STRM export (media servers)

Export a channel folder as Emby/Kodi/Jellyfin **`.strm`** files. Each `.strm`
holds a URL that streams the file straight from Telegram, so a media server can
present the whole library without storing anything locally.

```
POST /api/v1/shares/strm?channelId=1290586824
{
  "path": "/movies/",
  "host": "https://tfm.example.com",   // base URL written into the files; defaults to the request host
  "destinationFolder": "strm/movies"    // optional
}
```

- **With `destinationFolder`** — the `.strm` files are written under the server's
  local root at that path; the response returns the folder.
- **Without `destinationFolder`** — the response returns a relative URL to a zip
  archive of the `.strm` files.

The exact stream URL flavour written into each file depends on
`strmStreamingMode` in the [configuration](system-and-config.md#streaming):

| Mode | Behaviour of the URL |
| --- | --- |
| `DirectStream` | Streams chunks from Telegram on demand; nothing cached. |
| `ProgressiveCache` | Streams immediately while a background download fills the on-disk cache. |
| `Preload` | Downloads the whole file to the cache before playback. |

Files smaller than `maxPreloadFileSizeInMb` are always fully preloaded
regardless of the mode.

Next: [system-and-config.md](system-and-config.md).
