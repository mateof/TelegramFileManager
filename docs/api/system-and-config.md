# System, diagnostics & configuration

Health, metrics, logs, database maintenance and application settings. None of
these require a Telegram session (they only need the API key). The native WebDAV
endpoint is documented separately in [webdav.md](webdav.md).

## System

Base path: `/api/v1/system`.

### Health & readiness

```
GET /api/v1/system/ping     # -> "pong". Liveness + API-key check.
GET /api/v1/system/info     # server identity and readiness
```

`info` is the natural first call of a client:

```json
{
  "data": {
    "product": "TelegramFileManager",
    "version": "3.7.0.0",
    "apiVersion": "1.0",
    "serverTimeUtc": "2026-07-23T17:32:07Z",
    "mongoConnected": true,
    "telegramConfigured": true,
    "telegramAuthenticated": false,
    "setupComplete": true,
    "transfersHubPath": "/hubs/transfers",
    "requiresApiKey": true
  }
}
```

### Setup status

```
GET /api/v1/system/setup
```

Progress of the first-run wizard (`currentStep` is `Complete`,
`MongoDbRequired` or `TelegramRequired`). When it is not `Complete`, the
session-bound endpoints answer `503 setup_required`; finish setup from the web UI
at `/setup`.

### Metrics

```
GET /api/v1/system/metrics
```

CPU, memory and disk usage of the server (a `SystemMetricsDto`) — good for a
dashboard. Poll at whatever cadence you need.

### Logs

Logs live in the `TFM_Logs` MongoDB database. When MongoDB is not configured
these answer `503`.

```
GET    /api/v1/system/logs?level=Error&search=timeout&fromDate=…&toDate=…&page=1&pageSize=50
GET    /api/v1/system/logs/loggers      # distinct logger names
GET    /api/v1/system/logs/versions     # app versions present
DELETE /api/v1/system/logs?daysToKeep=30
```

A log entry:

```json
{
  "id": "…", "timestamp": "…", "level": "Error",
  "message": "…", "logger": "TelegramDownloader.Data.TelegramService",
  "exception": "…", "version": "3.7.0.0"
}
```

### Database maintenance

The channel indexes are MongoDB databases; these inspect and repair them.

```
GET  /api/v1/system/databases                          # list indexes + size
GET  /api/v1/system/databases/{channelId}/analysis     # check folder-path integrity
POST /api/v1/system/databases/{channelId}/repair       # fix broken folder paths
POST /api/v1/system/maintenance/cleanup-tasks          # drop stale persisted tasks
```

Older versions could store inconsistent folder paths, which shows up as folders
that look empty. `analysis` reports the problem; `repair` fixes it and returns
the number of repaired entries. `databases` returns:

```json
{
  "data": [
    { "channelId": "1290586824", "channelName": "Fresh Electronic Music | EDM",
      "sizeInBytes": 5242880, "sizeText": "5.0 MB", "documentCount": 3132,
      "createdAt": "…", "lastModified": "…" }
  ]
}
```

## Configuration

Base path: `/api/v1/config`. Settings are **global and shared with the web UI**.

### Read

```
GET /api/v1/config
```

Returns the full `AppConfigDto`. Highlights:

| Field | Meaning |
| --- | --- |
| `maxSimultaneousDownloads` | How many downloads run at once. |
| `splitSize` | Threshold (GB) above which uploads are split into multiple messages. |
| `checkHash` | Compute MD5/xxHash on upload. |
| `strmStreamingMode` | `DirectStream` / `ProgressiveCache` / `Preload` (see [shares](shares.md#strm-export-media-servers)). |
| `enableTaskPersistence`, `autoResumeOnStartup` | Persist transfers and resume them after a restart. |
| `parallelTransfers` (1–16) | Chunks requested in parallel per transfer; raises throughput. |
| `enableMultiConnectionDownloads`, `downloadConnections` (2–8) | Multi-connection downloads for large files. |
| `enableMemorySplitUpload`, `memorySplitSizeGB` | Split large uploads in memory instead of on disk. |
| `favouriteChannels` | Ids of favourite channels. |
| `hiddenChannels` | Ids of channels hidden from the channel lists (read-only here; change via `POST/DELETE /api/v1/channels/{id}/hidden`). |
| `showHiddenChannels` | When `true`, hidden channels are still shown in the channel lists. |

<a id="streaming"></a>

### Update (partial)

```
PATCH /api/v1/config
{ "maxSimultaneousDownloads": 3, "parallelTransfers": 8, "strmStreamingMode": "ProgressiveCache" }
```

Only the fields you send are applied; everything else keeps its value. The
response returns the **effective** configuration after server-side clamping — for
example `memorySplitSizeGB` is capped by the account's Telegram limit (4 GB
Premium, 2 GB otherwise) and by `splitSize`, and `parallelTransfers`,
`downloadConnections`, `multiConnectionBlockSizeMB` are clamped to their valid
ranges. An unknown `strmStreamingMode` yields `400 invalid_request`.

## WebDAV

Each channel is exposed as a **native, always-on read/write WebDAV share** at
`/webdav/{channelId}/` — there is no bridge to start or stop. This is what a
Synology Hyper Backup task (or rclone, davfs2, Windows Explorer…) mounts.
Authentication is HTTP Basic, configured from the **Config** page
(`WebDAV User` / `WebDAV Password`).

See **[webdav.md](webdav.md)** for the full guide: URL layout, supported verbs,
`curl` examples and Hyper Backup setup.

> The old Python WebDAV proxy (`config/webdav/*` endpoints, `webDav` config block)
> was removed in favour of this native implementation.

Next: [reference.md](reference.md).
