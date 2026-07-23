# Playlists

Playlists mix **Telegram-hosted tracks** and **local files** and are shared with
the web player and the audio app. Base path: `/api/v1/playlists`.

Reading and editing playlists does not require a Telegram session; downloading a
playlist to disk does.

## Model

```json
{
  "id": "6a58f4e80d200494f33016f3",
  "name": "Dance 26",
  "description": "",
  "tracks": [
    {
      "fileId": "694a7ec440073c1c7e42f678",
      "channelId": "1290586824",
      "channelName": "Fresh Electronic Music | EDM",
      "fileName": "NIVIRO - Flashes.mp3",
      "filePath": "/music/rock/",
      "fileType": ".mp3",
      "fileSize": 8123456,
      "order": 0,
      "directUrl": null,
      "isLocalFile": false,
      "dateAdded": "2026-07-01T12:00:00Z"
    }
  ],
  "dateCreated": "2026-07-01T12:00:00Z",
  "dateModified": "2026-07-10T09:30:00Z",
  "trackCount": 1
}
```

A track is either:

- a **channel track** — `channelId` + `fileId` reference an indexed file, or
- a **local track** — `directUrl` points at a local/streamable file
  (`isLocalFile` is then `true`).

To build a play URL for a channel track, resolve the file with
`GET /api/v1/channels/{channelId}/files/{fileId}` and use its `streamUrl`. For a
local track use `directUrl`.

## CRUD

```
GET    /api/v1/playlists                 # list (paged; sortBy: name | date | tracks)
GET    /api/v1/playlists/{id}            # one playlist, tracks ordered
POST   /api/v1/playlists                 # create -> 201
PUT    /api/v1/playlists/{id}            # update name/description/tracks
DELETE /api/v1/playlists/{id}            # delete
```

Create body (minimal):

```json
{ "name": "My mix", "description": "optional" }
```

`PUT` applies only the fields you send: pass `name`/`description` to rename, or a
non-empty `tracks` array to replace the whole track list.

## Tracks

```
POST   /api/v1/playlists/{id}/tracks               # append a track
DELETE /api/v1/playlists/{id}/tracks/{fileId}      # remove a track
PUT    /api/v1/playlists/{id}/tracks/order         # reorder
```

Append body — a `PlaylistTrackModel`; at minimum `fileId` (channel track) or
`directUrl` (local track):

```json
{
  "fileId": "694a…",
  "channelId": "1290586824",
  "channelName": "Fresh Electronic Music | EDM",
  "fileName": "NIVIRO - Flashes.mp3",
  "fileType": ".mp3",
  "fileSize": 8123456
}
```

Reorder body — the file ids in the desired order:

```json
["694b…", "694a…", "694c…"]
```

Each of these returns the updated playlist, so a client can re-render from the
response.

## Download a whole playlist (session required)

```
POST /api/v1/playlists/{id}/download?destinationFolder=Dance%2026
```

Downloads every track to the server's local storage under
`destinationFolder` (defaults to the playlist name). Runs in the background,
returns `202 Accepted`, and reports on the `/hubs/transfers` hub like any other
download. Local tracks are copied, channel tracks are pulled from Telegram.

Next: [shares.md](shares.md).
