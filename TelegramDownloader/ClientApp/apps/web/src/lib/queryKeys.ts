/**
 * Query keys shared across features (so one feature can invalidate another's
 * data). Feature-private keys can live in the feature folder, but start them
 * with the feature name.
 */
export const queryKeys = {
  webAuthStatus: ['web-auth', 'status'] as const,
  telegramStatus: ['auth', 'status'] as const,
  channels: ['channels', 'list'] as const,
  channelFolders: ['channels', 'folders'] as const,
  channel: (id: string | number) => ['channels', 'detail', String(id)] as const,
  serverInfo: ['system', 'info'] as const,
  setupStatus: ['system', 'setup'] as const
};
