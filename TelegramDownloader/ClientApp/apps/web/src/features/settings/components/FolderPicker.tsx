import { cn, Input, SegmentedControl, Skeleton } from '@tfm/ui';
import { useQuery } from '@tanstack/react-query';
import { ChevronRight, Folder, FolderOpen, List, ListTree, Search } from 'lucide-react';
import { useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { ancestorsOf, buildFolderTree, filterFolderTree, type FolderNode } from '../model/folderTree';
import { settingsKeys } from '../model/useSettingsForm';

type View = 'tree' | 'list';

const VIEW_KEY = 'tfm.settings.folderPickerView';
/** Subfolders shown at once under a folder, and folders in the flat list. */
const PAGE = 200;

function savedView(): View {
  try {
    return localStorage.getItem(VIEW_KEY) === 'list' ? 'list' : 'tree';
  } catch {
    return 'tree';
  }
}

type Row = { kind: 'folder'; node: FolderNode; depth: number; open: boolean } | { kind: 'more'; parent: string; depth: number; rest: number };

/** The visible rows of the tree: the root, and the children of every open folder. */
function visibleRows(root: FolderNode, isOpen: (path: string) => boolean, limitOf: (path: string) => number): Row[] {
  const rows: Row[] = [{ kind: 'folder', node: root, depth: 0, open: true }];
  const walk = (node: FolderNode, depth: number) => {
    const limit = limitOf(node.path);
    for (const child of node.children.slice(0, limit)) {
      const open = child.children.length > 0 && isOpen(child.path);
      rows.push({ kind: 'folder', node: child, depth, open });
      if (open) walk(child, depth + 1);
    }
    if (node.children.length > limit) rows.push({ kind: 'more', parent: node.path, depth, rest: node.children.length - limit });
  };
  walk(root, 1);
  return rows;
}

function Highlight({ text, query }: { text: string; query: string }): ReactNode {
  const q = query.trim().toLowerCase();
  const at = q ? text.toLowerCase().indexOf(q) : -1;
  if (at < 0) return text;
  return (
    <>
      {text.slice(0, at)}
      <mark className="rounded-sm bg-accent-400/25 text-inherit">{text.slice(at, at + q.length)}</mark>
      {text.slice(at + q.length)}
    </>
  );
}

/**
 * Picks a folder of a channel index, browsing it as a tree (default) or as a flat list. The
 * search filters both: the tree keeps the matches in their place, with their subfolders.
 */
export function FolderPicker({ channelId, value, onChange }: { channelId: number; value: string; onChange: (path: string) => void }) {
  const [query, setQuery] = useState('');
  const [view, setView] = useState<View>(savedView);
  const folders = useQuery({
    queryKey: settingsKeys.channelFolders(channelId),
    queryFn: ({ signal }) => api.library.channelFolders(channelId, signal),
    staleTime: 60_000
  });

  const changeView = (next: View) => {
    setView(next);
    try {
      localStorage.setItem(VIEW_KEY, next);
    } catch {
      // private mode: the choice lasts until the page is closed
    }
  };

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <div className="flex min-w-0 items-center gap-2">
        <Input
          inputSize="sm"
          leading={<Search />}
          placeholder={t('Search folders...')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          aria-label={t('Search folders...')}
          className="min-w-0 flex-1"
        />
        <SegmentedControl<View>
          size="sm"
          value={view}
          onValueChange={changeView}
          aria-label={t('Folder view')}
          options={[
            { value: 'tree', label: <span className="sr-only">{t('Tree')}</span>, icon: <ListTree /> },
            { value: 'list', label: <span className="sr-only">{t('List')}</span>, icon: <List /> }
          ]}
        />
      </div>
      {folders.isPending ? (
        <Skeleton className="h-48" />
      ) : folders.isError ? (
        <p className="text-xs text-danger">{errorMessage(folders.error)}</p>
      ) : view === 'tree' ? (
        <FolderTreeView paths={folders.data} query={query} value={value} onChange={onChange} />
      ) : (
        <FolderFlatList paths={folders.data} query={query} value={value} onChange={onChange} />
      )}
      <p className="flex min-w-0 items-center gap-1.5 text-xs text-fg-muted">
        <span className="shrink-0">{t('Selected:')}</span>
        <span className="truncate font-mono text-fg" title={value}>
          {value === '/' ? t('/ (whole channel)') : value}
        </span>
      </p>
    </div>
  );
}

function FolderTreeView({ paths, query, value, onChange }: { paths: string[]; query: string; value: string; onChange: (path: string) => void }) {
  const tree = useMemo(() => buildFolderTree(paths), [paths]);
  const filtered = useMemo(() => filterFolderTree(tree, query), [tree, query]);
  const searching = query.trim() !== '';
  // Browsing: the folders the user opened, starting with the way to the chosen one.
  const [opened, setOpened] = useState<Set<string>>(() => new Set(ancestorsOf(value)));
  // Searching: the parents of the matches, until the user opens or closes one for this query.
  const [searchOpened, setSearchOpened] = useState<{ query: string; open: Set<string> } | null>(null);
  const [limits, setLimits] = useState<Record<string, number>>({});
  const listRef = useRef<HTMLUListElement>(null);

  const open = searching ? (searchOpened?.query === query ? searchOpened.open : filtered.open) : opened;
  const setOpen = (path: string, on: boolean) => {
    if (open.has(path) === on) return;
    const next = new Set(open);
    if (on) next.add(path);
    else next.delete(path);
    if (searching) setSearchOpened({ query, open: next });
    else setOpened(next);
  };
  const rows = useMemo(
    () => visibleRows(filtered.root, (p) => open.has(p), (p) => limits[p] ?? PAGE),
    [filtered.root, open, limits]
  );

  const focusRow = (index: number) => listRef.current?.querySelectorAll<HTMLElement>('[data-row]')[index]?.focus();
  const onKeyDown = (e: KeyboardEvent, index: number, row: Extract<Row, { kind: 'folder' }>) => {
    const hasChildren = row.node.children.length > 0 && row.depth > 0;
    if (e.key === 'ArrowDown') focusRow(index + 1);
    else if (e.key === 'ArrowUp') focusRow(index - 1);
    else if (e.key === 'ArrowRight' && hasChildren) {
      if (!row.open) setOpen(row.node.path, true);
      else focusRow(index + 1);
    } else if (e.key === 'ArrowLeft') {
      if (hasChildren && row.open) setOpen(row.node.path, false);
      else {
        const parent = ancestorsOf(row.node.path).at(-1) ?? '/';
        const at = rows.findIndex((r) => r.kind === 'folder' && r.node.path === parent);
        if (at >= 0 && row.depth > 0) focusRow(at);
      }
    } else return;
    e.preventDefault();
  };

  if (searching && filtered.matches === 0) return <p className="rounded-lg border border-border px-3 py-6 text-center text-xs text-fg-subtle">{t('No folder matches.')}</p>;
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <ul ref={listRef} role="tree" aria-label={t('Folder')} className="max-h-72 min-w-0 overflow-y-auto rounded-lg border border-border py-1">
        {rows.map((row, index) => {
          const pad = { paddingLeft: `${row.depth * 1}rem` };
          if (row.kind === 'more')
            return (
              <li key={`more:${row.parent}`} role="none" style={pad}>
                <button
                  type="button"
                  data-row
                  onClick={() => setLimits({ ...limits, [row.parent]: (limits[row.parent] ?? PAGE) + PAGE })}
                  className="ml-7 px-2 py-1 text-xs text-accent-400 hover:text-accent-300 focus-visible:outline-2 focus-visible:outline-ring"
                >
                  {t('Show {count} more', { count: Math.min(row.rest, PAGE) })}
                </button>
              </li>
            );
          const { node } = row;
          const selected = node.path === value;
          const hasChildren = node.children.length > 0 && row.depth > 0;
          const Icon = row.open && hasChildren ? FolderOpen : Folder;
          return (
            <li
              key={node.path}
              role="treeitem"
              aria-level={row.depth + 1}
              aria-selected={selected}
              aria-expanded={hasChildren ? row.open : undefined}
              className={cn('flex min-w-0 items-center pr-2', selected ? 'bg-primary-soft text-accent-300' : 'hover:bg-surface-2')}
              style={pad}
            >
              {hasChildren ? (
                <button
                  type="button"
                  tabIndex={-1}
                  onClick={() => setOpen(node.path, !row.open)}
                  aria-label={row.open ? t('Collapse') : t('Expand')}
                  className="grid size-7 shrink-0 place-items-center rounded text-fg-muted hover:text-fg"
                >
                  <ChevronRight className={cn('size-3.5 transition-transform duration-150', row.open && 'rotate-90')} />
                </button>
              ) : (
                <span className="size-7 shrink-0" />
              )}
              <button
                type="button"
                data-row
                onClick={() => onChange(node.path)}
                onDoubleClick={() => hasChildren && setOpen(node.path, !row.open)}
                onKeyDown={(e) => onKeyDown(e, index, row)}
                title={node.path}
                className="flex min-w-0 flex-1 items-center gap-2 rounded py-1.5 pr-1 text-left text-sm focus-visible:outline-2 focus-visible:outline-ring"
              >
                <Icon className={cn('size-4 shrink-0', selected ? 'text-accent-300' : 'text-fg-muted')} />
                <span className={cn('truncate', row.depth === 0 && 'font-medium')}>
                  {row.depth === 0 ? t('/ (whole channel)') : <Highlight text={node.name} query={query} />}
                </span>
                {hasChildren && <span className="ml-auto shrink-0 pl-2 text-[11px] tabular-nums text-fg-subtle">{node.children.length}</span>}
              </button>
            </li>
          );
        })}
      </ul>
      {searching && <p className="text-[11px] text-fg-subtle">{filtered.matches === 1 ? t('1 folder matches') : t('{count} folders match', { count: filtered.matches })}</p>}
    </div>
  );
}

