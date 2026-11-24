import type { FileSystemProvider } from '@tfm/file-manager';
import { entryMediaKind } from '@tfm/file-manager';
import { toast } from '@tfm/ui';
import { downloadUrls, uploadBody, xhrUpload } from '@/features/files/browser';
import { withoutPendingDeletes } from '@/features/files/deletes/deleteStore';
import { startDelete } from '@/features/files/deletes/deleteTracker';
import { apiFilter, apiSort, dtoOf, toEntry } from '@/features/files/entries';
import { localRel } from '@/features/files/local';
import { t } from '@/i18n';
import { api } from '@/lib/api';

export const LOCAL_PROVIDER_ID = 'local';

/** Server local storage (`/api/v1/local/*`). Entry ids are paths relative to the local root. */
export function createLocalProvider(rootName: string): FileSystemProvider {
  const skipped = (res: { skipped?: string[] } | null | undefined) => {
    if (res?.skipped?.length) toast.warning(t('{count} entries were skipped', { count: res.skipped.length }));
  };
  return {
    id: LOCAL_PROVIDER_ID,
    rootName,
    capabilities: {
      rename: true,
      move: true,
      copy: true,
      delete: true,
      createFolder: true,
      upload: true,
      download: true,
      search: false,
      preview: true,
      thumbnails: true,
      paging: true,
      sortFields: ['name', 'modifiedAt', 'size', 'type'],
      serverFilter: true
    },

    async list(path, options) {
      const res = await api.local.list(
        {
          path: localRel(path),
          page: options?.page,
          pageSize: options?.pageSize,
          ...apiSort(options?.sort),
          filter: apiFilter(options?.filter),
          search: options?.filter?.text || undefined
        },
        options?.signal
      );
      let items = withoutPendingDeletes(LOCAL_PROVIDER_ID, res.contents.items.map(toEntry));
      if (options?.filter?.kind) items = items.filter((e) => e.kind === options.filter?.kind);
      return { items, page: res.page.page, pageSize: res.page.pageSize, totalItems: res.page.totalItems, hasNext: res.page.hasNext };
    },

    async createFolder(parentPath, name) {
      const dto = await api.local.createFolder({ path: localRel(parentPath), name });
      return dto ? toEntry(dto) : undefined;
    },
    async rename(entry, newName) {
      const dto = await api.local.rename({ path: entry.id, newName });
      return dto ? toEntry(dto) : undefined;
    },
    async move(entries, targetPath) {
      skipped(await api.local.move({ paths: entries.map((e) => e.id), targetPath: localRel(targetPath) }));
    },
    async copy(entries, targetPath) {
      skipped(await api.local.copy({ paths: entries.map((e) => e.id), targetPath: localRel(targetPath) }));
    },
    async delete(entries) {
      // Runs in the background with a progress panel (or the old endpoint on older servers)
      await startDelete({ kind: 'local', placeName: rootName, providerId: LOCAL_PROVIDER_ID, entries });
    },
    async upload(targetPath, file, options) {
      await xhrUpload(api.http.url('/api/v1/local/upload'), uploadBody(file, localRel(targetPath)), file, options);
    },
    async download(entries) {
      const files = entries.filter((e) => e.kind === 'file');
      if (files.length < entries.length) toast.info(t('Folders cannot be downloaded to the browser'));
      await downloadUrls(files.map((e) => ({ url: api.local.downloadUrl(e.id), name: e.name })));
    },

    previewUrl: (entry) => (entry.kind === 'file' ? dtoOf(entry).contentUrl ?? api.local.inlineUrl(entry.id) : undefined),
    thumbnailUrl: (entry) => (entry.kind === 'file' && entryMediaKind(entry) === 'image' ? api.local.inlineUrl(entry.id) : undefined)
  };
}
