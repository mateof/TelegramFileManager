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
  /**
   * Series only: the title every file of the folder belongs to. On an update,
   * `providerId` null or missing keeps the one the rule has and `''` clears it.
   */
  provider?: string | null;
  providerId?: string | null;
  title?: string | null;
  year?: number | null;
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
  /** Always use the touch file manager in the web UI, also on desktop. */
  useMobileFileManagerAlways: boolean;
  /** Virtual scrolling in the web UI file manager. */
  hasFileManagerVirtualScroll: boolean;
  /** API key for apps, masked (`****` + the last 4 characters); null when none is set here. */
  mobileApiKey: string | null;
  /** An API key is configured (here or in config.json). */
  mobileApiKeySet: boolean;
  /** UI served at `/`: `classic` (Blazor) or `app` (redirects to `/app/`). */
  defaultWebUi: 'classic' | 'app' | (string & {});
  /** WebDAV user name. */
  webDavUser: string | null;
  /** A WebDAV password is configured (the password itself is never returned). */
  webDavPasswordSet: boolean;
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
  useMobileFileManagerAlways?: boolean | null;
  hasFileManagerVirtualScroll?: boolean | null;
  /** New API key for apps (8+ characters). Omit to keep, `""` to remove (config.json applies). */
  mobileApiKey?: string | null;
  /** `classic` or `app` (400 for other values). */
  defaultWebUi?: 'classic' | 'app' | null;
  /** WebDAV user. Omit to keep, `""` to clear. */
  webDavUser?: string | null;
  /** WebDAV password (write-only). Omit to keep, `""` to clear. */
  webDavPassword?: string | null;
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
