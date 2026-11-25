import { useVirtualizer } from '@tanstack/react-virtual';
import { Button, cn, ContextMenu, ContextMenuContent, ContextMenuTrigger, EmptyState, Skeleton, Spinner } from '@tfm/ui';
import { AlertTriangle, ArrowDown, ArrowUp, FolderOpen, SearchX } from 'lucide-react';
import { useEffect, useLayoutEffect, useRef, useState, type DragEvent, type MouseEvent, type ReactNode } from 'react';
import type { FileEntry, SortField } from '../types';
import { ActionMenuItems } from './ActionMenu';
import { DRAG_MIME, useFileManagerUi } from './context';
import { DetailsRow, GridCard, ListRow } from './EntryItems';
import { entryDomId } from './useEntryInteractions';

const ROW_HEIGHT = { details: 44, list: 58 } as const;
const GRID_MIN = 148;
const GRID_GAP = 10;
const MOBILE_GRID_MIN = 108;

function hasFiles(e: DragEvent) {
  return Array.from(e.dataTransfer.types).includes('Files');
}

function SortHeader({ field, label, className }: { field: SortField; label: string; className?: string }) {
  const { fm } = useFileManagerUi();
  const active = fm.sort.field === field;
  return (
    <button
      type="button"
      onClick={() => fm.toggleSort(field)}
      aria-sort={active ? (fm.sort.direction === 'asc' ? 'ascending' : 'descending') : undefined}
      className={cn(
        'inline-flex items-center gap-1 rounded px-1 text-xs font-medium transition-colors hover:text-fg',
        active ? 'text-fg' : 'text-fg-subtle',
        className
      )}
    >
      {label}
      {active && (fm.sort.direction === 'asc' ? <ArrowUp className="size-3" /> : <ArrowDown className="size-3" />)}
    </button>
  );
}

function DetailsHeader() {
  const { strings } = useFileManagerUi();
  return (
    <div role="presentation" className="flex h-9 shrink-0 items-center gap-3 border-b border-border px-2.5 pl-12">
      <SortHeader field="name" label={strings.name} className="flex-1 justify-start" />
      <SortHeader field="size" label={strings.size} className="hidden w-24 justify-end md:inline-flex" />
      <SortHeader field="modifiedAt" label={strings.modified} className="hidden w-40 lg:inline-flex" />
      <SortHeader field="type" label={strings.type} className="hidden w-16 xl:inline-flex" />
      <span className="w-7" />
    </div>
  );
}

