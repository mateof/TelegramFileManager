// Channel index and local disk entries (mirrors Models/Api/FileDtos.cs)
import type { PageInfo, PagedQuery } from '../http';

/** Category of an entry as computed by `ApiFileDto.CategoryOf` (plus `Folder`). */
export type ApiFileCategory = 'Audio' | 'Video' | 'Photo' | 'Document' | 'Archive' | 'Application' | 'Other' | 'Folder';

/** `filter` values of {@link BrowseQuery}. */
export type BrowseFilter = 'audio' | 'video' | 'photo' | 'document' | 'archive' | 'all';

/** `sortBy` values of the browse/search endpoints. */
export type BrowseSortBy = 'name' | 'date' | 'size' | 'type';

/** A file or folder as stored in the channel index (MongoDB) or on the local disk. */
export interface ApiFileDto {
  /** MongoDB id for remote entries, relative path for local entries. */
  id: string;
  name: string;
  /** Folder path this entry lives in, always ending with `/`. */
  path: string;
  /** Id of the parent folder (remote entries only). */
  parentId: string | null;
  isFile: boolean;
  hasChildren: boolean;
  size: number;
  sizeText: string;
  /** File extension including the dot, or `folder`. */
  type: string;
  /** Audio, Video, Photo, Document, Archive, Folder... */
  category: ApiFileCategory | (string & {});
  /** ISO date. */
  dateCreated: string;
  /** ISO date. */
  dateModified: string;
  /** Telegram message id backing this file, when not split. */
  messageId: number | null;
  /** The message of the file, or of its first part when it is split. Absent on older servers. */
  firstMessageId?: number | null;
  /** True when the file was uploaded as several Telegram messages. */
  isSplit: boolean;
  md5Hash: string | null;
  xxHash: string | null;
  /** Absolute URL for range-capable streaming, when applicable. */
  streamUrl: string | null;
  /** Absolute URL that downloads the whole file. */
  downloadUrl: string | null;
  /** v1 URL of the bytes, inline (`/api/v1/channels/{c}/files/{id}/content`, or `/api/v1/local/download?...&disposition=inline`). */
  contentUrl?: string | null;
  /** Same as {@link contentUrl} with `disposition=attachment` (browser download). */
  contentDownloadUrl?: string | null;
  /** Audio/video: what to give a media element (the transcoding `stream` URL for formats browsers cannot play). */
  playbackUrl?: string | null;
  /** Photos and single-message videos: a small JPEG preview. */
  thumbnailUrl?: string | null;
}

/** Listing of a folder plus navigation and aggregate information. */
export interface ApiFolderContentsDto {
  /** Channel id for remote listings, null for local listings. */
  channelId: string | null;
  currentPath: string;
  currentFolderId: string | null;
  parentPath: string | null;
  parentFolderId: string | null;
  folderName: string;
  items: ApiFileDto[];
  stats: ApiFolderStatsDto;
  /** Breadcrumb from the root down to the current folder. */
  breadcrumbs: ApiBreadcrumbDto[];
}

/**
 * Client side: a browse answer. The server sends the {@link ApiFolderContentsDto} as `data`
 * (its `items` are the current page) with the `page` block of the envelope next to it.
 */
export interface FolderContentsPage {
  contents: ApiFolderContentsDto;
  page: PageInfo;
}

/** One breadcrumb hop. */
export interface ApiBreadcrumbDto {
  name: string;
  path: string;
  folderId: string | null;
}

/** Aggregate counters for a folder listing. */
export interface ApiFolderStatsDto {
  folderCount: number;
  fileCount: number;
  audioCount: number;
  videoCount: number;
  photoCount: number;
  documentCount: number;
  totalSize: number;
  totalSizeText: string;
}

/** Query string for browse/search endpoints. `sortBy`: `name` (default), `date`, `size`, `type`. */
export interface BrowseQuery extends PagedQuery {
  /** Folder id to list (remote listings). Empty means the channel root. */
  folderId?: string;
  /** Folder path to list. Used when `folderId` is not supplied. */
  path?: string;
  /** Restrict to a category: `audio`, `video`, `photo`, `document`, `archive`, `all`. */
  filter?: BrowseFilter | (string & {});
  /** Case-insensitive substring match on the file name. */
  search?: string;
  /** Hide folders and return only files. */
  filesOnly?: boolean;
  /** Read an imported shared collection instead of the channel's own index (channel listings only). */
  collectionId?: string;
}

