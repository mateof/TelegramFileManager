# Real-time updates (SignalR)

Download and upload progress is streamed over a **SignalR hub** so clients never
have to poll. This is the recommended way to keep a transfer screen live.

```
Hub URL: /hubs/transfers
Protocol: SignalR (WebSockets, with automatic fallback to SSE / long polling)
```

`GET /api/v1/system/info` returns the hub path in `transfersHubPath`, so a client
can discover it instead of hard-coding.

## Authentication

The hub is behind the same API key as the REST API. SignalR clients cannot set a
custom header on the WebSocket handshake, so pass the key as the standard
`access_token` query parameter:

```
/hubs/transfers?access_token=YOUR_API_KEY
```

The official SignalR clients do this for you via the `accessTokenFactory`
option (see the samples below). When no API key is configured on the server, the
hub is open.

## Server → client messages

Subscribe to these. Every payload matches a REST DTO exactly, so the same render
code works for a snapshot fetched over REST and one pushed over the hub.

| Message | Payload | When |
| --- | --- | --- |
| `TransfersSnapshot` | `TransfersSnapshotDto` | On connect, and whenever the set of transfers or their progress changes (coalesced to at most one every 500 ms, with a guaranteed final push). |
| `TransferSummary` | `TransferSummaryDto` | On every change — cheaper and more frequent than the snapshot. Ideal for a status bar/badge. |
| `SpeedHistoryPoint` | `(SpeedPointDto download, SpeedPointDto upload)` | One download + one upload sample every few seconds, for live charts. |

`TransfersSnapshotDto`, `TransferSummaryDto` and `SpeedPointDto` are documented in
[transfers.md](transfers.md) and [reference.md](reference.md).

## Client → server methods

Invoke these on the connection when you need them.

| Method | Returns | Purpose |
| --- | --- | --- |
| `GetSnapshot()` | `TransfersSnapshotDto` | Fetch the current snapshot on demand. |
| `GetSummary()` | `TransferSummaryDto` | Fetch the current summary. |
| `GetSpeedHistory()` | `SpeedHistoryDto` | Fetch the retained speed history (to prime a chart). |
| `MuteSnapshots()` / `UnmuteSnapshots()` | – | Stop/resume full snapshots while keeping summaries. Good for a backgrounded app that only needs a progress badge. |
| `MuteSpeedHistory()` / `UnmuteSpeedHistory()` | – | Stop/resume speed samples. |

A new connection is subscribed to all three streams and receives a
`TransfersSnapshot` immediately.

## Controlling transfers

The hub is **read/subscribe only**. To pause, resume, cancel, retry or start a
transfer, call the REST endpoints in [transfers.md](transfers.md). The resulting
state change is pushed back over the hub, so your UI stays consistent without
extra work.

## JavaScript / TypeScript client

```bash
npm install @microsoft/signalr
```

```ts
import * as signalR from "@microsoft/signalr";

const API_KEY = "your-secret";
const BASE = "http://192.168.1.50:5257";

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${BASE}/hubs/transfers`, {
    accessTokenFactory: () => API_KEY,   // sent as ?access_token=…
  })
  .withAutomaticReconnect()
  .build();

connection.on("TransfersSnapshot", (snapshot) => {
  renderDownloads(snapshot.downloads, snapshot.queuedDownloads);
  renderUploads(snapshot.uploads, snapshot.queuedUploads);
  renderTasks(snapshot.tasks);
  renderStatusBar(snapshot.summary);
});

connection.on("TransferSummary", (summary) => renderStatusBar(summary));

connection.on("SpeedHistoryPoint", (download, upload) => {
  chart.push(download.time, download.bytesPerSecond, upload.bytesPerSecond);
});

await connection.start();

// Optional: prime the chart with the retained history.
const history = await connection.invoke("GetSpeedHistory");
chart.load(history);
```

## C# / .NET client (also usable from .NET MAUI)

```bash
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl($"{BASE}/hubs/transfers", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult<string?>(API_KEY);
    })
    .WithAutomaticReconnect()
    .Build();

connection.On<TransfersSnapshotDto>("TransfersSnapshot", RenderAll);
connection.On<TransferSummaryDto>("TransferSummary", RenderStatusBar);
connection.On<SpeedPointDto, SpeedPointDto>("SpeedHistoryPoint",
    (down, up) => Chart.Push(down, up));

await connection.StartAsync();
```

## Kotlin / Android client

```kotlin
// implementation "com.microsoft.signalr:signalr:8.+"
val hub = HubConnectionBuilder
    .create("$BASE/hubs/transfers")
    .withAccessTokenProvider(Single.defer { Single.just(API_KEY) })
    .build()

hub.on("TransferSummary", { summary -> renderStatusBar(summary) }, TransferSummaryDto::class.java)
hub.on("TransfersSnapshot", { s -> renderAll(s) }, TransfersSnapshotDto::class.java)
hub.start().blockingAwait()
```

## Reconnection

Use `withAutomaticReconnect()` (or the platform equivalent). On every
(re)connect the hub immediately pushes a fresh `TransfersSnapshot`, so there is
no gap to fill manually — just re-render from it. If you disabled reconnect,
call `GetSnapshot()` after reconnecting.

## Choosing between the hub and polling

| Situation | Use |
| --- | --- |
| A live transfers screen | The hub. |
| A status badge in the background | `TransferSummary` on the hub (or `MuteSnapshots()` to reduce traffic). |
| No WebSocket support at all (locked-down proxy) | SignalR falls back to SSE/long polling automatically; if even that is blocked, poll `GET /api/v1/transfers/summary`. |

Next: [local-files.md](local-files.md).
