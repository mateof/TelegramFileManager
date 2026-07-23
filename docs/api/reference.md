# Reference

The authoritative, always-current reference is the OpenAPI document the running
server serves at **`/swagger/api-v1/swagger.json`** (browsable at
**`/api-docs`**). This page is a hand-maintained companion.

## Conventions recap

- Base path: `/api/v1`. Real-time hub: `/hubs/transfers`.
- Every request carries the API key (`X-Api-Key` header, or `apiKey` /
  `access_token` query). See [getting-started.md](getting-started.md).
- Every response is the envelope `{ success, data, error, message, page }`.
- **Sess.** in the table below marks endpoints that also require a signed-in
  Telegram account (`401 not_logged_in` otherwise).

## Endpoint index

### Auth

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/auth/status` | | Current authentication state. |
| GET | `/api/v1/auth/me` | | Signed-in Telegram user (`401` if none). |
| POST | `/api/v1/auth/login` | | Advance the phone login one step. |
| POST | `/api/v1/auth/qr` | | Start a QR login session. |
| GET | `/api/v1/auth/qr/{sessionId}` | | Poll a QR session. |
| POST | `/api/v1/auth/qr/{sessionId}/password` | | Supply the 2FA password to a QR session. |
| DELETE | `/api/v1/auth/qr/{sessionId}` | | Cancel a QR session. |
| POST | `/api/v1/auth/logout` | | Sign out. |

### Channels

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/channels` | ✓ | List chats (`onlySaved`, `favoritesOnly`, `search`, sort, paging). |
| GET | `/api/v1/channels/folders` | ✓ | Chats grouped by Telegram folder. |
| GET | `/api/v1/channels/favorites` | ✓ | Favourite channels. |
| POST | `/api/v1/channels/{id}/favorite` | ✓ | Add favourite. |
| DELETE | `/api/v1/channels/{id}/favorite` | ✓ | Remove favourite. |
| GET | `/api/v1/channels/{id}` | ✓ | Details + indexed-content stats. |
| POST | `/api/v1/channels` | ✓ | Create a channel. |
| POST | `/api/v1/channels/{id}/database` | ✓ | Create the local index. |
| DELETE | `/api/v1/channels/{id}/database` | ✓ | Drop the local index. |
| POST | `/api/v1/channels/{id}/leave` | ✓ | Leave / delete a channel. |
| POST | `/api/v1/channels/{id}/refresh` | ✓ | Index new files (background). |
| GET | `/api/v1/channels/{id}/refresh` | ✓ | Is a refresh running? |
| GET | `/api/v1/channels/{id}/messages` | ✓ | Recent message history. |
| GET | `/api/v1/channels/{id}/image` | ✓ | Channel avatar (image bytes). |
| GET | `/api/v1/channels/{id}/invitation` | ✓ | Invitation link. |
| POST | `/api/v1/channels/join` | ✓ | Join via invite hash. |

### Files (channel storage)

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/channels/{channelId}/files` | ✓ | Browse a folder. |
| GET | `/api/v1/channels/{channelId}/files/search` | ✓ | Search a subtree by name. |
| GET | `/api/v1/channels/{channelId}/files/{fileId}` | ✓ | One entry. |
| GET | `/api/v1/channels/{channelId}/files/stats` | ✓ | Recursive stats of a subtree. |
| POST | `/api/v1/channels/{channelId}/files/folders` | ✓ | Create a folder. |
| PUT | `/api/v1/channels/{channelId}/files/{fileId}/name` | ✓ | Rename. |
| POST | `/api/v1/channels/{channelId}/files/delete` | ✓ | Delete (also frees Telegram storage). |
| POST | `/api/v1/channels/{channelId}/files/copy` | ✓ | Copy within the channel. |
| POST | `/api/v1/channels/{channelId}/files/move` | ✓ | Move within the channel. |
| POST | `/api/v1/channels/{channelId}/files/upload` | ✓ | Direct multipart upload to Telegram. |
| GET | `/api/v1/channels/{channelId}/files/export` | ✓ | Export the index (JSON). |
| POST | `/api/v1/channels/{channelId}/files/import` | ✓ | Import an index. |

### Transfers

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/transfers` | | Full snapshot (= hub `TransfersSnapshot`). |
| GET | `/api/v1/transfers/summary` | | Counters + speeds (= hub `TransferSummary`). |
| GET | `/api/v1/transfers/speed-history` | | Speed samples for charts. |
| GET | `/api/v1/transfers/downloads` | | List downloads (`queued`). |
| GET | `/api/v1/transfers/uploads` | | List uploads (`queued`). |
| GET | `/api/v1/transfers/tasks` | | List batch tasks. |
| GET | `/api/v1/transfers/{id}` | | One transfer. |
| POST | `/api/v1/transfers/downloads` | ✓ | Download channel files to the server. |
| POST | `/api/v1/transfers/uploads` | ✓ | Upload server files to a channel. |
| POST | `/api/v1/transfers/messages` | ✓ | Download media from raw messages. |
| POST | `/api/v1/transfers/downloads/pause` | | Pause all downloads. |
| POST | `/api/v1/transfers/downloads/resume` | | Resume the download queue. |
| POST | `/api/v1/transfers/downloads/stop` | | Stop + empty the download queue. |
| POST | `/api/v1/transfers/{id}/pause` | | Pause one download. |
| POST | `/api/v1/transfers/{id}/cancel` | | Cancel one transfer. |
| POST | `/api/v1/transfers/{id}/retry` | | Retry a transfer. |
| POST | `/api/v1/transfers/clear` | | Remove finished entries (`scope`). |
| POST | `/api/v1/transfers/queue/clear` | | Empty a queue (`scope`). |
| GET | `/api/v1/transfers/persisted` | | List persisted transfers. |
| DELETE | `/api/v1/transfers/persisted/{internalId}` | | Delete one persisted transfer. |
| DELETE | `/api/v1/transfers/persisted` | | Delete all persisted transfers. |

