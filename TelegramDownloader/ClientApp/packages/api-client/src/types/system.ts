// Server identity, health, logs and database maintenance: /api/v1/system (mirrors the system
// classes of Models/Api/SystemDtos.cs)
import type { PagedQuery } from '../http';

/** Server identity and health, returned by `GET /api/v1/system/info`. */
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
