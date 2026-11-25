import type { FileEntry, FileSystemProvider, Page, ProviderCapabilities } from '@tfm/file-manager';
import { toast } from '@tfm/ui';
import { downloadUrls, uploadBody, xhrUpload } from '@/features/files/browser';
import { withoutPendingDeletes } from '@/features/files/deletes/deleteStore';
import { startDelete } from '@/features/files/deletes/deleteTracker';
import { apiFilter, apiSort, dtoOf, toApiFolder, toEntry } from '@/features/files/entries';
import { t } from '@/i18n';
import { api } from '@/lib/api';

export interface ChannelProviderOptions {
  channelId: string;
  /** Display name of the root. */
  rootName: string;
  /** Browse an imported shared collection (read only). */
  collectionId?: string;
}

/** Provider id of a channel (or of a collection), used to invalidate its listings. */
export function channelProviderId(channelId: string, collectionId?: string): string {
  return collectionId ? `collection:${collectionId}` : `channel:${channelId}`;
}

/**
 * Telegram channel index (`/api/v1/channels/{cid}/files*`) as a file manager
 * provider. With `collectionId` it reads an imported shared collection and
 * is read only (mutations always act on the channel's own index).
 */
export function createChannelProvider({ channelId, rootName, collectionId }: ChannelProviderOptions): FileSystemProvider {
  const readOnly = !!collectionId;
  const capabilities: ProviderCapabilities = {
    rename: !readOnly,
    move: !readOnly,
    copy: !readOnly,
    delete: !readOnly,
    createFolder: !readOnly,
    upload: !readOnly,
    download: true,
    search: true,
    preview: true,
    thumbnails: true,
    paging: true,
    sortFields: ['name', 'modifiedAt', 'size', 'type'],
    serverFilter: true
  };

  const page = (items: FileEntry[], p: { page: number; pageSize: number; totalItems: number; hasNext: boolean }): Page<FileEntry> => ({
    items,
    page: p.page,
    pageSize: p.pageSize,
    totalItems: p.totalItems,
    hasNext: p.hasNext
  });

  const id = channelProviderId(channelId, collectionId);

  return {
    id,
    rootName,
    capabilities,

    async list(path, options) {
      const res = await api.files.list(
        channelId,
        {
          path: toApiFolder(path),
          page: options?.page,
          pageSize: options?.pageSize,
          ...apiSort(options?.sort),
          filter: apiFilter(options?.filter),
          search: options?.filter?.text || undefined,
          collectionId
        },
        options?.signal
      );
      let items = withoutPendingDeletes(id, res.contents.items.map(toEntry));
      if (options?.filter?.kind) items = items.filter((e) => e.kind === options.filter?.kind);
      return page(items, res.page);
    },

    async search(path, query, options) {
      const res = await api.files.search(
        channelId,
        {
          q: query,
          path: toApiFolder(path),
          page: options?.page,
          pageSize: options?.pageSize,
          ...apiSort(options?.sort),
          filter: apiFilter(options?.filter),
          collectionId
        },
        options?.signal
      );
      return page(withoutPendingDeletes(id, res.items.map(toEntry)), res.page);
    },

    async createFolder(parentPath, name) {
      const dto = await api.files.createFolder(channelId, { path: toApiFolder(parentPath), name });
      return dto ? toEntry(dto) : undefined;
    },

    async rename(entry, newName) {
      const dto = await api.files.rename(channelId, entry.id, { newName });
      return dto ? toEntry(dto) : undefined;
    },

    async move(entries, targetPath) {
      await api.files.move(channelId, { ids: entries.map((e) => e.id), targetPath: toApiFolder(targetPath) });
    },

    async copy(entries, targetPath) {
      await api.files.copy(channelId, { ids: entries.map((e) => e.id), targetPath: toApiFolder(targetPath) });
    },

    async delete(entries) {
      // Runs in the background with a progress panel (or the old endpoint on older servers)
      await startDelete({ kind: 'channel', channelId, placeName: rootName, providerId: id, entries });
    },

    async upload(targetPath, file, options) {
      // The server stages the bytes and queues the Telegram upload (visible in Transfers)
      await xhrUpload(api.http.url(`/api/v1/channels/${encodeURIComponent(channelId)}/files/upload`), uploadBody(file, toApiFolder(targetPath)), file, options);
    },

    async download(entries) {
      const files = entries.filter((e) => e.kind === 'file');
      if (files.length < entries.length) toast.info(t('Folders cannot be downloaded to the browser: use "Download to the server"'));
      await downloadUrls(
        files.map((e) => {
          const dto = dtoOf(e);
          return { url: dto.contentDownloadUrl ?? api.files.contentUrl(channelId, e.id, { disposition: 'attachment', collectionId }), name: e.name };
        })
      );
    },

    previewUrl: (entry) => (entry.kind === 'file' ? dtoOf(entry).contentUrl ?? undefined : undefined),
    thumbnailUrl: (entry) => dtoOf(entry)?.thumbnailUrl ?? undefined
  };
}
