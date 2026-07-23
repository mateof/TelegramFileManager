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

The pause/resume/stop endpoints return the fresh `summary`; the per-item ones
return an empty success. Watch the hub for the state change either way.

## Persisted transfers (survive restarts)

When `enableTaskPersistence` is on (default), transfers are written to MongoDB.
On startup the app reloads them and, if `autoResumeOnStartup` is on, resumes
them from the last confirmed byte.

```
GET    /api/v1/transfers/persisted                 # list (paged)
DELETE /api/v1/transfers/persisted/{internalId}    # forget one
DELETE /api/v1/transfers/persisted                 # forget all
```

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
