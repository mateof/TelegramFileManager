import { mediaKindOf } from '../core/media';
import { extensionOf, isSameOrDescendant, joinPath, normalizePath, parentPath } from '../core/paths';
import { filterEntries, sortEntries } from '../core/sortFilter';
import type { FileEntry, FileSystemProvider, ListOptions, Page, ProviderCapabilities, SearchOptions } from '../types';

export interface MemoryFile {
  /** Full path, e.g. `/music/song.mp3`. Folders end with `/` or have `folder: true`. */
  path: string;
  folder?: boolean;
  size?: number;
  modifiedAt?: string;
}

export interface MemoryProviderOptions {
  id?: string;
  rootName?: string;
  files?: MemoryFile[];
  capabilities?: Partial<ProviderCapabilities>;
  /** Simulated latency of every call. */
  latencyMs?: number;
}

const ALL_CAPS: ProviderCapabilities = {
  rename: true,
  move: true,
  copy: true,
  delete: true,
  createFolder: true,
  upload: true,
  download: false,
  search: true,
  preview: false,
  thumbnails: false,
  paging: false
};

function makeEntry(path: string, folder: boolean, size?: number, modifiedAt?: string): FileEntry {
  const p = normalizePath(path);
  const name = p.slice(p.lastIndexOf('/') + 1);
  return {
    id: p,
    name,
    kind: folder ? 'folder' : 'file',
    path: p,
    parentPath: parentPath(p),
    size: folder ? undefined : size ?? 0,
    modifiedAt: modifiedAt ?? new Date(0).toISOString(),
    extension: folder ? undefined : extensionOf(name),
    mediaKind: folder ? undefined : mediaKindOf(name)
  };
}

function paginate(items: FileEntry[], options: ListOptions | undefined, paging: boolean): Page<FileEntry> {
  if (!paging) return { items, page: 1, pageSize: items.length, totalItems: items.length, hasNext: false };
  const page = options?.page ?? 1;
  const size = options?.pageSize ?? 50;
  const slice = items.slice((page - 1) * size, page * size);
  return { items: slice, page, pageSize: size, totalItems: items.length, hasNext: page * size < items.length };
}

/**
 * In-memory provider: a working example of a {@link FileSystemProvider}, handy
 * for demos, tests and as a template for a real one. Entry ids are their paths.
 */
export function createMemoryProvider(options: MemoryProviderOptions = {}): FileSystemProvider & { snapshot(): FileEntry[] } {
  const entries = new Map<string, FileEntry>();
  const ensureFolder = (path: string) => {
    const p = normalizePath(path);
    if (p === '/' || entries.has(p)) return;
    ensureFolder(parentPath(p));
    entries.set(p, makeEntry(p, true));
  };
  for (const f of options.files ?? []) {
    const folder = f.folder || f.path.endsWith('/');
    ensureFolder(parentPath(f.path));
    if (folder) ensureFolder(f.path);
    else entries.set(normalizePath(f.path), makeEntry(f.path, false, f.size, f.modifiedAt));
  }
  const caps: ProviderCapabilities = { ...ALL_CAPS, ...options.capabilities };
  const wait = () => (options.latencyMs ? new Promise((r) => setTimeout(r, options.latencyMs)) : Promise.resolve());
  const childrenOf = (path: string) => [...entries.values()].filter((e) => e.parentPath === normalizePath(path));
  const withCounts = (e: FileEntry): FileEntry =>
    e.kind === 'folder' ? { ...e, childCount: childrenOf(e.path).length, hasChildren: childrenOf(e.path).some((c) => c.kind === 'folder') } : e;
  const conflict = (path: string) => {
    if (entries.has(normalizePath(path))) throw new Error(`An entry named ${path} already exists`);
  };
  const relocate = (entry: FileEntry, targetFolder: string, keepSource: boolean) => {
    const dest = joinPath(targetFolder, entry.name);
    conflict(dest);
    const moved = [...entries.values()].filter((e) => isSameOrDescendant(e.path, entry.path));
    for (const e of moved) {
      const newPath = dest + e.path.slice(entry.path.length);
      if (!keepSource) entries.delete(e.path);
      entries.set(newPath, { ...e, id: newPath, path: newPath, parentPath: parentPath(newPath) });
    }
  };

  return {
    id: options.id ?? 'memory',
    rootName: options.rootName ?? 'Files',
    capabilities: caps,
    snapshot: () => [...entries.values()],

    async list(path, opts) {
      await wait();
      const items = childrenOf(path).map(withCounts);
      const filtered = filterEntries(items, opts?.filter);
      return paginate(sortEntries(filtered, opts?.sort ?? { field: 'name', direction: 'asc' }), opts, caps.paging);
    },
    async search(path, query, opts?: SearchOptions) {
      await wait();
      const q = query.toLocaleLowerCase();
      const items = [...entries.values()].filter((e) => isSameOrDescendant(e.parentPath, path) && e.name.toLocaleLowerCase().includes(q));
      return paginate(sortEntries(filterEntries(items, opts?.filter), opts?.sort ?? { field: 'name', direction: 'asc' }), opts, caps.paging);
    },
    async createFolder(parent, name) {
      await wait();
      const p = joinPath(parent, name);
      conflict(p);
      const e = makeEntry(p, true);
      entries.set(p, e);
      return e;
    },
    async rename(entry, newName) {
      await wait();
      const dest = joinPath(entry.parentPath, newName);
      conflict(dest);
      for (const e of [...entries.values()].filter((x) => isSameOrDescendant(x.path, entry.path))) {
        entries.delete(e.path);
        const np = dest + e.path.slice(entry.path.length);
        const name = np.slice(np.lastIndexOf('/') + 1);
        entries.set(np, { ...e, id: np, path: np, name, parentPath: parentPath(np), extension: e.kind === 'file' ? extensionOf(name) : undefined });
      }
    },
    async move(list, target) {
      await wait();
      list.forEach((e) => relocate(e, target, false));
    },
    async copy(list, target) {
      await wait();
      list.forEach((e) => relocate(e, target, true));
    },
    async delete(list) {
      await wait();
      for (const entry of list) for (const e of [...entries.values()]) if (isSameOrDescendant(e.path, entry.path)) entries.delete(e.path);
    },
    async upload(target, file, opts) {
      opts?.onProgress?.({ file, loaded: 0, total: file.size });
      await wait();
      if (opts?.signal?.aborted) throw new DOMException('Aborted', 'AbortError');
      const p = joinPath(target, file.name);
      entries.set(p, makeEntry(p, false, file.size, new Date().toISOString()));
      opts?.onProgress?.({ file, loaded: file.size, total: file.size });
    }
  };
}
