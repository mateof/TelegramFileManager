# @tfm/file-manager

A reusable React file manager: a **headless core** (pure modules + the `useFileManager` hook) and a
**default UI** built on `@tfm/ui`. It knows nothing about TFM: every data access goes through a
`FileSystemProvider` adapter. TFM ships three providers in the app (Telegram channel, server local
files, shared collection); `createMemoryProvider()` is a complete in-memory example.

Dependencies: `react` 19, `@tfm/ui`, `@tanstack/react-virtual`, `lucide-react`; peer
`@tanstack/react-query` 5 (a `QueryClientProvider` must be mounted above it).

## Quick start

```tsx
import { FileManager, createMemoryProvider } from '@tfm/file-manager';

const provider = createMemoryProvider({ files: [{ path: '/music/song.mp3', size: 4_000_000 }] });

export function Files() {
  return (
    <div className="h-[600px]">
      <FileManager provider={provider} onOpen={(entry) => (console.log('open', entry), true)} />
    </div>
  );
}
```

Controlled path (router): pass `path` + `onPathChange`; the internal back/forward history follows
external changes (browser back works).

## Writing a provider

Paths are absolute, `/` is the root, folders have no trailing slash. Implement `list` and whatever
your backend supports, and declare it in `capabilities`: the UI hides what is not supported.

```ts
import type { FileEntry, FileSystemProvider } from '@tfm/file-manager';
import { joinPath, mediaKindOf } from '@tfm/file-manager';

interface Dto { id: string; name: string; dir: boolean; bytes: number; mtime: string }

const toEntry = (parent: string) => (d: Dto): FileEntry => ({
  id: d.id,
  name: d.name,
  kind: d.dir ? 'folder' : 'file',
  path: joinPath(parent, d.name),
  parentPath: parent,
  size: d.dir ? undefined : d.bytes,
  modifiedAt: d.mtime,
  mediaKind: d.dir ? undefined : mediaKindOf(d.name),
  data: d
});

export function createRestProvider(base: string): FileSystemProvider {
  return {
    id: `rest:${base}`,
    rootName: 'My files',
    capabilities: {
      rename: true, move: true, copy: false, delete: true, createFolder: true, upload: true,
      download: true, search: false, preview: true, thumbnails: false,
      paging: true, sortFields: ['name', 'size', 'modifiedAt'], serverFilter: false
    },
    async list(path, { page = 1, pageSize = 100, sort, signal } = {}) {
      const q = new URLSearchParams({ path, page: String(page), size: String(pageSize), sort: sort?.field ?? 'name', desc: String(sort?.direction === 'desc') });
      const res = await fetch(`${base}/list?${q}`, { signal });
      const body = (await res.json()) as { items: Dto[]; total: number };
      return { items: body.items.map(toEntry(path)), page, pageSize, totalItems: body.total, hasNext: page * pageSize < body.total };
    },
    async createFolder(parent, name) { await fetch(`${base}/folders`, { method: 'POST', body: JSON.stringify({ parent, name }) }); },
    async rename(entry, newName) { await fetch(`${base}/${entry.id}/name`, { method: 'PUT', body: newName }); },
    async move(entries, target) { await fetch(`${base}/move`, { method: 'POST', body: JSON.stringify({ ids: entries.map((e) => e.id), target }) }); },
    async delete(entries) { await fetch(`${base}/delete`, { method: 'POST', body: JSON.stringify(entries.map((e) => e.id)) }); },
    // One file per call: the upload queue handles concurrency, progress, cancel and retry
    async upload(target, file, { signal } = {}) {
      const form = new FormData();
      form.append('file', file);
      form.append('path', target);
      await fetch(`${base}/upload`, { method: 'POST', body: form, signal });
    },
    async download(entries) { entries.forEach((e) => window.open(`${base}/${e.id}/content?download=1`)); },
    previewUrl: (e) => (e.kind === 'file' ? `${base}/${e.id}/content` : undefined)
  };
}
```

Client side fallbacks: sort fields not listed in `sortFields` (or any sort when `paging` is false)
and the media-kind/text filters are applied to the loaded entries. Without `search` the search box
filters the current folder; with it, it searches the sub-tree (debounced).

## Custom actions (actions registry)

The equivalent of the `Can*` flags + events of the Blazor `MobileFileManager`:

```tsx
const actions: FileAction[] = [
  {
    id: 'share',
    label: 'Share',
    icon: <Share2 />,
    selection: 'single',                   // 'single' | 'multiple' | 'any' (default) | 'none' (current folder)
    placement: ['context', 'details', 'selection'],   // also 'toolbar'
    group: 'share', order: 1,              // menus are grouped/sorted; separators between groups
    shortcut: 'mod+shift+s',               // shown in menus
    when: ([e]) => e.kind === 'folder',    // visibility
    isEnabled: () => true,                 // shown but disabled when false
    run: ([e], ctx) => openShareDialog(e, ctx.currentPath)
  }
];
<FileManager provider={provider} actions={actions} />;
```

Built-in actions use the ids `fm:open`, `fm:download`, `fm:cut`, `fm:copy`, `fm:paste`,
`fm:paste-into`, `fm:rename`, `fm:details`, `fm:delete`, `fm:new-folder`, `fm:upload`,
`fm:select-all`, `fm:refresh`; hide some with `hiddenActions`.

## What the default UI does

- Toolbar: back/forward/up, breadcrumb (drop target), search, filter by kind, sort, view mode
  (details / list / grid), new folder, upload, refresh, folder tree and details panel toggles.
- Virtualized views with infinite scroll, thumbnails, inline rename, cut items dimmed.
- Selection: click, Ctrl/Cmd toggle, Shift range, checkboxes, select all; touch: tap opens, long
  press starts the selection mode; a selection bar (bottom bar on mobile) with the actions.
- Context menu on items and on the background; per-item "more" menu.
- Drag & drop: move into folders, breadcrumb hops or tree nodes (Ctrl/Alt = copy); drop OS files
  to upload (into the folder under the pointer or the current one).
- Upload queue panel (progress, cancel, retry, clear finished).
- Keyboard: arrows (grid aware), Home/End, PageUp/PageDown, Shift extends, Enter opens, Backspace
  or Alt+Up goes up, Alt+Left/Right history, F2 rename, Delete, Ctrl+A/C/X/V, Ctrl+F search,
  Ctrl+Shift+N new folder, F5 refresh, Esc, Space toggles, type-ahead.
- Labels: English by default, pass `strings` (`FileManagerStrings`) to translate.

## Headless use

`useFileManager(options)` returns the whole controller (`path`, `entries`, `selection`,
`clipboard`, `rename`, `uploads`, `handleKeyDown`, `createFolder`, `deleteEntries`, `moveEntries`...)
and `<FileManagerView fm={controller} />` renders the default UI around it. The pure modules
(`selectionReducer`, history helpers, `pasteBlockReason`, `resolveKey`, `UploadQueue`,
`sortEntries`/`filterEntries`, `resolveActions`) have no React dependency.

Listings are cached under `fileManagerQueryKey(provider.id)`: invalidate it to refresh from outside.
