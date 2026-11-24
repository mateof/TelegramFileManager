import type { HttpClient, Paged } from '../http';
import type {
  ApiChannelDetailDto,
  ApiChannelDto,
  ApiChannelRefreshProgressDto,
  ApiChannelRefreshResultDto,
  ApiChannelsWithFoldersDto,
  ApiChatMessageDto,
  ChannelDeleteRequest,
  ChannelId,
  ChannelFavoritesQuery,
  ChannelListQuery,
  ChannelMessagesQuery,
  CreateChannelRequest,
  InvitationInfo,
  RefreshChannelRequest
} from '../types/channels';

const BASE = '/api/v1/channels';
const enc = encodeURIComponent;

/** Telegram chats/channels, their favourite/hidden flags, local index and refresh. */
export function createChannelsApi(http: HttpClient) {
  return {
    /** GET /api/v1/channels (paged) */
    list: (query: ChannelListQuery = {}, signal?: AbortSignal): Promise<Paged<ApiChannelDto>> =>
      http.getPaged<ApiChannelDto>(BASE, { query: { ...query }, signal }),
    /** GET /api/v1/channels/folders */
    folders: (signal?: AbortSignal) => http.get<ApiChannelsWithFoldersDto>(`${BASE}/folders`, { signal }),
    /** GET /api/v1/channels/favorites */
    favorites: (query: ChannelFavoritesQuery = {}, signal?: AbortSignal) =>
      http.get<ApiChannelDto[]>(`${BASE}/favorites`, { query: { ...query }, signal }),
    /** POST /api/v1/channels/{id}/favorite */
    addFavorite: (id: ChannelId) => http.post<void>(`${BASE}/${enc(id)}/favorite`),
    /** DELETE /api/v1/channels/{id}/favorite */
    removeFavorite: (id: ChannelId) => http.delete<void>(`${BASE}/${enc(id)}/favorite`),
    /** GET /api/v1/channels/hidden */
    hidden: (signal?: AbortSignal) => http.get<ApiChannelDto[]>(`${BASE}/hidden`, { signal }),
    /** POST /api/v1/channels/{id}/hidden */
    hide: (id: ChannelId) => http.post<void>(`${BASE}/${enc(id)}/hidden`),
    /** DELETE /api/v1/channels/{id}/hidden */
    unhide: (id: ChannelId) => http.delete<void>(`${BASE}/${enc(id)}/hidden`),
    /** GET /api/v1/channels/{id} */
    get: (id: ChannelId, signal?: AbortSignal) => http.get<ApiChannelDetailDto>(`${BASE}/${enc(id)}`, { signal }),
    /** POST /api/v1/channels (201) */
    create: (body: CreateChannelRequest, signal?: AbortSignal) => http.post<ApiChannelDto>(BASE, body, { signal }),
    /** POST /api/v1/channels/{id}/database: creates the local index. */
    createDatabase: (id: ChannelId) => http.post<void>(`${BASE}/${enc(id)}/database`),
    /** DELETE /api/v1/channels/{id}/database: drops the local index (files stay on Telegram). */
    deleteDatabase: (id: ChannelId) => http.delete<void>(`${BASE}/${enc(id)}/database`),
    /** POST /api/v1/channels/{id}/leave: leaves (or, with `deleteOnTelegram`, deletes) the channel. */
    leave: (id: ChannelId, body: ChannelDeleteRequest = {}) => http.post<void>(`${BASE}/${enc(id)}/leave`, body),
    /** POST /api/v1/channels/{id}/refresh (202; 409 `already_running`): starts a background index refresh. */
    refresh: (id: ChannelId, body: RefreshChannelRequest = {}) => http.post<void>(`${BASE}/${enc(id)}/refresh`, body),
    /** GET /api/v1/channels/{id}/refresh: true while a refresh runs. */
    isRefreshing: (id: ChannelId, signal?: AbortSignal) => http.get<boolean>(`${BASE}/${enc(id)}/refresh`, { signal }),
    /** GET /api/v1/channels/{id}/refresh/progress: null when nothing runs. */
    refreshProgress: (id: ChannelId, signal?: AbortSignal) =>
      http.get<ApiChannelRefreshProgressDto | null>(`${BASE}/${enc(id)}/refresh/progress`, { signal }),
    /** DELETE /api/v1/channels/{id}/refresh (202; 404 when nothing runs) */
    cancelRefresh: (id: ChannelId) => http.delete<void>(`${BASE}/${enc(id)}/refresh`),
    /** GET /api/v1/channels/{id}/refresh/result: null when not refreshed since the server started. */
    refreshResult: (id: ChannelId, signal?: AbortSignal) =>
      http.get<ApiChannelRefreshResultDto | null>(`${BASE}/${enc(id)}/refresh/result`, { signal }),
    /** GET /api/v1/channels/{id}/messages: recent history straight from Telegram. */
    messages: (id: ChannelId, query: ChannelMessagesQuery = {}, signal?: AbortSignal) =>
      http.get<ApiChatMessageDto[]>(`${BASE}/${enc(id)}/messages`, { query: { ...query }, signal }),
    /**
     * URL of GET /api/v1/channels/{id}/messages/{messageId}/content: the file of
     * a message, straight from Telegram (no index entry needed), Range-capable.
     * `<video>`/`<audio>`/`<img>` src, or a download with `attachment`.
     */
    messageContentUrl: (id: ChannelId, messageId: number, disposition?: 'inline' | 'attachment') =>
      http.url(`${BASE}/${enc(id)}/messages/${messageId}/content`, { disposition: disposition === 'attachment' ? 'attachment' : undefined }),
    /** GET /api/v1/channels/{id}/image: avatar (image/jpeg, 404 without one). URL for `<img src>`. */
    imageUrl: (id: ChannelId) => http.url(`${BASE}/${enc(id)}/image`),
    /** GET /api/v1/channels/{id}/invitation */
    invitation: (id: ChannelId, signal?: AbortSignal) => http.get<InvitationInfo>(`${BASE}/${enc(id)}/invitation`, { signal }),
    /** POST /api/v1/channels/join?hash=: joins with the part after `t.me/+` or `joinchat/`. */
    join: (hash: string) => http.post<void>(`${BASE}/join`, undefined, { query: { hash } })
  };
}

export type ChannelsApi = ReturnType<typeof createChannelsApi>;
