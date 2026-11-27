// Public surface of @tfm/api-client (the SignalR hubs live in the `./signalr` entry point).
export * from './http';
export * from './client';

// DTOs and request/query shapes, one file per API area
export * from './types/auth';
export type * from './types/channels';
export * from './types/config';
export * from './types/deleteOperations';
export type * from './types/files';
export * from './types/library';
export type * from './types/local';
export type * from './types/player';
export type * from './types/playlists';
export type * from './types/search';
export type * from './types/shares';
export type * from './types/system';
export * from './types/transfers';
export type * from './types/webAuth';
export type * from './types/events';

// One module per controller
export { createAuthApi, type AuthApi } from './modules/auth';
export { createChannelsApi, type ChannelsApi } from './modules/channels';
export { createConfigApi, type ConfigApi } from './modules/config';
export { createDeleteOperationsApi, type DeleteOperationsApi } from './modules/deleteOperations';
export { createFilesApi, type FilesApi } from './modules/files';
export { createLibraryApi, type LibraryApi } from './modules/library';
export { createLocalApi, type LocalApi } from './modules/local';
export { createPlayerApi, type PlayerApi } from './modules/player';
export { createPlaylistsApi, type PlaylistsApi } from './modules/playlists';
export { createSearchApi, type SearchApi } from './modules/search';
export { createSharesApi, type SharesApi } from './modules/shares';
export { createSystemApi, type SystemApi } from './modules/system';
export { createTransfersApi, type TransfersApi } from './modules/transfers';
export { createWebAuthApi, type WebAuthApi } from './modules/webAuth';