### Local files

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/local` | | Browse a directory. |
| GET | `/api/v1/local/info` | | Entry metadata. |
| GET | `/api/v1/local/size` | | Recursive size + type breakdown. |
| POST | `/api/v1/local/folders` | | Create a directory. |
| POST | `/api/v1/local/rename` | | Rename. |
| POST | `/api/v1/local/delete` | | Delete (recursive). |
| GET | `/api/v1/local/download` | | Download a local file (range-capable). |
| POST | `/api/v1/local/upload` | | Store an uploaded file locally. |
| POST | `/api/v1/local/cache/clear` | | Empty the streaming cache. |

### Playlists

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/playlists` | | List playlists. |
| GET | `/api/v1/playlists/{id}` | | One playlist. |
| POST | `/api/v1/playlists` | | Create. |
| PUT | `/api/v1/playlists/{id}` | | Update. |
| DELETE | `/api/v1/playlists/{id}` | | Delete. |
| POST | `/api/v1/playlists/{id}/tracks` | | Append a track. |
| DELETE | `/api/v1/playlists/{id}/tracks/{fileId}` | | Remove a track. |
| PUT | `/api/v1/playlists/{id}/tracks/order` | | Reorder. |
| POST | `/api/v1/playlists/{id}/download` | ✓ | Download the playlist to disk. |

### Shares

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/shares` | ✓ | List shared collections. |
| GET | `/api/v1/shares/{id}` | ✓ | One shared collection. |
| GET | `/api/v1/shares/export` | ✓ | Build a share payload. |
| POST | `/api/v1/shares/import` | ✓ | Import a share. |
| DELETE | `/api/v1/shares/{id}` | ✓ | Delete a shared collection. |
| POST | `/api/v1/shares/strm` | ✓ | Export `.strm` files. |

### Configuration

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/config` | | Read configuration. |
| PATCH | `/api/v1/config` | | Partial update. |
| GET | `/api/v1/config/webdav` | | WebDAV bridge state. |
| POST | `/api/v1/config/webdav/start` | | Start the WebDAV bridge. |
| POST | `/api/v1/config/webdav/stop` | | Stop the WebDAV bridge. |

### System

| Method | Path | Sess. | Description |
| --- | --- | :---: | --- |
| GET | `/api/v1/system/ping` | | Liveness + key check. |
| GET | `/api/v1/system/info` | | Identity, versions, readiness. |
| GET | `/api/v1/system/setup` | | First-run wizard progress. |
| GET | `/api/v1/system/metrics` | | CPU/memory/disk usage. |
| GET | `/api/v1/system/logs` | | Query logs. |
| GET | `/api/v1/system/logs/loggers` | | Distinct logger names. |
| GET | `/api/v1/system/logs/versions` | | App versions in the log store. |
| DELETE | `/api/v1/system/logs` | | Delete old logs. |
| GET | `/api/v1/system/databases` | | List channel indexes + size. |
| GET | `/api/v1/system/databases/{channelId}/analysis` | | Folder-path integrity check. |
| POST | `/api/v1/system/databases/{channelId}/repair` | | Repair folder paths. |
| POST | `/api/v1/system/maintenance/cleanup-tasks` | | Drop stale persisted tasks. |

