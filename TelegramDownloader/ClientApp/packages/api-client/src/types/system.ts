// Server identity, health, logs and database maintenance: /api/v1/system (mirrors the system
// classes of Models/Api/SystemDtos.cs)
import type { PagedQuery } from '../http';

/** Server identity and health, returned by `GET /api/v1/system/info`. */
/** `GET /api/mobile/capabilities`: optional features of the server (404 on old servers: none). */
export interface CapabilitiesDto {
  apiVersion: number;
  serverVersion: string;
  /** Stable identifiers, e.g. `files.deleteOperations`, `events.hub`. */
  features: string[];
}

export interface ServerInfoDto {
  product: string;
  version: string;
  /** Highest API version this server implements. */
  apiVersion: string;
  /** ISO date. */
  serverTimeUtc: string;
  mongoConnected: boolean;
  telegramConfigured: boolean;
  telegramAuthenticated: boolean;
  setupComplete: boolean;
  /** Relative path of the SignalR hub streaming transfer updates. */
  transfersHubPath: string;
  /** True when the server requires an `X-Api-Key` header. */
  requiresApiKey: boolean;
}

/** Machine resource usage, returned by `GET /api/v1/system/metrics`. */
export interface SystemMetricsDto {
  systemCpuUsage: number;
  appCpuUsage: number;
  processorCount: number;
  totalMemoryBytes: number;
  usedMemoryBytes: number;
  availableMemoryBytes: number;
  memoryUsagePercent: number;
  appMemoryBytes: number;
  tempFolderPath: string | null;
  tempFolderSizeBytes: number;
  diskTotalBytes: number;
  diskUsedBytes: number;
  diskFreeBytes: number;
  diskUsagePercent: number;
}

/** `currentStep` of {@link SetupStatusDto}. */
export type SetupStep = 'Complete' | 'MongoDbRequired' | 'TelegramRequired';

/** Progress of the first-run wizard. */
export interface SetupStatusDto {
  /** `Complete`, `MongoDbRequired` or `TelegramRequired`. */
  currentStep: SetupStep | (string & {});
  mongoDbConfigured: boolean;
  mongoDbConnected: boolean;
  telegramConfigured: boolean;
  mongoDbError: string | null;
}

/** Statistics of one indexed channel database. */
export interface DatabaseStatsDto {
  channelId: string;
  channelName: string | null;
  sizeInBytes: number;
  sizeText: string;
  documentCount: number;
  /** ISO date. */
  createdAt: string | null;
  /** ISO date. */
  lastModified: string | null;
}

/** Result of a filter-path integrity analysis on a channel database. */
export interface PathAnalysisDto {
  databaseName: string;
  totalItems: number;
  itemsWithIssues: number;
  filterPathIssues: number;
  filterIdIssues: number;
  filePathIssues: number;
  hasIssues: boolean;
  error: string | null;
}

/** One application log record. */
export interface LogEntryDto {
  id: string;
  /** ISO date. */
  timestamp: string;
  level: string | null;
  message: string | null;
  logger: string | null;
  exception: string | null;
  version: string | null;
}

/** Log levels accepted by {@link LogQuery.level}. */
export type LogLevel = 'Verbose' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Fatal';

/** Query string for `GET /api/v1/system/logs`. */
export interface LogQuery extends PagedQuery {
  /** ISO date. */
  fromDate?: string;
  /** ISO date. */
  toDate?: string;
  /** `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. */
  level?: LogLevel | (string & {});
  logger?: string;
  version?: string;
  search?: string;
}

/** Query of `DELETE /api/v1/system/logs`. */
export interface DeleteLogsQuery {
  /** Keep the entries of the last N days (default 30). */
  daysToKeep?: number;
}

/** Image cache usage (`GET/DELETE /api/v1/system/image-cache`). */
export interface ImageCacheInfoDto {
  channelImages: number;
  thumbnails: number;
  /** Library posters, backdrops and stills (servers before the library image cache omit it). */
  libraryImages?: number;
  sizeInBytes: number;
  sizeText: string;
}

/** FFmpeg availability (`GET /api/v1/system/ffmpeg`). */
export interface FfmpegInfoDto {
  available: boolean;
  /** e.g. `6.1.1`; null when FFmpeg is missing. */
  version: string | null;
  /** Current value of `enableVideoTranscoding`. */
  transcodingEnabled: boolean;
}

/** A GitHub release. */
export interface ReleaseDto {
  version: string;
  name: string | null;
  url: string | null;
  /** ISO date. */
  publishedAt: string;
  prerelease: boolean;
  /** Release notes (Markdown). */
  notes: string | null;
}

/** Current version and the latest GitHub release / dev build (`GET /api/v1/system/version`). */
export interface VersionCheckDto {
  currentVersion: string;
  isDevVersion: boolean;
  latestRelease: ReleaseDto | null;
  latestDev: ReleaseDto | null;
  hasNewRelease: boolean;
  hasNewDev: boolean;
}

/** Outcome of a setup test (`POST /api/v1/system/setup/test-*`). */
export interface SetupTestResultDto {
  success: boolean;
  error: string | null;
}

/** Body of `POST /api/v1/system/setup/test-mongo`. */
export interface TestMongoRequest {
  connectionString: string;
}

/** Body of `POST /api/v1/system/setup/test-telegram`. */
export interface TestTelegramRequest {
  apiId: string;
  apiHash: string;
}

/** Body of `POST /api/v1/system/setup`: send either part or both. */
export interface SaveSetupRequest {
  mongoConnectionString?: string | null;
  apiId?: string | null;
  apiHash?: string | null;
}
