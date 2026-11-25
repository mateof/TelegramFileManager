// Downloads, uploads and batch tasks: /api/v1/transfers (mirrors Models/Api/TransferDtos.cs
// and Services/Upload/UploadPreviewPlanner.cs)
import type { PagedQuery } from '../http';

/** Kind of transfer reported by the API and the SignalR hub. */
export const TransferKind = {
  Download: 'download',
  Upload: 'upload',
  /** A batch job that spawns individual downloads/uploads. */
  Task: 'task'
} as const;

export type TransferKind = (typeof TransferKind)[keyof typeof TransferKind];

/** `StateTask` names as serialised in {@link TransferDto.state} (`ToString()` of the C# enum). */
export const TransferState = {
  Error: 'Error',
  Pending: 'Pending',
  Canceled: 'Canceled',
  Paused: 'Paused',
  Completed: 'Completed',
  Working: 'Working'
} as const;

export type TransferState = (typeof TransferState)[keyof typeof TransferState];

/**
 * A single running/queued/finished transfer. Shape is shared by the REST endpoints and by the
 * `transfers` SignalR hub, so a client can render the same view from a snapshot or from a live event.
 */
export interface TransferDto {
  /** Stable id of the transfer. Use it to pause/resume/cancel. */
  id: string;
  /** One of {@link TransferKind}. */
  kind: TransferKind;
  /** Operation label: `Download`, `Upload`, `Splitting`, `MD5 Calc`, `XxHash Calc`. */
  action: string;
  /** `Error`, `Pending`, `Canceled`, `Paused`, `Completed` or `Working`. */
  state: TransferState | (string & {});
  /** True when the transfer sits in the queue instead of running. */
  isQueued: boolean;
  /** Whether it can be paused while it runs. Absent on older servers. */
  canPause?: boolean;
  /** Whether it can be retried once cancelled or failed. Absent on older servers. */
  canRetry?: boolean;
  name: string;
  /** Destination path for downloads, source path for uploads. */
  path: string | null;
  channelId: string | null;
  channelName: string | null;
  size: number;
  transmitted: number;
  sizeText: string;
  transmittedText: string;
  /** Completion percentage, 0-100. */
  progress: number;
  /** ISO date. */
  createdAt: string;
  /** ISO date. */
  startedAt: string | null;
  /** ISO date. */
  endedAt: string | null;
  /** Number of files in the batch (batch tasks only). */
  totalItems: number | null;
  /** Number of files already processed (batch tasks only). */
  executedItems: number | null;
  /** True when a batch task uploads, false when it downloads. */
  isUpload: boolean | null;
  fromPath: string | null;
  toPath: string | null;
}

/** Aggregate view of everything in flight. Payload of the `TransfersSnapshot` hub message and of `GET /api/v1/transfers`. */
export interface TransfersSnapshotDto {
  downloads: TransferDto[];
  queuedDownloads: TransferDto[];
  uploads: TransferDto[];
  queuedUploads: TransferDto[];
  tasks: TransferDto[];
  summary: TransferSummaryDto;
}

/** Lightweight counters and current speeds. Pushed on its own as `TransferSummary` so clients can render a status bar cheaply. */
export interface TransferSummaryDto {
  activeDownloads: number;
  queuedDownloads: number;
  activeUploads: number;
  queuedUploads: number;
  activeTasks: number;
  totalTasks: number;
  /** Human readable download speed, e.g. `4.2 MB/s`. */
  downloadSpeed: string;
  /** Human readable upload speed. */
  uploadSpeed: string;
  /** Bytes transferred during the last full second. */
  downloadBytesPerSecond: number;
  uploadBytesPerSecond: number;
  /** True when the download queue has been paused globally. */
  downloadsPaused: boolean;
  /** Computed on the server: something is downloading, uploading or running a task. */
  isWorking: boolean;
}

/** One sample of the speed history chart. */
export interface SpeedPointDto {
  /** ISO date. */
  time: string;
  bytesPerSecond: number;
  speedText: string;
  activeFiles: string[];
}