### SignalR hub `/hubs/transfers`

| Direction | Name | Payload |
| --- | --- | --- |
| Server → client | `TransfersSnapshot` | `TransfersSnapshotDto` |
| Server → client | `TransferSummary` | `TransferSummaryDto` |
| Server → client | `SpeedHistoryPoint` | `SpeedPointDto`, `SpeedPointDto` |
| Client → server | `GetSnapshot()` | → `TransfersSnapshotDto` |
| Client → server | `GetSummary()` | → `TransferSummaryDto` |
| Client → server | `GetSpeedHistory()` | → `SpeedHistoryDto` |
| Client → server | `MuteSnapshots()` / `UnmuteSnapshots()` | – |
| Client → server | `MuteSpeedHistory()` / `UnmuteSpeedHistory()` | – |

---

## Data models

Field casing in JSON is camelCase. Types below use TypeScript notation.

### Envelope

```ts
interface ApiResult<T> {
  success: boolean;
  data: T | null;
  error: { code: string; message: string; detail?: string } | null;
  message?: string;
  page?: PageInfo;
}
interface PageInfo {
  page: number; pageSize: number; totalItems: number;
  totalPages: number; hasNext: boolean; hasPrevious: boolean;
}
```

### Auth

```ts
interface AuthStatusDto { step: "phone"|"vc"|"pass"|"ok"|"setup_required"; isAuthenticated: boolean; isConfigured: boolean; user: TelegramUserDto|null; }
interface TelegramUserDto { id: number; username?: string; firstName?: string; lastName?: string; phone?: string; isPremium: boolean; }
interface QrLoginDto { sessionId: string; loginUrl?: string; qrImageBase64?: string; status: "waiting"|"password_required"|"authenticated"|"cancelled"|"error"; error?: string; }
```

### Channels

```ts
interface ApiChannelDto { id: number; name: string; type: "channel"|"group"|"chat"; isOwner: boolean; isFavorite: boolean; imageUrl: string; hasDatabase: boolean; }
interface ApiChannelDetailDto extends ApiChannelDto {
  fileCount: number; folderCount: number; totalSize: number; totalSizeText: string;
  audioCount: number; videoCount: number; photoCount: number; documentCount: number;
  isRefreshing: boolean; canRefresh: boolean;
}
interface ApiChannelsWithFoldersDto { folders: ApiChannelFolderDto[]; ungrouped: ApiChannelDto[]; totalChannels: number; }
interface ApiChannelFolderDto { id: number; title: string; iconEmoji?: string; channels: ApiChannelDto[]; channelCount: number; }
interface ApiChatMessageDto { id: number; date: string; text?: string; hasMedia: boolean; mediaType?: "photo"|"video"|"audio"|"document"; fileName?: string; fileSize: number; mimeType?: string; from?: string; }
```

### Files

```ts
interface ApiFileDto {
  id: string; name: string; path: string; parentId?: string;
  isFile: boolean; hasChildren: boolean;
  size: number; sizeText: string; type: string; category: string;
  dateCreated: string; dateModified: string;
  messageId?: number; isSplit: boolean; md5Hash?: string; xxHash?: string;
  streamUrl?: string; downloadUrl?: string;
}
interface ApiFolderContentsDto {
  channelId?: string; currentPath: string; currentFolderId?: string;
  parentPath?: string; parentFolderId?: string; folderName: string;
  items: ApiFileDto[]; stats: ApiFolderStatsDto; breadcrumbs: ApiBreadcrumbDto[];
}
interface ApiFolderStatsDto { folderCount: number; fileCount: number; audioCount: number; videoCount: number; photoCount: number; documentCount: number; totalSize: number; totalSizeText: string; }
interface ApiBreadcrumbDto { name: string; path: string; folderId?: string; }
```

### Transfers

