# Channels

A **channel** in this API is any Telegram peer the signed-in account can see: a
broadcast channel, a group, or a one-to-one chat. The channel id (a number like
`1290586824`) is the key you pass everywhere.

When the app **indexes** a channel it walks its message history and records every
file in a MongoDB database named after the channel id. That index is what the
[files](files.md) endpoints browse — fast, paged, searchable — without hitting
Telegram again. A channel with an index is called *saved*.

All endpoints here require a Telegram session ([authentication.md](authentication.md)).

---

## List channels

```
GET /api/v1/channels
```

| Query | Default | Notes |
| --- | --- | --- |
| `onlySaved` | `false` | Only channels that already have a local index. |
| `favoritesOnly` | `false` | Only favourites. |
| `hiddenOnly` | `false` | Only hidden channels. |
| `includeHidden` | `false` | Include hidden channels even when "show hidden channels" is off. |
| `search` | – | Case-insensitive substring on the name. |

Hidden channels are **excluded by default** unless the `showHiddenChannels`
config option is on, or you pass `includeHidden=true` / `hiddenOnly=true`.
| `sortBy` | `name` | `name` or `id`. |
| `sortDescending` | `false` | |
| `page`, `pageSize` | 1, 50 | |

```json
{
  "success": true,
  "data": [
    {
      "id": 1290586824,
      "name": "Fresh Electronic Music | EDM",
      "type": "channel",
      "isOwner": false,
      "isFavorite": true,
      "isHidden": false,
      "imageUrl": "/api/channel/image/1290586824",
      "hasDatabase": true
    }
  ],
  "page": { "page": 1, "pageSize": 50, "totalItems": 42, "totalPages": 1, "hasNext": false, "hasPrevious": false }
}
```

`type` is `channel`, `group` or `chat`. `imageUrl` is relative; the avatar bytes
are served by `GET /api/v1/channels/{id}/image` (below).

> **Tip:** For a file-manager UI, start with `?onlySaved=true` — those are the
> channels you can actually browse. Offer the full list (`onlySaved=false`) when
> the user wants to index a new channel.

## Channels grouped by folder

```
GET /api/v1/channels/folders
```

Mirrors Telegram's chat folders (a.k.a. chat filters):

```json
{
  "data": {
    "folders": [
      { "id": 2, "title": "Music", "iconEmoji": "🎵", "channels": [ … ], "channelCount": 7 }
    ],
    "ungrouped": [ … ],
    "totalChannels": 42
  }
}
```

## Favourites

```
GET    /api/v1/channels/favorites          # list (optional ?refresh=false)
POST   /api/v1/channels/{id}/favorite       # add
DELETE /api/v1/channels/{id}/favorite       # remove
```

Favourites are stored in the app configuration and shared with the web UI.

## Hidden channels

```
GET    /api/v1/channels/hidden              # list the hidden channels
POST   /api/v1/channels/{id}/hidden         # hide a channel
DELETE /api/v1/channels/{id}/hidden         # unhide a channel
```

Hidden channels are stored in the app configuration and shared with the web UI.
By default they are excluded from `GET /api/v1/channels` and the folders list;
turn on the `showHiddenChannels` config option (or pass `includeHidden=true` /
`hiddenOnly=true`) to see them. Each channel DTO carries an `isHidden` flag.

## Channel details

```
GET /api/v1/channels/{id}
```

Adds indexed-content statistics on top of the basic fields:

```json
{
  "data": {
    "id": 1290586824,
    "name": "Fresh Electronic Music | EDM",
    "type": "channel",
    "isOwner": false,
    "isFavorite": true,
    "isHidden": false,
    "imageUrl": "/api/channel/image/1290586824",
    "hasDatabase": true,
    "fileCount": 3120,
    "folderCount": 12,
    "totalSize": 41231234567,
    "totalSizeText": "38.4 GB",
    "audioCount": 3040,
    "videoCount": 5,
    "photoCount": 40,
    "documentCount": 35,
    "isRefreshing": false,
    "canRefresh": true
  }
}
```

`canRefresh` is `false` for channels you own unless
`enableRefreshOwnChannels` is set in the configuration.

## Create a channel

```
POST /api/v1/channels
{ "title": "My Backup", "about": "Personal file storage", "createDatabase": true }
```

Creates a Telegram channel owned by the account and, by default, its local
index at the same time so you can immediately use it as an upload target.
Returns `201` with the new channel.

## The local index (database)

```
POST   /api/v1/channels/{id}/database   # create the index for an existing channel
DELETE /api/v1/channels/{id}/database   # drop the index (files stay in Telegram)
```

Dropping the index only makes the app forget the folder structure; nothing is
deleted from Telegram. Rebuild it with a refresh.

## Leave or delete a channel

```
POST /api/v1/channels/{id}/leave
{ "deleteLocalDatabase": true, "deleteOnTelegram": false }
```

- `deleteOnTelegram: false` (default) — just leave the channel.
- `deleteOnTelegram: true` — delete the channel for everyone. **Owner only**
  (otherwise `403 forbidden`), irreversible, and it destroys the files stored
  inside.
- `deleteLocalDatabase: true` — also drop the local index.

## Refresh (index new files)

```
POST /api/v1/channels/{id}/refresh
{
  "includeDocuments": true,
  "includeAudio": true,
  "includeVideo": true,
  "includePhotos": true,
  "force": false
}
```

