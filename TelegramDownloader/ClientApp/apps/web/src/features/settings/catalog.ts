import {
  ArrowDownUp,
  Bell,
  Cable,
  Clapperboard,
  FolderTree,
  HardDriveUpload,
  Info,
  LayoutList,
  ListRestart,
  Network,
  Palette,
  Radio,
  ShieldCheck,
  Smartphone,
  type LucideIcon
} from 'lucide-react';
import { t } from '@/i18n';

/**
 * Sections and settings of the settings page. The settings list feeds both
 * the rows (label + help) and the search box, so a setting is described once.
 * Built by functions so the strings are translated at render time.
 */

export type SectionId =
  | 'general'
  | 'connections'
  | 'streaming'
  | 'uploads'
  | 'file-manager'
  | 'notifications'
  | 'channels'
  | 'tasks'
  | 'security'
  | 'apps'
  | 'webdav'
  | 'library'
  | 'appearance'
  | 'about';

export interface SectionDef {
  id: SectionId;
  title: string;
  description: string;
  icon: LucideIcon;
  group: 'server' | 'access' | 'personal';
}

export function sections(): SectionDef[] {
  return [
    { id: 'general', title: t('General & transfers'), description: t('Default interface, simultaneous downloads, parallel chunks and logging.'), icon: ArrowDownUp, group: 'server' },
    { id: 'connections', title: t('Multi-connection & preload'), description: t('Faster downloads of large files and preloading of small ones.'), icon: Cable, group: 'server' },
    { id: 'streaming', title: t('Streaming'), description: t('STRM playback mode and video transcoding.'), icon: Radio, group: 'server' },
    { id: 'uploads', title: t('Uploads'), description: t('Splitting of large files, captions and hash checks.'), icon: HardDriveUpload, group: 'server' },
    { id: 'file-manager', title: t('File manager'), description: t('Layout and loading of the file lists.'), icon: LayoutList, group: 'server' },
    { id: 'notifications', title: t('Notifications'), description: t('Messages about finished operations.'), icon: Bell, group: 'server' },
    { id: 'channels', title: t('Channels'), description: t('Refresh of own channels, images and hidden channels.'), icon: FolderTree, group: 'server' },
    { id: 'tasks', title: t('Tasks & auto-resume'), description: t('Keep transfers across restarts.'), icon: ListRestart, group: 'server' },
    { id: 'library', title: t('Media library'), description: t('Movies and series: providers, channels, folder rules and scans.'), icon: Clapperboard, group: 'server' },
    { id: 'security', title: t('Web security'), description: t('Password of the web interface, passkeys and the local network rule.'), icon: ShieldCheck, group: 'access' },
    { id: 'apps', title: t('Apps & API'), description: t('API key for TFM Player, Android and Android TV.'), icon: Smartphone, group: 'access' },
    { id: 'webdav', title: t('WebDAV'), description: t('Credentials of the native WebDAV share.'), icon: Network, group: 'access' },
    { id: 'appearance', title: t('Appearance'), description: t('Theme, accent colour, effects, density and language.'), icon: Palette, group: 'personal' },
    { id: 'about', title: t('About'), description: t('Version, updates and links.'), icon: Info, group: 'personal' }
  ];
}

export function sectionGroups(): { id: SectionDef['group']; label: string }[] {
  return [
    { id: 'server', label: t('Server') },
    { id: 'access', label: t('Access') },
    { id: 'personal', label: t('This browser') }
  ];
}

export function isSectionId(value: string | undefined): value is SectionId {
  return !!value && sections().some((s) => s.id === value);
}

export interface SettingDef {
  /** Anchor id (`#setting-<id>`) and React key. */
  id: string;
  section: SectionId;
  label: string;
  description?: string;
  /** Defaults / recommended values line. */
  hint?: string;
  keywords?: string;
}

