// Application configuration: /api/v1/config (mirrors the config classes of Models/Api/SystemDtos.cs
// and Models.LibraryFolderRule)
import type { ApiLibraryProviderDto, LibraryProviderConfigDto } from './library';

/** `StreamingMode` names accepted/returned in `strmStreamingMode` (the server sends `ToString()` of the enum). */
export const StrmStreamingMode = {
  DirectStream: 'DirectStream',
  ProgressiveCache: 'ProgressiveCache',
  Preload: 'Preload'
} as const;

export type StrmStreamingMode = (typeof StrmStreamingMode)[keyof typeof StrmStreamingMode];

/** What a channel folder holds for the media library: `movie`, `series` or `ignore`. */
export type LibraryFolderRuleKind = 'movie' | 'series' | 'ignore';

/** A folder of a channel index and the kind of content the library should expect there (mirrors `Models.LibraryFolderRule`). */
export interface LibraryFolderRule {
  channelId: number;
  /** Folder path as stored in the index (`/Series/`); `/` is the whole channel. */
  path: string;
  /** `movie`, `series` or `ignore`. */
  kind: LibraryFolderRuleKind | (string & {});
}

/** Application configuration exposed for reading and updating. */
export interface AppConfigDto {
  shouldNotify: boolean;
  timeSleepBetweenTransactions: number;
  splitSize: number;
  maxSimultaneousDownloads: number;
  checkHash: boolean;
  maxImageUploadSizeInMb: number;
  maxPreloadFileSizeInMb: number;
  shouldShowCaptionPath: boolean;
  shouldShowLogInTerminal: boolean;
  /** `DirectStream`, `ProgressiveCache` or `Preload`. */
  strmStreamingMode: StrmStreamingMode | (string & {});
  shouldShowPaginatedFileChannel: boolean;
  showChannelImages: boolean;
  showHiddenChannels: boolean;
  favouriteChannels: number[];
  hiddenChannels: number[];
  enableTaskPersistence: boolean;
  taskPersistenceDebounceSeconds: number;
  staleTaskCleanupDays: number;
  autoResumeOnStartup: boolean;
  enableVideoTranscoding: boolean;
  enableRefreshOwnChannels: boolean;
  enableMemorySplitUpload: boolean;
  memorySplitSizeGB: number;
  parallelTransfers: number;
  enableMultiConnectionDownloads: boolean;
  downloadConnections: number;
  multiConnectionPartSizeKB: number;
  multiConnectionBlockSizeMB: number;
  multiConnectionMinFileSizeMB: number;
  libraryEnabled: boolean;
  libraryLanguage: string;
  libraryAutoScan: boolean;
  /** Channels to scan; empty means every indexed channel. */
  libraryIncludedChannels: number[];
  libraryExcludedChannels: number[];
  libraryWatchedThreshold: number;
  /** Metadata providers with masked keys. Update them with `libraryProviders` in the PATCH body. */
  libraryProviders: ApiLibraryProviderDto[];
  /** What each channel folder holds: `movie`, `series` or `ignore`. Longest path wins. */
  libraryFolderRules: LibraryFolderRule[];
}

/**
 * Partial configuration update. Only the properties present in the request body are applied;
 * everything else keeps its current value.
 */
export interface UpdateConfigRequest {
  shouldNotify?: boolean | null;
  timeSleepBetweenTransactions?: number | null;
  splitSize?: number | null;
  maxSimultaneousDownloads?: number | null;
  checkHash?: boolean | null;
  maxImageUploadSizeInMb?: number | null;
  maxPreloadFileSizeInMb?: number | null;
  shouldShowCaptionPath?: boolean | null;
  shouldShowLogInTerminal?: boolean | null;
  strmStreamingMode?: StrmStreamingMode | (string & {}) | null;
  shouldShowPaginatedFileChannel?: boolean | null;
  showChannelImages?: boolean | null;
  showHiddenChannels?: boolean | null;
  enableTaskPersistence?: boolean | null;
  taskPersistenceDebounceSeconds?: number | null;
  staleTaskCleanupDays?: number | null;
  autoResumeOnStartup?: boolean | null;
  enableVideoTranscoding?: boolean | null;
  enableRefreshOwnChannels?: boolean | null;
  enableMemorySplitUpload?: boolean | null;
  memorySplitSizeGB?: number | null;
  parallelTransfers?: number | null;
  enableMultiConnectionDownloads?: boolean | null;
  downloadConnections?: number | null;
  multiConnectionPartSizeKB?: number | null;
  multiConnectionBlockSizeMB?: number | null;
  multiConnectionMinFileSizeMB?: number | null;
  libraryEnabled?: boolean | null;
  libraryLanguage?: string | null;
  libraryAutoScan?: boolean | null;
  libraryIncludedChannels?: number[] | null;
  libraryExcludedChannels?: number[] | null;
  libraryWatchedThreshold?: number | null;
  /** Per provider: `enabled`, `apiKey` (omit to keep, empty to clear) and `priority`. */
  libraryProviders?: LibraryProviderConfigDto[] | null;
  /** Replaces the whole list of folder rules (`channelId`, `path`, `kind`). */
  libraryFolderRules?: LibraryFolderRule[] | null;
}
