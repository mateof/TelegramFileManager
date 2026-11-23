import type { HttpClient, PageInfo, Paged } from '../http';
import type {
  ApiFileDto,
  ApiFolderContentsDto,
  ApiFolderStatsDto,
  BrowseQuery,
  ContentUrlOptions,
  CopyMoveRequest,
  CreateFolderRequest,
  FileIdsRequest,
  FileSearchQuery,
  FolderContentsPage,
  FolderStatsQuery,
  PreloadFilesRequest,
  RenameRequest,
  UploadFileOptions
} from '../types/files';
import type { TransferAcceptedDto } from '../types/transfers';
import type { ChannelId } from '../types/channels';
import type { ImportStartedDto } from '../types/events';

const enc = encodeURIComponent;
const base = (channelId: ChannelId) => `/api/v1/channels/${enc(channelId)}/files`;

/** Builds the multipart body of the upload endpoints (`file` part + optional `path` field). */
export function uploadForm(file: Blob, options: UploadFileOptions = {}): FormData {
  const form = new FormData();
  const name = options.fileName ?? (typeof File !== 'undefined' && file instanceof File ? file.name : 'file');
  form.append('file', file, name);
  if (options.path) form.append('path', options.path);
  return form;
}

function emptyPage(itemCount: number): PageInfo {
  return {
    page: 1,
    pageSize: itemCount,
    totalItems: itemCount,
    totalPages: itemCount ? 1 : 0,
    hasNext: false,
    hasPrevious: false
  };
}

/** Browses a folder listing endpoint (the `data` is an ApiFolderContentsDto, the paging sits next to it). */
export async function browseFolder(http: HttpClient, path: string, query: BrowseQuery, signal?: AbortSignal): Promise<FolderContentsPage> {
  const envelope = await http.envelope<ApiFolderContentsDto>('GET', path, { query: { ...query }, signal });
  const contents = envelope.data as ApiFolderContentsDto;
  return { contents, page: envelope.page ?? emptyPage(contents?.items?.length ?? 0) };
}

/** Files and folders of a channel index: `/api/v1/channels/{channelId}/files`. */
export function createFilesApi(http: HttpClient) {
  return {
    /** GET /api/v1/channels/{channelId}/files (paged: `contents.items` is the current page) */
    list: (channelId: ChannelId, query: BrowseQuery = {}, signal?: AbortSignal): Promise<FolderContentsPage> =>
      browseFolder(http, base(channelId), query, signal),
    /** GET /api/v1/channels/{channelId}/files/search (paged) */
    search: (channelId: ChannelId, query: FileSearchQuery, signal?: AbortSignal): Promise<Paged<ApiFileDto>> =>
      http.getPaged<ApiFileDto>(`${base(channelId)}/search`, { query: { ...query }, signal }),
    /** GET /api/v1/channels/{channelId}/files/{fileId} */
    get: (channelId: ChannelId, fileId: string, signal?: AbortSignal) =>
      http.get<ApiFileDto>(`${base(channelId)}/${enc(fileId)}`, { signal }),
    /** GET /api/v1/channels/{channelId}/files/stats */
    stats: (channelId: ChannelId, query: FolderStatsQuery = {}, signal?: AbortSignal) =>
      http.get<ApiFolderStatsDto>(`${base(channelId)}/stats`, { query: { ...query }, signal }),
    /** POST /api/v1/channels/{channelId}/files/folders (201; 409 when the name exists) */
    createFolder: (channelId: ChannelId, body: CreateFolderRequest) =>
      http.post<ApiFileDto>(`${base(channelId)}/folders`, body),
    /** PUT /api/v1/channels/{channelId}/files/{fileId}/name */
    rename: (channelId: ChannelId, fileId: string, body: RenameRequest) =>
      http.put<ApiFileDto>(`${base(channelId)}/${enc(fileId)}/name`, body),
    /** POST /api/v1/channels/{channelId}/files/delete: also frees the Telegram messages. */
    delete: (channelId: ChannelId, body: FileIdsRequest, signal?: AbortSignal) =>
      http.post<TransferAcceptedDto>(`${base(channelId)}/delete`, body, { signal }),
    /** POST /api/v1/channels/{channelId}/files/copy (index level, no extra storage) */
    copy: (channelId: ChannelId, body: CopyMoveRequest, signal?: AbortSignal) =>
      http.post<TransferAcceptedDto>(`${base(channelId)}/copy`, body, { signal }),
    /** POST /api/v1/channels/{channelId}/files/move */
    move: (channelId: ChannelId, body: CopyMoveRequest, signal?: AbortSignal) =>
      http.post<TransferAcceptedDto>(`${base(channelId)}/move`, body, { signal }),
    /** POST /api/v1/channels/{channelId}/files/upload (multipart, 202): uploads a browser file to the channel. */
    upload: (channelId: ChannelId, file: Blob, options: UploadFileOptions = {}, signal?: AbortSignal) =>
      http.post<void>(`${base(channelId)}/upload`, uploadForm(file, options), { signal }),
    /** GET /api/v1/channels/{channelId}/files/export: the whole index as a JSON file download. */
    exportUrl: (channelId: ChannelId) => http.url(`${base(channelId)}/export`),
    /** POST /api/v1/channels/{channelId}/files/import (multipart `file`, 202): imports an exported index; progress as `ImportProgress`. */
    import: (channelId: ChannelId, file: Blob, signal?: AbortSignal) =>
      http.post<ImportStartedDto>(`${base(channelId)}/import`, uploadForm(file, {}), { signal }),
    /** POST /api/v1/channels/{channelId}/files/preload (202): downloads files/folders into the streaming cache. */
    preload: (channelId: ChannelId, body: PreloadFilesRequest) =>
      http.post<TransferAcceptedDto>(`${base(channelId)}/preload`, body),
    /** URL of GET /api/v1/channels/{channelId}/files/{fileId}/content (inline or attachment, Range-capable). */
    contentUrl: (channelId: ChannelId, fileId: string, options: ContentUrlOptions = {}) =>
      http.url(`${base(channelId)}/${enc(fileId)}/content`, { ...options }),
    /** URL of GET /api/v1/channels/{channelId}/files/{fileId}/stream (video: native or transcoded). */
    streamUrl: (channelId: ChannelId, fileId: string) => http.url(`${base(channelId)}/${enc(fileId)}/stream`)
  };
}

export type FilesApi = ReturnType<typeof createFilesApi>;