Scans the channel on Telegram and adds files that are not indexed yet. This is
how a channel becomes *saved* and how new uploads by others become visible.

- Returns `202 Accepted` immediately; the scan runs in the background and can
  take minutes on large channels.
- Only new files are added, so repeated calls are safe (idempotent in effect).
- `409 already_running` if a scan is already in progress.

Poll the state with:

```
GET /api/v1/channels/{id}/refresh      -> true | false
```

(kept for compatibility; the progress endpoint below has the details) and watch
`/hubs/transfers` for the download/index activity it generates.

### Incremental scans (per media type)

Each media type (`documents`, `audio`, `video`, `photos`) keeps its own
high-water mark in the channel database (collection `refresh_marks`): the
highest message id up to which *every* message of that type is known to be in
the index. A normal refresh only asks Telegram for messages above the mark of
each selected type, so:

- a type selected for the first time (for example photos on a channel that was
  only ever refreshed for audio) is scanned in full once, then incrementally;
- a message that failed to index, or that was not reached because the refresh
  was canceled, is picked up by the next refresh: the mark never moves past a
  message that is not in the index;
- channels indexed before this existed have no marks, so their first refresh
  after the upgrade scans every selected type in full once (files already
  indexed are skipped, nothing is duplicated).

`force: true` keeps its meaning: the selected types are rescanned in full and
their marks recomputed. Deleting or importing the channel database drops the
marks with it.

### Live progress

```
GET /api/v1/channels/{id}/refresh/progress
```

`data: null` when no refresh is running. While one runs:

```json
{
  "running": true,
  "phase": "searching",
  "startedAt": "2026-08-31T16:38:02",
  "filters": [
    { "type": "documents", "fetched": 400, "total": 1250, "done": false },
    { "type": "audio", "fetched": 12, "total": 8031, "done": true }
  ],
  "newFiles": 0,
  "indexed": 0,
  "cancelRequested": false
}
```

- `phase`: `searching` (paging Telegram), `indexing` (writing the new files),
  then briefly `done` before the run is released.
- `filters` lists the selected types. `fetched` is the number of messages read
  so far; `total` is what Telegram reports for the whole channel (`null` until
  the first page arrives). An incremental scan stops at the mark, so it often
  ends (`done: true`) far below `total`.
- `newFiles` is known once the search is over; `indexed` counts the new files
  written so far.
- Poll about once a second at most.

### Cancel

```
DELETE /api/v1/channels/{id}/refresh
```

- `202 Accepted` when a cancel was requested, `404 not_found` when nothing runs.
- The refresh stops after the Telegram page or insert batch in flight (usually
  within a few seconds); until then `GET .../refresh` still answers `true` and
  `.../refresh/progress` shows `cancelRequested: true`. A new refresh can only
  start once it has stopped.
- Canceled while searching Telegram: nothing is indexed. Canceled while
  indexing: the batches already written stay (1000 files each) and the index is
  consistent. Either way the result reports `canceled: true` with the counts of
  what got in, and the per-type marks only cover what is actually indexed.

### What the scan added

Once the poll above answers `false`, read the breakdown of the finished scan:

```
GET /api/v1/channels/{id}/refresh/result
```

```json
{
  "channelId": "1234567890",
  "startedAt": "2026-08-31T16:38:02Z",
  "finishedAt": "2026-08-31T16:40:57Z",
  "failed": false,
  "canceled": false,
  "photos": 0,
  "videos": 12,
  "audios": 3,
  "documents": 5,
  "total": 20
}
```

- `data: null` when the channel has not been refreshed since the server
  started — the result is a report on this run, not history.
- `failed: true` means the scan threw; the counts are what it managed to index
  before giving up.
- `failed: true` is also set when one media type's search failed: the other
  types are still indexed, the failed one keeps its mark and is retried next time.
- `canceled: true` means the refresh was canceled (see above); the counts are
  what was indexed before it stopped.
- A scan that found nothing new answers with every count at `0`, not `null`.

## Message history

```
GET /api/v1/channels/{id}/messages?limit=30&offset=0&onlyMedia=true
```

Reads the raw recent history straight from Telegram (does not use the index), so
it works even for channels that were never indexed. Use it to build a
"messages" view and hand message ids to
`POST /api/v1/transfers/messages` to download their attachments.

```json
{
  "data": [
    {
      "id": 88213,
      "date": "2026-07-20T18:04:11Z",
      "text": "New release!",
      "hasMedia": true,
      "mediaType": "audio",
      "fileName": "NIVIRO - Flashes.mp3",
      "fileSize": 8123456,
      "mimeType": "audio/mpeg",
      "from": "Alice"
    }
  ]
}
```

`limit` is clamped to 1–100. `mediaType` is `photo`, `video`, `audio`,
`document` or null.

## Avatar

```
GET /api/v1/channels/{id}/image
```

Returns the channel avatar as `image/jpeg`, or `404` when there is none. Because
`<img>` cannot send headers, pass the key in the query string:
`/api/v1/channels/{id}/image?apiKey=…`.

## Invitations

```
GET  /api/v1/channels/{id}/invitation   # get (or generate) the invite link
POST /api/v1/channels/join?hash=…        # join using an invite hash
```

The `hash` is the part after `t.me/+` or `joinchat/` in an invite link. Joining
is also done automatically when you import a [share](shares.md) whose channel you
are not a member of.

Next: [files.md](files.md).