/** Download and upload speed history, used to draw charts. */
export interface SpeedHistoryDto {
  download: SpeedPointDto[];
  upload: SpeedPointDto[];
  /** Seconds between samples. */
  intervalSeconds: number;
  /** How long samples are retained, in seconds. */
  windowSeconds: number;
}

/** Body of `POST /api/v1/transfers/downloads`. */
export interface StartDownloadRequest {
  /** Channel whose indexed files should be downloaded. */
  channelId: string;
  /** Ids of the files/folders to download. Folders are pulled recursively. */
  fileIds: string[];
  /** Destination folder relative to the server local root. Null keeps the original channel folder structure. */
  targetPath?: string | null;
  /** Set when downloading from a shared collection instead of an owned channel. */
  sharedCollectionId?: string | null;
}

/** Body of `POST /api/v1/transfers/uploads`. */
export interface StartUploadRequest {
  /** Destination channel. */
  channelId: string;
  /** Paths relative to the server local root. Folders are pushed recursively. */
  localPaths: string[];
  /** Destination folder inside the channel, e.g. `/backup/`. Defaults to the root. */
  targetPath?: string | null;
}

/** Body of `POST /api/v1/transfers/uploads/preview`: the upload body plus the preview options. */
export interface UploadPreviewRequest extends StartUploadRequest {
  /**
   * Also look for files whose content is already in the channel at another path. Hashes every local
   * file whose size matches a hashed channel file, so it can take a while on large trees.
   */
  findDuplicates?: boolean;
  /** Maximum number of per item decisions returned in `items` (default 500, max 5000). */
  maxItems?: number | null;
}

/** Body of `POST /api/v1/transfers/messages`. */
export interface DownloadMessagesRequest {
  /** Chat the messages belong to. */
  chatId: number;
  /** Telegram message ids carrying the media to download. */
  messageIds: number[];
  /** Destination folder relative to the server local root. */
  targetPath?: string | null;
}

/** Result returned when a transfer batch has been queued. */
export interface TransferAcceptedDto {
  /** Number of items accepted for transfer. */
  accepted: number;
  /** Ids that could not be resolved and were skipped. */
  skipped: string[];
  /** Id of the batch task, when the operation created one. */
  taskId: string | null;
}

/** A transfer restored from MongoDB after an application restart. */
export interface PersistedTaskDto {
  id: string;
  internalId: string;
  type: string;
  state: string;
  name: string | null;
  channelId: string | null;
  channelName: string | null;
  totalSize: number;
  transmittedBytes: number;
  progress: number;
  sourcePath: string | null;
  destinationPath: string | null;
  /** ISO date. */
  creationDate: string;
  /** ISO date. */
  lastUpdated: string;
  retryCount: number;
  lastError: string | null;
}

/** Result of `POST /api/v1/transfers/persisted/{internalId}/resume`. */
export interface TaskResumeResultDto {
  internalId: string;
  /** True when the transfer was queued again. */
  resumed: boolean;
  /** `Skip`, `Fail`, `ResumeDownload`, `ResumeSplitDownload` or `ResumeBatchUpload`. */
  action: string;
  message: string | null;
}

/** Action labels used in {@link UploadPreviewItem.action}. */
export const UploadPreviewActions = {
  /** File not in the destination: will be uploaded. */
  New: 'new',
  /** Older copy indexed at the same path: will be uploaded and the old entry deleted. */
  Replace: 'replace',
  /** Empty file: skipped. */
  SkipEmpty: 'skip-empty',
  /** The indexed copy is newer: skipped. */
  SkipNewer: 'skip-newer',
  /** Folder not indexed yet: will be created. */
  CreateFolder: 'create-folder',
  /** Folder already indexed: reused. */
  ExistingFolder: 'existing-folder'
} as const;

export type UploadPreviewActions = (typeof UploadPreviewActions)[keyof typeof UploadPreviewActions];