/** Query of `GET /api/v1/channels/{channelId}/files/search`: the text plus the browse options (`path` scopes the subtree). */
export interface FileSearchQuery extends BrowseQuery {
  /** Text to look for (case-insensitive, substring match). Required. */
  q: string;
}

/** Query of `GET /api/v1/channels/{channelId}/files/stats`. */
export interface FolderStatsQuery {
  path?: string;
  folderId?: string;
  collectionId?: string;
}

/** Body of `POST /api/v1/channels/{channelId}/files/folders`. */
export interface CreateFolderRequest {
  /** Parent folder path, e.g. `/music/`. Defaults to the root. */
  path?: string;
  /** Name of the new folder. */
  name: string;
}

/** Body of `PUT /api/v1/channels/{channelId}/files/{fileId}/name`. */
export interface RenameRequest {
  newName: string;
}

/** Body of the delete/copy/move endpoints. */
export interface FileIdsRequest {
  /** Ids of the entries to operate on. */
  ids: string[];
}

/** Body of `POST /api/v1/channels/{channelId}/files/copy` and `/move`. */
export interface CopyMoveRequest extends FileIdsRequest {
  /** Destination folder path, e.g. `/backup/`. */
  targetPath?: string;
  /** Destination folder id. Takes precedence over `targetPath`. */
  targetFolderId?: string | null;
}

/** Body of the local file-system mutation endpoints. */
export interface LocalPathRequest {
  /** Path relative to the local root, e.g. `music/rock`. */
  path: string;
}

/** Body of `POST /api/v1/local/folders`. */
export interface LocalCreateFolderRequest extends LocalPathRequest {
  name: string;
}

/** Body of `POST /api/v1/local/rename`. */
export interface LocalRenameRequest extends LocalPathRequest {
  newName: string;
}

/** Body of `POST /api/v1/local/delete`. */
export interface LocalDeleteRequest {
  /** Paths relative to the local root. */
  paths: string[];
}

/** Body of `POST /api/v1/local/copy` and `/move`. */
export interface LocalCopyMoveRequest {
  /** Files or folders to copy/move, relative to the local root. */
  paths: string[];
  /** Destination folder, relative to the local root. Created when missing. */
  targetPath: string;
  /** Replace entries with the same name in the destination; otherwise they are skipped. */
  overwrite?: boolean;
}

/** Body of `POST /api/v1/channels/{channelId}/files/preload`. */
export interface PreloadFilesRequest {
  /** File or folder ids; folders are expanded recursively. */
  fileIds: string[];
}

/** Query of the `content` media URL. */
export interface ContentUrlOptions {
  /** `inline` (default) or `attachment` (browser download). */
  disposition?: 'inline' | 'attachment';
  /** Entry of an imported shared collection. */
  collectionId?: string;
}

/** Options of the multipart upload endpoints (`file` part + optional `path` form field). */
export interface UploadFileOptions {
  /** Destination folder (channel folder or path relative to the local root). Defaults to the root. */
  path?: string;
  /** File name sent with the part. Defaults to `File.name`, or `file` for a plain Blob. */
  fileName?: string;
}

/**
 * A raw entry of a channel index (mirrors `Models.BsonFileManagerModel`). Only travels inside
 * share payloads (`ShareFilesModel.files`). Property names follow the C# ones
 * camel-cased by System.Text.Json (hence `mD5Hash`/`xxHash`).
 */
export interface BsonFileManagerModel {
  id: string | null;
  caseSensitive: boolean;
  /** ISO date. */
  dateCreated: string;
  /** ISO date. */
  dateModified: string;
  filterId: string | null;
  filterPath: string | null;
  hasChild: boolean;
  isFile: boolean;
  name: string | null;
  filePath: string | null;
  parentId: string | null;
  showHiddenItems: boolean;
  size: number;
  type: string | null;
  messageId: number | null;
  isSplit: boolean;
  listMessageId: number[] | null;
  isEncrypted: boolean;
  mD5Hash: string | null;
  xxHash: string | null;
}
