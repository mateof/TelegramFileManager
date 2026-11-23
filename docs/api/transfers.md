# Transfers

Everything that moves bytes: pulling files out of Telegram onto the server,
pushing server files into Telegram, and controlling the queue.

**The golden rule:** these endpoints only *enqueue* work and return immediately
(`202 Accepted`). Progress is delivered over the SignalR hub at
`/hubs/transfers`. The REST snapshot endpoints below return the *same payload* as
the hub, so a client can render an initial state (or fall back to polling) and
then switch to live updates. Read [signalr.md](signalr.md) alongside this page.

Base path: `/api/v1/transfers`.

## Vocabulary

| Term | Meaning |
| --- | --- |
| **Download** | One file coming from Telegram to the server's local storage. |
| **Upload** | One file going from the server to Telegram. |
| **Task** (batch) | A whole folder operation. Spawns individual downloads/uploads. |
| **Queue** | Pending transfers not yet running. Downloads run `maxSimultaneousDownloads` at a time. |
| **Persisted transfer** | A transfer saved in MongoDB so it survives a restart and can auto-resume. |

## The transfer object

Shared by REST and the hub:

```json
{
  "id": "e2f1…",             // stable id: use it to pause/cancel/retry
  "kind": "download",         // download | upload | task
  "action": "Download",       // Download | Upload | Splitting | MD5 Calc | XxHash Calc | …
  "state": "Working",         // Pending | Working | Paused | Completed | Canceled | Error
  "isQueued": false,
  "name": "NIVIRO - Flashes.mp3",
  "path": "/downloads/edm",
  "channelId": "1290586824",
  "channelName": "Fresh Electronic Music | EDM",
  "size": 8123456,
  "transmitted": 4050000,
  "sizeText": "7.7 MB",
  "transmittedText": "3.9 MB",
  "progress": 49,
  "createdAt": "2026-07-23T18:00:00Z",
  "startedAt": "2026-07-23T18:00:01Z",
  "endedAt": null,
  "totalItems": null,         // batch tasks only
  "executedItems": null,
  "isUpload": null,
  "fromPath": null,
  "toPath": null
}
```

## Reading state (no session required)

### Full snapshot

```
GET /api/v1/transfers
```

```json
{
  "data": {
    "downloads": [ … ],
    "queuedDownloads": [ … ],
    "uploads": [ … ],
    "queuedUploads": [ … ],
    "tasks": [ … ],
    "summary": {
      "activeDownloads": 1, "queuedDownloads": 3,
      "activeUploads": 0, "queuedUploads": 0,
      "activeTasks": 1, "totalTasks": 1,
      "downloadSpeed": "4.2 MB/s", "uploadSpeed": "0 KB/s",
      "downloadBytesPerSecond": 4404019, "uploadBytesPerSecond": 0,
      "downloadsPaused": false, "isWorking": true
    }
  }
}
```

Identical to the hub's `TransfersSnapshot` message.

### Just the summary

```
GET /api/v1/transfers/summary
```

The `summary` block alone — cheap enough to poll for a status bar. Identical to
the hub's `TransferSummary`.

### Speed history (for charts)

```
GET /api/v1/transfers/speed-history
```

```json
{
  "data": {
    "download": [ { "time": "…", "bytesPerSecond": 4404019, "speedText": "4.2 MB/s", "activeFiles": ["…"] } ],
    "upload": [ … ],
    "intervalSeconds": 3,
    "windowSeconds": 600
  }
}
```

### Filtered lists

```
GET /api/v1/transfers/downloads?queued=false     # running downloads (or queued=true)
GET /api/v1/transfers/uploads?queued=false
GET /api/v1/transfers/tasks
GET /api/v1/transfers/{id}                         # one transfer, any kind
```

All paged.

## Starting transfers (session required)

### Download channel files to the server

```
POST /api/v1/transfers/downloads
{
  "channelId": "1290586824",
  "fileIds": ["694a…", "6949…"],       // file ids and/or folder ids (folders pulled recursively)
  "targetPath": "downloads/edm",         // relative to the local root; null keeps channel structure
  "sharedCollectionId": null             // set when downloading from an imported share
}
```