/** One local file or folder of an upload preview. */
export interface UploadPreviewItem {
  /** Path relative to the server local root (folders end with `/`). */
  localPath: string;
  /** Index path the item gets in the channel (`FilePath`). */
  targetPath: string;
  /** File or folder. */
  isFile: boolean;
  /** Bytes of the local file (0 for folders). */
  size: number;
  /** One of {@link UploadPreviewActions}. */
  action: UploadPreviewActions | (string & {});
  /** ISO date. Modification date of the indexed copy, for `replace` and `skip-newer`. */
  existingDateModified: string | null;
}

/** A channel file with the same content as a local file. */
export interface UploadPreviewDuplicateMatch {
  /** Index id of the channel file. */
  id: string;
  /** Path of the channel file. */
  filePath: string;
  /** Size of the channel file. */
  size: number;
}

/** A local file that will be uploaded although its content is already in the channel. */
export interface UploadPreviewDuplicate {
  /** Path relative to the server local root. */
  localPath: string;
  /** Where the upload would index it. */
  targetPath: string;
  /** Size of the local file. */
  size: number;
  /** Stored-format XXHash shared with the matches. */
  xxHash: string;
  /** Channel files with the same hash and size, at other paths. */
  matches: UploadPreviewDuplicateMatch[];
}

/** Result of an upload dry run: what `POST /api/v1/transfers/uploads` would do with the same body. */
export interface UploadPreviewResult {
  /** Destination folder in the channel. */
  targetPath: string;
  /** Whether the destination folder is indexed. When it is not, the upload attaches the new entries to the channel root. */
  targetFolderExists: boolean;
  /** Files not in the destination. */
  newFiles: number;
  /** Bytes of the new files. */
  newBytes: number;
  /** Files replacing an older indexed copy. */
  replacedFiles: number;
  /** Bytes of the replacing files. */
  replacedBytes: number;
  /** Computed: files that will be uploaded (`newFiles + replacedFiles`). */
  filesToUpload: number;
  /** Computed: bytes that will be sent to Telegram. */
  bytesToUpload: number;
  /** Computed: files skipped (empty, or a newer copy is indexed). */
  skippedFiles: number;
  /** Empty files skipped. */
  skippedEmpty: number;
  /** Files skipped because the indexed copy is newer. */
  skippedNewerInChannel: number;
  /** Bytes of the skipped files. */
  skippedBytes: number;
  /** Folders that will be created in the index. */
  foldersToCreate: number;
  /** Folders that already exist and are reused. */
  existingFolders: number;
  /** Whether duplicate detection by content ran. */
  duplicateCheck: boolean;
  /** Local files read to compute their hash. */
  hashedFiles: number;
  /** Bytes read to compute those hashes. */
  hashedBytes: number;
  /** Files to upload whose content is already in the channel at another path. */
  duplicates: UploadPreviewDuplicate[];
  /** Per item decisions, capped at the requested maximum. */
  items: UploadPreviewItem[];
  /** True when `items` was cut at the maximum. */
  itemsTruncated: boolean;
  /** Requested local paths that do not exist under the local root. */
  missingLocalPaths: string[];
}

/** Query of `GET /api/v1/transfers/downloads` and `/uploads`. */
export interface TransferListQuery extends PagedQuery {
  /** List the queue instead of the running/finished list. */
  queued?: boolean;
}

/** `scope` of `POST /api/v1/transfers/clear`. */
export type TransferClearScope = 'downloads' | 'uploads' | 'tasks' | 'all';

/** States accepted by the `states` filter of `POST /api/v1/transfers/clear` (`failed`/`cancelled` are aliases). */
export type TransferClearState = 'completed' | 'error' | 'failed' | 'canceled' | 'cancelled' | 'paused' | 'pending';

/** Query of `POST /api/v1/transfers/clear`. */
export interface TransferClearQuery {
  /** `downloads`, `uploads`, `tasks` or `all` (default). */
  scope?: TransferClearScope;
  /** Restrict the cleanup to these states (sent comma separated). Omit for every finished entry. */
  states?: readonly TransferClearState[];
}

/** `scope` of `POST /api/v1/transfers/queue/clear`. */
export type TransferQueueScope = 'downloads' | 'uploads' | 'all';
