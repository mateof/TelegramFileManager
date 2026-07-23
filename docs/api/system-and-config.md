# System, diagnostics & configuration

Health, metrics, logs, database maintenance, application settings and the WebDAV
bridge. None of these require a Telegram session (they only need the API key).

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
    "webDavRunning": false,
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
| `webDav` | The WebDAV bridge block (host, ports, running state). |

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

## WebDAV bridge

Exposes channels as WebDAV shares so media servers can mount a library.

```
GET  /api/v1/config/webdav          # state
POST /api/v1/config/webdav/start    # start the bridge
POST /api/v1/config/webdav/stop     # stop the bridge
```

Once running, a channel is reachable at
`http://<host>:<externalPort>/<channelId>/`. `409 already_running` if you start
it twice. Change the host/ports via `PATCH /api/v1/config`
(`webDavHost`, `webDavInternalPort`, `webDavExternalPort`).

Next: [reference.md](reference.md).