Returns `202` with `{ accepted, skipped }`. Each file then appears as its own
download on the hub.

### Upload server files to a channel

```
POST /api/v1/transfers/uploads
{
  "channelId": "1290586824",
  "localPaths": ["music/album1", "music/single.mp3"],   // relative to the local root; folders recursive
  "targetPath": "/music/"                                 // channel folder; defaults to root
}
```

The whole request becomes one **batch task** (visible under `tasks`), which
spawns one upload per file. Returns `202` with the `taskId`.

### Preview an upload (dry run)

```
POST /api/v1/transfers/uploads/preview
{
  "channelId": "1290586824",
  "localPaths": ["music/album1", "music/single.mp3"],
  "targetPath": "/music/",
  "findDuplicates": false,     // optional: also look for the same content elsewhere in the channel
  "maxItems": 500              // optional: cap of `items` (0..5000)
}
```

Same body as the upload, nothing is sent or queued and no Telegram session is
needed. The decisions come from the same planner the upload runs, so the
preview cannot disagree with what the upload would do against the same index:

| Action | Rule |
| --- | --- |
| `new` | Not indexed at that path: uploaded. |
| `replace` | Indexed with an older or equal date: uploaded, then the old entry is deleted. |
| `skip-empty` | Empty file: never sent. |
| `skip-newer` | Indexed with a newer date than the local file: kept, nothing sent. |
| `create-folder` / `existing-folder` | Folder entry created or reused. |

```jsonc
{
  "success": true,
  "data": {
    "targetPath": "/music/",
    "targetFolderExists": true,        // false: entries go under the channel root, as the upload does
    "newFiles": 12, "newBytes": 734003200,
    "replacedFiles": 1, "replacedBytes": 8388608,
    "filesToUpload": 13, "bytesToUpload": 742391808,
    "skippedFiles": 3, "skippedEmpty": 1, "skippedNewerInChannel": 2, "skippedBytes": 20971520,
    "foldersToCreate": 2, "existingFolders": 1,
    "duplicateCheck": false, "hashedFiles": 0, "hashedBytes": 0,
    "duplicates": [],
    "items": [
      { "localPath": "/music/album1/", "targetPath": "/music/album1", "isFile": false, "size": 0, "action": "existing-folder", "existingDateModified": null },
      { "localPath": "/music/album1/01.flac", "targetPath": "/music/album1/01.flac", "isFile": true, "size": 41943040, "action": "new", "existingDateModified": null }
    ],
    "itemsTruncated": false,
    "missingLocalPaths": []
  }
}
```

The counters always cover the whole tree; `items` is cut at `maxItems`
(`itemsTruncated: true`).

**Duplicates by content.** With `findDuplicates: true` the server reads and
hashes the local files that would be uploaded **and** whose size equals the size
of some channel file with a stored hash (`hashedFiles`/`hashedBytes` say how
much it read). Each hit in `duplicates` lists the channel files with the same
XXHash and size at another path:

```jsonc
{ "localPath": "/music/single.mp3", "targetPath": "/music/single.mp3", "size": 5242880,
  "xxHash": "9A3F0C1B2D4E5F60",
  "matches": [ { "id": "665f…", "filePath": "/old/single.mp3", "size": 5242880 } ] }
```

Only files uploaded with **CheckHash** enabled carry a hash, so channels filled
another way report no duplicates. The preview does not skip anything: it is
information for deciding whether to upload.

### Download media from raw messages

```
POST /api/v1/transfers/messages
{ "chatId": 1290586824, "messageIds": [88213, 88214], "targetPath": "downloads" }
```

