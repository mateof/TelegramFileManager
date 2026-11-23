import { normalizePath } from '@tfm/file-manager';
import { api } from '@/lib/api';
import { toEntry } from './entries';
import type { FolderItem } from './FolderBrowser';

/** Path relative to the local root (`/music/rock` → `music/rock`, root → ``). */
export function localRel(path: string): string {
  return normalizePath(path).slice(1);
}

/** Sub-folders of a local folder (folder pickers). */
export async function listLocalFolders(path: string, signal: AbortSignal): Promise<FolderItem[]> {
  const res = await api.local.list({ path: localRel(path), page: 1, pageSize: 1000, sortBy: 'name' }, signal);
  return res.contents.items.filter((i) => !i.isFile).map((dto) => {
    const e = toEntry(dto);
    return { name: e.name, path: e.path };
  });
}

export async function createLocalFolder(parent: string, name: string): Promise<void> {
  await api.local.createFolder({ path: localRel(parent), name });
}