function LoadingRows({ grid }: { grid: boolean }) {
  if (grid) {
    return (
      <div className="grid grid-cols-[repeat(auto-fill,minmax(130px,1fr))] gap-3 p-3">
        {Array.from({ length: 12 }, (_, i) => (
          <div key={i} className="flex flex-col gap-2">
            <Skeleton className="aspect-square w-full rounded-xl" />
            <Skeleton className="h-3.5 w-3/4" />
          </div>
        ))}
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-1 p-2">
      {Array.from({ length: 10 }, (_, i) => (
        <div key={i} className="flex h-11 items-center gap-3 px-2.5">
          <Skeleton className="size-7 rounded-lg" />
          <Skeleton className="h-3.5 flex-1" style={{ maxWidth: `${40 + ((i * 37) % 45)}%` }} />
          <Skeleton className="hidden h-3 w-16 md:block" />
        </div>
      ))}
    </div>
  );
}

/** Empty / error / no-match states of the item area. */
function StateMessage() {
  const { fm, strings } = useFileManagerUi();
  if (fm.error) {
    const message = fm.error instanceof Error ? fm.error.message : undefined;
    return (
      <EmptyState
        icon={<AlertTriangle />}
        title={strings.loadError}
        description={message}
        action={
          <Button variant="secondary" size="sm" onClick={fm.refresh}>
            {strings.retry}
          </Button>
        }
      />
    );
  }
  const filtered = fm.isSearching || !!fm.filter.mediaKinds?.length || !!fm.filter.kind;
  return filtered ? (
    <EmptyState icon={<SearchX />} title={strings.noMatches} description={strings.noMatchesHint} />
  ) : (
    <EmptyState icon={<FolderOpen />} title={strings.emptyFolder} description={strings.emptyFolderHint} />
  );
}

/** Virtualized items of the current view mode + keyboard, background menu, drop of OS files. */
export function ItemsView({ footer }: { footer?: ReactNode }) {
  const ui = useFileManagerUi();
  const { fm, strings, isMobile } = ui;
  const scrollRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const [fileDrag, setFileDrag] = useState(false);
  const [menuEntries, setMenuEntries] = useState<FileEntry[]>([]);
  const mode = fm.viewMode;
  const grid = mode === 'grid';
  const entries = fm.entries;
  const showLocation = fm.isSearching && !!fm.provider.search;

  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setWidth(el.clientWidth));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const minCard = isMobile ? MOBILE_GRID_MIN : GRID_MIN;
  const innerWidth = Math.max(0, width - 24);
  const columns = grid ? Math.max(1, Math.floor((innerWidth + GRID_GAP) / (minCard + GRID_GAP))) : 1;
  const cardWidth = grid ? (innerWidth - GRID_GAP * (columns - 1)) / columns : 0;
  const rowCount = grid ? Math.ceil(entries.length / columns) : entries.length;
  const rowHeight = grid ? Math.round(cardWidth + 64 + GRID_GAP) : ROW_HEIGHT[mode === 'list' ? 'list' : 'details'];

  const { setColumns } = fm;
  useEffect(() => {
    setColumns(columns);
  }, [columns, setColumns]);

  // eslint-disable-next-line react-hooks/incompatible-library -- TanStack Virtual returns fresh functions each render by design
  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => rowHeight,
    overscan: grid ? 3 : 10,
    // Until the first measure (and in tests/SSR) assume a common viewport
    initialRect: { width: 800, height: 600 },
    paddingStart: 8,
    paddingEnd: 8
  });

  useEffect(() => {
    virtualizer.measure();
  }, [rowHeight, virtualizer]);

  const items = virtualizer.getVirtualItems();
  const lastIndex = items.length ? items[items.length - 1].index : -1;
  const { hasNextPage, fetchNextPage, isFetchingNextPage } = fm;
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && lastIndex >= rowCount - 6) fetchNextPage();
  }, [lastIndex, rowCount, hasNextPage, isFetchingNextPage, fetchNextPage]);

  // Keep the keyboard cursor visible
  const focusId = fm.selection.focusId;
  useEffect(() => {
    if (!focusId) return;
    const index = entries.findIndex((e) => e.id === focusId);
    if (index >= 0) virtualizer.scrollToIndex(grid ? Math.floor(index / columns) : index, { align: 'auto' });
  }, [focusId, entries, grid, columns, virtualizer]);

  const onContextMenu = (e: MouseEvent) => {
    const id = (e.target as HTMLElement).closest('[data-entry-id]')?.getAttribute('data-entry-id');
    const entry = id ? entries.find((x) => x.id === id) : undefined;
    if (!entry) {
      fm.selection.dispatch({ type: 'clear' });
      setMenuEntries([]);
    } else {
      setMenuEntries(fm.selection.isSelected(entry.id) ? fm.selection.entries : [entry]);
    }
  };

  const canUpload = fm.provider.capabilities.upload && !!fm.provider.upload;
  const dropHandlers = {
    onDragOver: (e: DragEvent) => {
      if (canUpload && hasFiles(e)) {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        if (!fileDrag) setFileDrag(true);
      } else if (Array.from(e.dataTransfer.types).includes(DRAG_MIME)) {
        // Internal drags over the background: nothing to do (dropping on the same folder is a no-op)
      }
    },
    onDragLeave: (e: DragEvent) => {
      if (!e.currentTarget.contains(e.relatedTarget as Node)) setFileDrag(false);
    },
    onDrop: (e: DragEvent) => {
      setFileDrag(false);
      const files = Array.from(e.dataTransfer.files ?? []);
      if (canUpload && files.length) {
        e.preventDefault();
        fm.uploads.add(files);
      }
    }
  };

  const showState = !fm.isLoading && entries.length === 0;
  const activeDescendant = focusId ? entryDomId(fm.provider.id, focusId) : undefined;

  return (
    <div className="relative flex min-h-0 flex-1 flex-col" {...dropHandlers}>
      {mode === 'details' && !isMobile && entries.length > 0 && <DetailsHeader />}
      <ContextMenu modal={false}>
        <ContextMenuTrigger asChild disabled={isMobile}>
          <div
            ref={scrollRef}
            role="listbox"
            aria-multiselectable
            aria-label={strings.items.replace('{count}', String(fm.totalItems))}
            aria-activedescendant={activeDescendant}
            aria-busy={fm.isLoading || undefined}
            tabIndex={0}
            onKeyDown={fm.handleKeyDown}
            onContextMenu={onContextMenu}
            onClick={(e) => {
              if (e.target === e.currentTarget || (e.target as HTMLElement).dataset.fmBackground !== undefined) {
                fm.selection.dispatch({ type: 'clear' });
                fm.selection.setSelectionMode(false);
              }
            }}
            className="relative min-h-0 flex-1 overflow-y-auto overflow-x-hidden overscroll-contain px-1.5 outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring/40 sm:px-3"
          >
            {fm.isLoading ? (
              <LoadingRows grid={grid} />
            ) : showState ? (
              <div data-fm-background className="flex h-full items-center justify-center">
                <StateMessage />
              </div>
            ) : (
              <div data-fm-background style={{ height: virtualizer.getTotalSize(), position: 'relative', width: '100%' }}>
                {items.map((row) => {
                  const top = row.start;
                  if (!grid) {
                    const entry = entries[row.index];
                    if (!entry) return null;
                    const Row = mode === 'list' ? ListRow : DetailsRow;
                    return (
                      <Row
                        key={entry.id}
                        entry={entry}
                        showLocation={showLocation}
                        style={{ position: 'absolute', top: 0, left: 0, right: 0, height: rowHeight - 2, transform: `translateY(${top}px)` }}
                      />
                    );
                  }
                  const slice = entries.slice(row.index * columns, row.index * columns + columns);
                  return (
                    <div
                      key={row.key}
                      data-fm-background
                      className="absolute inset-x-0 flex"
                      style={{ top: 0, transform: `translateY(${top}px)`, gap: GRID_GAP, height: rowHeight - GRID_GAP }}
                    >
                      {slice.map((entry) => (
                        <GridCard key={entry.id} entry={entry} style={{ width: cardWidth, flex: 'none' }} />
                      ))}
                    </div>
                  );
                })}
              </div>
            )}
            {isFetchingNextPage && (
              <div className="flex items-center justify-center gap-2 py-3 text-xs text-fg-muted">
                <Spinner size="sm" /> {strings.loadingMore}
              </div>
            )}
            {footer}
          </div>
        </ContextMenuTrigger>
        <ContextMenuContent className="min-w-56">
          <ActionMenuItems menu="context" entries={menuEntries} />
        </ContextMenuContent>
      </ContextMenu>

      {fileDrag && (
        <div className="pointer-events-none absolute inset-2 z-20 flex flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-accent-400/80 bg-primary-soft/80 backdrop-blur-sm animate-fade-in">
          <span className="text-base font-semibold text-fg">{strings.dropToUpload}</span>
          <span className="text-sm text-fg-muted">{strings.dropToUploadHint}</span>
        </div>
      )}
    </div>
  );
}