export function settingDefs(): SettingDef[] {
  return [
    // general
    {
      id: 'defaultWebUi',
      section: 'general',
      label: t('Open the new interface by default'),
      description: t('When on, the server address (/) redirects to this interface (/app/). Open /?ui=classic to keep the classic interface in that browser.'),
      keywords: 'blazor classic ui redirect'
    },
    { id: 'maxSimultaneousDownloads', section: 'general', label: t('Max. simultaneous downloads'), description: t('Number of files that can be downloaded at the same time (1-3).') },
    {
      id: 'parallelTransfers',
      section: 'general',
      label: t('Parallel chunk transfers'),
      description: t('Chunks requested in parallel per transfer (1-16), used by standard (single-connection) downloads and uploads. Higher values remove the latency bottleneck; Premium accounts benefit the most. Applied on the next transfer.'),
      hint: t('WTelegramClient default: 2 · App default: 4 · Recommended: 4-8')
    },
    { id: 'timeSleepBetweenTransactions', section: 'general', label: t('Transaction delay'), description: t('Delay between API transactions to avoid rate limiting (milliseconds).'), keywords: 'flood rate limit' },
    { id: 'shouldShowLogInTerminal', section: 'general', label: t('Show logs in the terminal'), description: t('Output detailed logs to the terminal/console for debugging.'), keywords: 'console debug' },

    // connections
    {
      id: 'enableMultiConnectionDownloads',
      section: 'connections',
      label: t('Multi-connection downloads'),
      description: t('Download large files using several parallel connections, like Telegram Desktop does. Telegram limits the speed per connection (~5-6 MB/s), so this is the way to go faster. The extra connections share your session authorization, so nothing new appears in your Telegram device list.'),
      hint: t('Default: disabled · Recommended: enabled for large files on fast lines')
    },
    { id: 'downloadConnections', section: 'connections', label: t('Download connections'), description: t('Number of parallel connections used per file download (2-8).'), hint: t('App default: 4 · Recommended: 4-8 (Telegram Desktop uses up to 8)') },
    { id: 'multiConnectionPartSizeKB', section: 'connections', label: t('Chunk size'), description: t('Size of each file chunk requested from Telegram. Bigger chunks mean fewer round-trips.'), hint: t('WTelegramClient default: 512 KB · App default and recommended: 1024 KB') },
    {
      id: 'multiConnectionBlockSizeMB',
      section: 'connections',
      label: t('Block size'),
      description: t('Work unit assigned to each connection (1-16 MB). All the chunks of a block are requested in parallel on the same connection, so block ÷ chunk = requests in flight per connection.'),
      hint: t('App default: 4 MB (4 chunks of 1024 KB in flight) · Recommended: 4-8 MB')
    },
    { id: 'multiConnectionMinFileSizeMB', section: 'connections', label: t('Min. file size'), description: t('Smaller files use the normal single-connection download (the setup is not worth it for small files).'), hint: t('App default and recommended: 32 MB') },
    { id: 'maxPreloadFileSizeInMb', section: 'connections', label: t('Max. preload file size'), description: t('Files smaller than this are downloaded entirely before playing. Improves playback of small files.') },

    // streaming
    { id: 'strmStreamingMode', section: 'streaming', label: t('Streaming mode (STRM / Emby / Kodi)'), description: t('How exported .strm files play media from Telegram. Applies to newly exported STRM files and to playback through the streaming endpoints. Files smaller than the max. preload size are always fully preloaded.'), keywords: 'emby kodi jellyfin strm cache' },
    { id: 'enableVideoTranscoding', section: 'streaming', label: t('Video transcoding'), description: t('Real-time transcoding to play video formats the browser cannot play (mkv, avi, wmv...) in the web player. Needs FFmpeg.'), keywords: 'ffmpeg mkv avi' },

    // uploads
    { id: 'maxImageUploadSizeInMb', section: 'uploads', label: t('Max. image upload size'), description: t('Larger images are sent as files instead of compressed photos. 0 always sends them as files.') },
    { id: 'splitSize', section: 'uploads', label: t('Split large files'), description: t('Files larger than this are uploaded in several parts. 0 disables splitting.'), keywords: 'premium 4gb 2gb' },
    { id: 'enableMemorySplitUpload', section: 'uploads', label: t('Memory-based split'), description: t('Split large files in memory instead of writing temporary parts to disk. Better for systems with little disk space.') },
    { id: 'memorySplitSizeGB', section: 'uploads', label: t('Memory chunk size'), description: t('Size of each part when splitting in memory. Must not exceed the split size.') },
    { id: 'shouldShowCaptionPath', section: 'uploads', label: t('Show the path in the caption'), description: t('Include the relative file path in the Telegram message captions.') },
    { id: 'checkHash', section: 'uploads', label: t('Verify file hash'), description: t('Check the file integrity after uploads and downloads with a hash.'), keywords: 'md5 xxhash integrity' },

    // file manager
    { id: 'useMobileFileManagerAlways', section: 'file-manager', label: t('Always use the touch file manager'), description: t('Use the mobile-optimised file manager also on desktop.') },
    { id: 'hasFileManagerVirtualScroll', section: 'file-manager', label: t('Virtual scroll'), description: t('Better performance with very large file lists.') },
    { id: 'shouldShowPaginatedFileChannel', section: 'file-manager', label: t('Paginated file view'), description: t('Show channel files in pages instead of loading them all at once.') },

    // notifications
    { id: 'shouldNotify', section: 'notifications', label: t('Show notifications'), description: t('Show a notification when downloads, uploads and other operations finish.'), keywords: 'toast alerts' },

    // channels
    { id: 'enableRefreshOwnChannels', section: 'channels', label: t('Refresh own channels'), description: t('Allow refreshing the data of channels where you are admin or owner. By default only channels you do not own can be refreshed (to sync changes made by others). A refresh downloads the metadata of every file again, which can take long on large channels.') },
    { id: 'showChannelImages', section: 'channels', label: t('Show channel images'), description: t('Show the channel avatars in the channel lists.'), keywords: 'avatar' },
    { id: 'imageCache', section: 'channels', label: t('Image cache'), description: t('Channel avatars and thumbnails cached on the server. Clear it to free disk space or force a new download.') },
    { id: 'showHiddenChannels', section: 'channels', label: t('Show hidden channels'), description: t('Show the channels you hid in the channel lists.') },
    { id: 'hiddenChannels', section: 'channels', label: t('Hidden channels'), description: t('Channels hidden from the lists. Unhide them here.'), keywords: 'unhide' },

    // tasks
    { id: 'enableTaskPersistence', section: 'tasks', label: t('Task persistence'), description: t('Save download and upload tasks in the database so they survive a restart.') },
    { id: 'autoResumeOnStartup', section: 'tasks', label: t('Auto-resume on startup'), description: t('Queue pending and interrupted transfers again once the Telegram session is ready after a restart. Downloads continue from the partial file and batch uploads skip what is already uploaded. Failed tasks wait for a manual retry.') },
    { id: 'taskPersistenceDebounceSeconds', section: 'tasks', label: t('Progress save interval'), description: t('How often the task progress is saved (seconds). Lower values resume more precisely, higher values load the database less.') },
    { id: 'staleTaskCleanupDays', section: 'tasks', label: t('Stale task cleanup'), description: t('Failed or stuck tasks older than this many days are deleted automatically.') },
    { id: 'pendingDownloads', section: 'tasks', label: t('Pending downloads'), description: t('Empty the queue of downloads that have not started yet.') },
    { id: 'persistedTasks', section: 'tasks', label: t('Persisted tasks'), description: t('Delete every task saved for resuming after a restart.') },

    // security
    { id: 'webPassword', section: 'security', label: t('Web password'), keywords: 'login password' },
    { id: 'passkeys', section: 'security', label: t('Passkeys'), keywords: 'passkey webauthn fido bitwarden biometric windows hello touch id security key' },
    { id: 'lanBypass', section: 'security', label: t('No password on the local network'), keywords: 'lan bypass local' },
    { id: 'signOut', section: 'security', label: t('Sessions'), keywords: 'sign out logout' },

    // apps
    { id: 'apiKey', section: 'apps', label: t('API key for apps'), keywords: 'mobile api key token qr' },
    { id: 'sisterApps', section: 'apps', label: t('TFM apps'), keywords: 'android tv player pwa' },

    // webdav
    { id: 'webDavUser', section: 'webdav', label: t('WebDAV user'), description: t('HTTP Basic user of the native WebDAV endpoint (/webdav). Leave it empty to leave WebDAV open.') },
    { id: 'webDavPassword', section: 'webdav', label: t('WebDAV password'), description: t('HTTP Basic password of the WebDAV endpoint. Serve WebDAV over HTTPS: Basic authentication is encoded, not encrypted.') },
    { id: 'webDavUrl', section: 'webdav', label: t('WebDAV address'), keywords: 'hyper backup rclone synology' },

    // library
    { id: 'libraryEnabled', section: 'library', label: t('Enable the media library'), description: t('Identify movies and series in the indexed channels (Plex/Emby style) with the metadata providers below, and keep the playback progress for the TV and mobile apps. Needs at least one provider with an API key.') },
    { id: 'libraryProviders', section: 'library', label: t('Metadata providers'), description: t('Lower priority numbers are asked first.'), keywords: 'tmdb omdb api key' },
    { id: 'libraryLanguage', section: 'library', label: t('Metadata language'), description: t('Language of titles and overviews at the providers that support it (TMDB), e.g. es-ES, en-US.') },
    { id: 'libraryAutoScan', section: 'library', label: t('Scan after a channel refresh'), description: t('When a channel index is refreshed, identify its new video files right away.') },
    { id: 'libraryWatchedThreshold', section: 'library', label: t('Watched threshold'), description: t('Fraction of the duration after which a file counts as watched (0.5 – 1). The default 0.92 leaves room for the end credits.') },
    { id: 'libraryChannels', section: 'library', label: t('Channels to scan'), description: t('Which indexed channels the library looks at: every channel with an index, or only the ones you pick.'), keywords: 'include exclude' },
    { id: 'libraryFolderRules', section: 'library', label: t('Folder rules'), description: t('Tell the scan what a folder holds when a channel mixes content: series, movies or ignore (extras, trailers). A rule covers every subfolder; the most specific path wins and / is the whole channel. Folders simply named Series, Películas, Cine, Movies… are recognised without a rule. Manual fixes are never undone by a rule.') },
    { id: 'libraryScan', section: 'library', label: t('Library scan'), keywords: 'rescan identify' },

    // appearance
    { id: 'theme', section: 'appearance', label: t('Theme'), keywords: 'dark light background amoled' },
    { id: 'accent', section: 'appearance', label: t('Accent colour'), keywords: 'color' },
    { id: 'effects', section: 'appearance', label: t('Effects'), keywords: 'glass blur ambient motion animations' },
    { id: 'density', section: 'appearance', label: t('Density'), keywords: 'compact' },
    { id: 'language', section: 'appearance', label: t('Language'), keywords: 'idioma español english' },

    // about
    { id: 'version', section: 'about', label: t('Version and updates'), keywords: 'update release github' },
    { id: 'links', section: 'about', label: t('Links'), keywords: 'github docs swagger' }
  ];
}

const normalize = (s: string) => s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();

/** Settings matching every word of the query (label, description, hint, keywords, section). */
export function searchSettings(query: string, defs = settingDefs(), secs = sections()): SettingDef[] {
  const words = normalize(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return [];
  return defs.filter((d) => {
    const section = secs.find((s) => s.id === d.section)?.title ?? '';
    const haystack = normalize([d.label, d.description, d.hint, d.keywords, section, d.id].filter(Boolean).join(' '));
    return words.every((w) => haystack.includes(w));
  });
}

export function settingDef(id: string): SettingDef | undefined {
  return settingDefs().find((d) => d.id === id);
}

export const anchorOf = (id: string) => `setting-${id}`;
