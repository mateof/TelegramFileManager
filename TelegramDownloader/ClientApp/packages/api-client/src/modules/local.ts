import type { HttpClient } from '../http';
import type {
  ApiFileDto,
  BrowseQuery,
  FolderContentsPage,
  LocalCopyMoveRequest,
  LocalCreateFolderRequest,
  LocalDeleteRequest,
  LocalRenameRequest,
  UploadFileOptions
} from '../types/files';
import type { DirectorySizeModel, LocalPathQuery, LocalSizeQuery } from '../types/local';
import type { TransferAcceptedDto } from '../types/transfers';
import { browseFolder, uploadForm } from './files';

const BASE = '/api/v1/local';

/** The server local storage (download target, upload source). Paths are relative to the local root. */
export function createLocalApi(http: HttpClient) {
  return {
    /** GET /api/v1/local (paged: `contents.items` is the current page; `path` selects the folder) */
    list: (query: BrowseQuery = {}, signal?: AbortSignal): Promise<FolderContentsPage> => browseFolder(http, BASE, query, signal),
    /** GET /api/v1/local/info?path= */
    info: (query: LocalPathQuery, signal?: AbortSignal) => http.get<ApiFileDto>(`${BASE}/info`, { query: { ...query }, signal }),
    /** GET /api/v1/local/size?path=: size and per extension breakdown of a folder tree (can be slow). */
    size: (query: LocalSizeQuery = {}, signal?: AbortSignal) =>
      http.get<DirectorySizeModel>(`${BASE}/size`, { query: { ...query }, signal }),
    /** POST /api/v1/local/folders (201) */
    createFolder: (body: LocalCreateFolderRequest) => http.post<ApiFileDto>(`${BASE}/folders`, body),
    /** POST /api/v1/local/rename */
    rename: (body: LocalRenameRequest) => http.post<ApiFileDto>(`${BASE}/rename`, body),
    /** POST /api/v1/local/delete */
    delete: (body: LocalDeleteRequest) => http.post<TransferAcceptedDto>(`${BASE}/delete`, body),
    /** GET /api/v1/local/download?path=: file stream with range support (usable as a media src). */
    downloadUrl: (path: string) => http.url(`${BASE}/download`, { path }),
    /** POST /api/v1/local/upload (multipart, 201): stores a browser file under `options.path`. */
    upload: (file: Blob, options: UploadFileOptions = {}, signal?: AbortSignal) =>
      http.post<ApiFileDto>(`${BASE}/upload`, uploadForm(file, options), { signal }),
    /** POST /api/v1/local/copy: copies files/folders into `targetPath` (created when missing). */
    copy: (body: LocalCopyMoveRequest) => http.post<TransferAcceptedDto>(`${BASE}/copy`, body),
    /** POST /api/v1/local/move */
    move: (body: LocalCopyMoveRequest) => http.post<TransferAcceptedDto>(`${BASE}/move`, body),
    /** GET /api/v1/local/download?path=&disposition=inline: URL to view a local file in the browser. */
    inlineUrl: (path: string) => http.url(`${BASE}/download`, { path, disposition: 'inline' }),
    /** POST /api/v1/local/cache/clear: empties the streaming/temporary cache. */
    clearCache: () => http.post<void>(`${BASE}/cache/clear`)
  };
}

export type LocalApi = ReturnType<typeof createLocalApi>;
