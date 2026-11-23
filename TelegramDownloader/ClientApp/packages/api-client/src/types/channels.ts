// Telegram chats/channels: /api/v1/channels (mirrors Models/Api/ChannelDtos.cs)
import type { PagedQuery } from '../http';

/** Channel id as accepted in routes (numeric Telegram id, or its string form). */
export type ChannelId = number | string;

/** `channel`, `group` or `chat`. */
export type ApiChannelType = 'channel' | 'group' | 'chat';

/** A Telegram chat/channel visible to the signed-in account. */
export interface ApiChannelDto {
  id: number;
  name: string;
  /** `channel`, `group` or `chat`. */
  type: ApiChannelType | (string & {});
  /** True when the signed-in account created the channel. */
  isOwner: boolean;
  /** True when the channel is marked as favourite in the app config. */
  isFavorite: boolean;
  /** True when the channel is hidden from the channel lists in the app config. */
  isHidden: boolean;
  /** Relative URL serving the channel avatar. */
  imageUrl: string;
  /** True when the app already has an indexed file database for this channel. */
  hasDatabase: boolean;
}

/** Channel plus indexed-content statistics. */
export interface ApiChannelDetailDto extends ApiChannelDto {
  fileCount: number;
  folderCount: number;
  totalSize: number;
  totalSizeText: string;
  audioCount: number;
  videoCount: number;
  photoCount: number;
  documentCount: number;
  /** True while a background refresh of this channel is running. */
  isRefreshing: boolean;
  /** True when the account can index/refresh this channel from the UI. */
  canRefresh: boolean;
}

/** What the last finished index refresh added, per media type. */
export interface ApiChannelRefreshResultDto {
  channelId: string;
  /** ISO date. */
  startedAt: string;
  /** ISO date. */
  finishedAt: string;
  /** The scan threw; the counts are what it managed before failing. */
  failed: boolean;
  /** The refresh was canceled; the counts are what was indexed before it stopped. */
  canceled: boolean;
  photos: number;
  videos: number;
  audios: number;
  documents: number;
  total: number;
}

/** Media filter of a refresh: `documents`, `audio`, `video` or `photos`. */
export type ApiChannelRefreshFilterType = 'documents' | 'audio' | 'video' | 'photos';

/** Live counters of one media filter of a running refresh. */
export interface ApiChannelRefreshFilterProgressDto {
  /** documents, audio, video or photos. */
  type: ApiChannelRefreshFilterType | (string & {});
  /** Messages read from Telegram so far. */
  fetched: number;
  /**
   * Messages of this type in the whole channel as Telegram reports it; null until the first page
   * arrives. An incremental scan stops at the last indexed message, so it usually ends well below this.
   */
  total: number | null;
  /** The search of this type is over. */
  done: boolean;
}

/** Phase of a running refresh: `searching` (Telegram), `indexing` (writing the new files) or `done`. */
export type ApiChannelRefreshPhase = 'searching' | 'indexing' | 'done';

/** Live progress of a running channel refresh. */
export interface ApiChannelRefreshProgressDto {
  /** Always true (the endpoint answers `data: null` when nothing runs). */
  running: boolean;
  /** searching (Telegram), indexing (writing the new files) or done. */
  phase: ApiChannelRefreshPhase | (string & {});
  /** ISO date. */
  startedAt: string;
  filters: ApiChannelRefreshFilterProgressDto[];
  /** Files found that were not indexed yet (known once the search is over). */
  newFiles: number;
  /** New files written to the index so far. */
  indexed: number;
  /** A cancel was requested; the refresh stops after the page or batch in flight. */
  cancelRequested: boolean;
}

/** A Telegram chat folder (filter) with the channels it contains. */
export interface ApiChannelFolderDto {
  id: number;
  title: string;
  iconEmoji: string | null;
  channels: ApiChannelDto[];
  /** Computed on the server (`channels.length`). */
  channelCount: number;
}

/** Channels grouped by Telegram folder. */
export interface ApiChannelsWithFoldersDto {
  folders: ApiChannelFolderDto[];
  ungrouped: ApiChannelDto[];
  totalChannels: number;
}

/** Body of `POST /api/v1/channels`. */
export interface CreateChannelRequest {
  /** Channel title. */
  title: string;
  /** Channel description. */
  about?: string | null;
  /** Create the MongoDB file database for the channel right away (default true). */
  createDatabase?: boolean;
}

/** Body of `POST /api/v1/channels/{id}/refresh`. Every media type defaults to true. */
export interface RefreshChannelRequest {
  includeDocuments?: boolean;
  includeAudio?: boolean;
  includeVideo?: boolean;
  includePhotos?: boolean;
  /** Re-scan the channel even when a previous scan already completed. */
  force?: boolean;
}

/** `photo`, `video`, `audio` or `document`. */
export type ApiChatMediaType = 'photo' | 'video' | 'audio' | 'document';

/** A raw Telegram message from a chat history. */
export interface ApiChatMessageDto {
  id: number;
  /** ISO date. */
  date: string;
  text: string | null;
  /** True when the message carries a document/media attachment. */
  hasMedia: boolean;
  /** `photo`, `video`, `audio`, `document` or null. */
  mediaType: ApiChatMediaType | (string & {}) | null;
  fileName: string | null;
  fileSize: number;
  mimeType: string | null;
  /** Sender display name, when resolvable. */
  from: string | null;
}

/** Body of `POST /api/v1/channels/{id}/leave` and delete operations. */
export interface ChannelDeleteRequest {
  /** Also drop the local MongoDB database that indexes the channel. */
  deleteLocalDatabase?: boolean;
  /** Delete the channel on Telegram (owner only) instead of just leaving it. */
  deleteOnTelegram?: boolean;
}

/** Invitation link of a channel (mirrors `Models.InvitationInfo`, returned by `GET /api/v1/channels/{id}/invitation`). */
export interface InvitationInfo {
  invitationHash: string | null;
  invitationLink: string | null;
}

/** Query of `GET /api/v1/channels`. `sortBy`: `name` (default) or `id`. */
export interface ChannelListQuery extends PagedQuery {
  /** Only channels that have a local index. */
  onlySaved?: boolean;
  favoritesOnly?: boolean;
  hiddenOnly?: boolean;
  /** Include hidden channels even when the "show hidden channels" setting is off. */
  includeHidden?: boolean;
  /** Case-insensitive substring match on the name. */
  search?: string;
}

/** Query of `GET /api/v1/channels/favorites`. */
export interface ChannelFavoritesQuery {
  /** Re-read the chat list from Telegram (default true). */
  refresh?: boolean;
}

/** Query of `GET /api/v1/channels/{id}/messages`. */
export interface ChannelMessagesQuery {
  /** Messages to return (1-100, default 30). */
  limit?: number;
  /** Messages to skip from the newest one. */
  offset?: number;
  /** Return only messages carrying a file. */
  onlyMedia?: boolean;
}