```ts
interface TransferDto {
  id: string; kind: "download"|"upload"|"task"; action: string;
  state: "Error"|"Pending"|"Canceled"|"Paused"|"Completed"|"Working";
  isQueued: boolean; name: string; path?: string;
  channelId?: string; channelName?: string;
  size: number; transmitted: number; sizeText: string; transmittedText: string; progress: number;
  createdAt: string; startedAt?: string; endedAt?: string;
  totalItems?: number; executedItems?: number; isUpload?: boolean; fromPath?: string; toPath?: string;
}
interface TransfersSnapshotDto { downloads: TransferDto[]; queuedDownloads: TransferDto[]; uploads: TransferDto[]; queuedUploads: TransferDto[]; tasks: TransferDto[]; summary: TransferSummaryDto; }
interface TransferSummaryDto {
  activeDownloads: number; queuedDownloads: number; activeUploads: number; queuedUploads: number;
  activeTasks: number; totalTasks: number;
  downloadSpeed: string; uploadSpeed: string; downloadBytesPerSecond: number; uploadBytesPerSecond: number;
  downloadsPaused: boolean; isWorking: boolean;
}
interface SpeedPointDto { time: string; bytesPerSecond: number; speedText: string; activeFiles: string[]; }
interface SpeedHistoryDto { download: SpeedPointDto[]; upload: SpeedPointDto[]; intervalSeconds: number; windowSeconds: number; }
interface TransferAcceptedDto { accepted: number; skipped: string[]; taskId?: string; }
interface PersistedTaskDto { id: string; internalId: string; type: string; state: string; name?: string; channelId?: string; channelName?: string; totalSize: number; transmittedBytes: number; progress: number; sourcePath?: string; destinationPath?: string; creationDate: string; lastUpdated: string; retryCount: number; lastError?: string; }
```

### Playlists

```ts
interface PlaylistModel { id: string; name: string; description?: string; tracks: PlaylistTrackModel[]; dateCreated: string; dateModified: string; trackCount: number; }
interface PlaylistTrackModel { fileId: string; channelId: string; channelName: string; fileName: string; filePath: string; fileType: string; fileSize: number; order: number; directUrl?: string; isLocalFile: boolean; dateAdded: string; }
```

### System & config

```ts
interface ServerInfoDto { product: string; version: string; apiVersion: string; serverTimeUtc: string; mongoConnected: boolean; telegramConfigured: boolean; telegramAuthenticated: boolean; setupComplete: boolean; webDavRunning: boolean; transfersHubPath: string; requiresApiKey: boolean; }
interface SystemMetricsDto { systemCpuUsage: number; appCpuUsage: number; processorCount: number; totalMemoryBytes: number; usedMemoryBytes: number; availableMemoryBytes: number; memoryUsagePercent: number; appMemoryBytes: number; tempFolderPath?: string; tempFolderSizeBytes: number; diskTotalBytes: number; diskUsedBytes: number; diskFreeBytes: number; diskUsagePercent: number; }
interface AppConfigDto { /* see system-and-config.md; PATCH with UpdateConfigRequest (all fields optional) */ }
interface LogEntryDto { id: string; timestamp: string; level?: string; message?: string; logger?: string; exception?: string; version?: string; }
interface SharedCollectionDto { id: string; name?: string; description?: string; channelId?: string; collectionId?: string; dateCreated: string; dateModified: string; }
```

## Request bodies (quick list)

| Endpoint | Body |
| --- | --- |
| `POST /auth/login` | `{ value: string; isPhone?: boolean }` |
| `POST /auth/qr/{sessionId}/password` | `{ password: string }` |
| `POST /channels` | `{ title: string; about?: string; createDatabase?: boolean }` |
| `POST /channels/{id}/leave` | `{ deleteLocalDatabase?: boolean; deleteOnTelegram?: boolean }` |
| `POST /channels/{id}/refresh` | `{ includeDocuments?, includeAudio?, includeVideo?, includePhotos?, force? }` |
| `POST …/files/folders` | `{ path: string; name: string }` |
| `PUT …/files/{fileId}/name` | `{ newName: string }` |
| `POST …/files/delete` | `{ ids: string[] }` |
| `POST …/files/copy` · `/move` | `{ ids: string[]; targetPath?: string; targetFolderId?: string }` |
| `POST /transfers/downloads` | `{ channelId: string; fileIds: string[]; targetPath?: string; sharedCollectionId?: string }` |
| `POST /transfers/uploads` | `{ channelId: string; localPaths: string[]; targetPath?: string }` |
| `POST /transfers/messages` | `{ chatId: number; messageIds: number[]; targetPath?: string }` |
| `POST /local/folders` | `{ path: string; name: string }` |
| `POST /local/rename` | `{ path: string; newName: string }` |
| `POST /local/delete` | `{ paths: string[] }` |
| `POST /playlists` · `PUT /playlists/{id}` | `PlaylistModel` |
| `POST /playlists/{id}/tracks` | `PlaylistTrackModel` |
| `PUT /playlists/{id}/tracks/order` | `string[]` (file ids in order) |
| `POST /shares/import` | `{ share: ShareFilesModel }` |
| `POST /shares/strm` | `{ path: string; host?: string; destinationFolder?: string }` |
| `PATCH /config` | `UpdateConfigRequest` (all fields optional) |