Works on any chat, indexed or not — this is how you save a file straight from a
[message list](channels.md#message-history).

## Controlling the queue

| Endpoint | Effect |
| --- | --- |
| `POST /api/v1/transfers/downloads/pause` | Pause all downloads; running ones go back to the front of the queue. |
| `POST /api/v1/transfers/downloads/resume` | Resume the download queue. |
| `POST /api/v1/transfers/downloads/stop` | Stop everything and empty the download queue. |
| `POST /api/v1/transfers/{id}/pause` | Pause one running download. |
| `POST /api/v1/transfers/{id}/cancel` | Cancel one transfer (a task also cancels its children). |
| `POST /api/v1/transfers/{id}/retry` | Retry a paused/cancelled/failed download or task. |
| `POST /api/v1/transfers/clear?scope=all` | Remove finished entries (`downloads`/`uploads`/`tasks`/`all`). |
| `POST /api/v1/transfers/queue/clear?scope=all` | Empty a queue without touching running transfers. |
| `DELETE /api/v1/transfers/{id}` | Remove one entry from the lists ("remove from list"): finished, paused or queued. A running transfer answers `409` (cancel it first). |

The pause/resume/stop endpoints return the fresh `summary`; the per-item ones
return an empty success. Watch the hub for the state change either way.

`clear` also takes an optional `states` filter, so a client can tidy up the
successes and leave the failures on screen to look at:

```
POST /api/v1/transfers/clear?scope=all&states=completed
POST /api/v1/transfers/clear?scope=all&states=completed,failed
```

Valid states are `completed`, `error` (or `failed`), `canceled` (or
`cancelled`), `paused` and `pending`; `working` is rejected, since a running
transfer is cancelled rather than cleared. Without `states` the endpoint
removes every finished entry, as it always did.

## Persisted transfers (survive restarts)

When `enableTaskPersistence` is on (default), transfers are written to MongoDB.
Once the Telegram session is ready after a restart, records left `working` by
the previous process are set back to `pending` and, if `autoResumeOnStartup`
is on (default), every `pending`/`paused` transfer is queued again:

- downloads continue the partial file from the last confirmed byte (split
  files from the first unfinished part);
- batch uploads are rebuilt from the selected items and skip the files already
  in the destination; parts of a split file that already reached Telegram are
  reused, not sent twice;
- a task whose source is gone (local folder deleted, Telegram message deleted)
  is marked `error` with the reason instead of blocking the queue.

`error` tasks are not retried automatically; resume them by hand (same code
path as the automatic resume):

```
GET    /api/v1/transfers/persisted                    # list (paged)
POST   /api/v1/transfers/persisted/{internalId}/resume # resume / retry one
DELETE /api/v1/transfers/persisted/{internalId}       # forget one
DELETE /api/v1/transfers/persisted                    # forget all
```

The resume answer says what happened: `resumed` (queued again), `action`
(`ResumeDownload`, `ResumeSplitDownload`, `ResumeBatchUpload`, `Fail` or `Skip`)
and a `message` (for instance "Already queued or running").

```json
{
  "data": [
    {
      "id": "6650…", "internalId": "e2f1…",
      "type": "Download", "state": "Working",
      "name": "big-movie.mkv", "channelId": "…", "channelName": "…",
      "totalSize": 4123456789, "transmittedBytes": 1200000000, "progress": 29,
      "sourcePath": null, "destinationPath": "downloads",
      "creationDate": "…", "lastUpdated": "…",
      "retryCount": 0, "lastError": null
    }
  ]
}
```

## Recommended client flow

```ts
// 1. Connect to the hub and render the first snapshot it pushes on connect.
hub.on("TransfersSnapshot", render);
hub.on("TransferSummary", renderStatusBar);
await hub.start();

// 2. Kick off work.
await api.post("/api/v1/transfers/downloads", {
  channelId, fileIds, targetPath: "downloads/edm"
});

// 3. Do nothing else — progress arrives on the hub.
// 4. Let the user pause/cancel by calling the control endpoints;
//    the resulting state change also arrives on the hub.
```

If you cannot use WebSockets at all, poll `GET /api/v1/transfers/summary` every
second and `GET /api/v1/transfers` on demand — same payloads.

Next: [signalr.md](signalr.md).
