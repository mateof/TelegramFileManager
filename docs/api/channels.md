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
| `search` | – | Case-insensitive substring on the name. |
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

and watch `/hubs/transfers` for the download/index activity it generates.

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
