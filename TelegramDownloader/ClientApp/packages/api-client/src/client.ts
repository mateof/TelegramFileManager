import { HttpClient, type HttpClientOptions } from './http';
import { createAuthApi } from './modules/auth';
import { createChannelsApi } from './modules/channels';
import { createConfigApi } from './modules/config';
import { createDeleteOperationsApi } from './modules/deleteOperations';
import { createFilesApi } from './modules/files';
import { createLibraryApi } from './modules/library';
import { createLocalApi } from './modules/local';
import { createPlaylistsApi } from './modules/playlists';
import { createSearchApi } from './modules/search';
import { createSharesApi } from './modules/shares';
import { createSystemApi } from './modules/system';
import { createTransfersApi } from './modules/transfers';
import { createWebAuthApi } from './modules/webAuth';

/**
 * The whole v1 API, one module per controller:
 *
 * ```ts
 * const api = createApiClient();
 * const channels = await api.channels.list({ signal });
 * ```
 */
export function createApiClient(options: HttpClientOptions = {}) {
  const http = new HttpClient(options);
  return {
    http,
    auth: createAuthApi(http),
    webAuth: createWebAuthApi(http),
    channels: createChannelsApi(http),
    files: createFilesApi(http),
    transfers: createTransfersApi(http),
    local: createLocalApi(http),
    deleteOperations: createDeleteOperationsApi(http),
    playlists: createPlaylistsApi(http),
    shares: createSharesApi(http),
    config: createConfigApi(http),
    system: createSystemApi(http),
    library: createLibraryApi(http),
    search: createSearchApi(http)
  };
}

export type ApiClient = ReturnType<typeof createApiClient>;