function FolderFlatList({ paths, query, value, onChange }: { paths: string[]; query: string; value: string; onChange: (path: string) => void }) {
  const [limit, setLimit] = useState(PAGE);
  const matching = useMemo(() => {
    const all = ['/', ...paths.filter((f) => f !== '/')];
    const q = query.trim().toLowerCase();
    return q ? all.filter((f) => f.toLowerCase().includes(q)) : all;
  }, [paths, query]);

  if (matching.length === 0) return <p className="rounded-lg border border-border px-3 py-6 text-center text-xs text-fg-subtle">{t('No folder matches.')}</p>;
  return (
    <ul role="listbox" aria-label={t('Folder')} className="max-h-72 min-w-0 overflow-y-auto rounded-lg border border-border">
      {matching.slice(0, limit).map((f) => (
        <li key={f} role="option" aria-selected={f === value}>
          <button
            type="button"
            onClick={() => onChange(f)}
            title={f}
            className={cn('flex w-full min-w-0 items-center gap-2 px-3 py-1.5 text-left font-mono text-xs hover:bg-surface-2', f === value && 'bg-primary-soft text-accent-300')}
          >
            <Folder className="size-3.5 shrink-0 opacity-60" />
            <span className="truncate">{f === '/' ? t('/ (whole channel)') : <Highlight text={f} query={query} />}</span>
          </button>
        </li>
      ))}
      {matching.length > limit && (
        <li>
          <button type="button" onClick={() => setLimit(limit + PAGE)} className="w-full px-3 py-1.5 text-left text-xs text-accent-400 hover:text-accent-300">
            {t('Show {count} more', { count: Math.min(matching.length - limit, PAGE) })}
          </button>
        </li>
      )}
    </ul>
  );
}
