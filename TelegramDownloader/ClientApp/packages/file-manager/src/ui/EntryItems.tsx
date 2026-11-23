import { Checkbox, cn, DropdownMenu, DropdownMenuContent, DropdownMenuTrigger, IconButton } from '@tfm/ui';
import { MoreVertical } from 'lucide-react';
import type { CSSProperties } from 'react';
import { entryMediaKind } from '../core/media';
import type { FileEntry } from '../types';
import { ActionMenuItems } from './ActionMenu';
import { useFileManagerUi } from './context';
import { EntryThumb } from './FileIcon';
import { formatBytes, formatDate } from './format';
import { fmt } from './strings';
import { RenameField } from './RenameField';
import { useEntryInteractions } from './useEntryInteractions';

/** Shared look of a selectable item (selection, focus, cut, drop target). */
function useItemState(entry: FileEntry) {
  const { fm, dropTargetId } = useFileManagerUi();
  return {
    selected: fm.selection.isSelected(entry.id),
    focused: fm.selection.focusId === entry.id,
    cut: fm.clipboard.isCut(entry.id),
    dropTarget: dropTargetId === entry.id,
    editing: fm.rename.editingId === entry.id
  };
}

function itemClasses(s: ReturnType<typeof useItemState>) {
  return cn(
    'group relative cursor-default select-none outline-none transition-[background-color,box-shadow,opacity] duration-150',
    s.selected ? 'bg-primary-soft text-fg' : 'hover:bg-surface-2/70',
    s.focused && 'ring-2 ring-inset ring-ring/70',
    s.cut && 'opacity-50',
    s.dropTarget && 'bg-primary-soft ring-2 ring-inset ring-accent-400'
  );
}

/** "More" button of an item (always visible on touch, on hover with a mouse). */
export function EntryMoreButton({ entry, className }: { entry: FileEntry; className?: string }) {
  const { fm, strings, isMobile } = useFileManagerUi();
  const targets = fm.selection.isSelected(entry.id) && fm.selection.entries.length > 1 ? fm.selection.entries : [entry];
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <IconButton
          label={strings.actions}
          size="xs"
          className={cn(!isMobile && 'opacity-0 focus-visible:opacity-100 group-hover:opacity-100 data-[state=open]:opacity-100', className)}
          onClick={(e) => e.stopPropagation()}
          onDoubleClick={(e) => e.stopPropagation()}
          onPointerDown={(e) => e.stopPropagation()}
        >
          <MoreVertical />
        </IconButton>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-52" onCloseAutoFocus={(e) => e.preventDefault()}>
        <ActionMenuItems menu="dropdown" entries={targets} />
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function SelectBox({ entry, visible }: { entry: FileEntry; visible: boolean }) {
  const { fm } = useFileManagerUi();
  const selected = fm.selection.isSelected(entry.id);
  return (
    <span
      className={cn('flex shrink-0 items-center', !visible && !selected && 'hidden group-hover:flex')}
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <Checkbox
        checked={selected}
        tabIndex={-1}
        aria-label={entry.name}
        onCheckedChange={() => fm.selection.dispatch({ type: 'toggle', id: entry.id })}
      />
    </span>
  );
}

function EntryName({ entry, showLocation }: { entry: FileEntry; showLocation?: boolean }) {
  const s = useItemState(entry);
  if (s.editing) return <RenameField entry={entry} />;
  return (
    <span className="flex min-w-0 flex-col">
      <span className="truncate text-sm font-medium text-fg" title={entry.name}>
        {entry.name}
      </span>
      {showLocation && <span className="truncate text-xs text-fg-subtle">{entry.parentPath}</span>}
    </span>
  );
}

function metaLine(entry: FileEntry, strings: ReturnType<typeof useFileManagerUi>['strings'], locale?: string) {
  if (entry.kind === 'folder') {
    return entry.childCount !== undefined ? (entry.childCount === 1 ? strings.items_one : fmt(strings.items, { count: entry.childCount })) : strings.folder;
  }
  return [formatBytes(entry.size), formatDate(entry.modifiedAt, locale)].filter(Boolean).join(' · ');
}

export interface RowProps {
  entry: FileEntry;
  style?: CSSProperties;
  showLocation?: boolean;
}

/** Row of the details view (columns on md+, stacked on small screens). */
export function DetailsRow({ entry, style, showLocation }: RowProps) {
  const ui = useFileManagerUi();
  const { fm, strings, locale, isMobile } = ui;
  const s = useItemState(entry);
  const props = useEntryInteractions(entry);
  const thumb = ui.thumbnails ? fm.provider.thumbnailUrl?.(entry) : undefined;
  return (
    <div {...props} style={style} className={cn(itemClasses(s), 'flex items-center gap-3 rounded-lg px-2.5')}>
      <SelectBox entry={entry} visible={isMobile ? fm.selection.selectionMode : fm.selection.entries.length > 1} />
      <EntryThumb entry={entry} url={thumb} size="sm" />
      <div className="flex min-w-0 flex-1 flex-col">
        <EntryName entry={entry} showLocation={showLocation} />
        <span className="truncate text-xs text-fg-subtle md:hidden">{metaLine(entry, strings, locale)}</span>
      </div>
      <span className="hidden w-24 shrink-0 text-right text-xs tabular-nums text-fg-muted md:block">
        {entry.kind === 'file' ? formatBytes(entry.size) : ''}
      </span>
      <span className="hidden w-40 shrink-0 truncate text-xs text-fg-muted lg:block">{formatDate(entry.modifiedAt, locale)}</span>
      <span className="hidden w-16 shrink-0 truncate text-xs uppercase text-fg-subtle xl:block">
        {entry.kind === 'folder' ? '' : entry.extension ?? ''}
      </span>
      <EntryMoreButton entry={entry} />
    </div>
  );
}

/** Compact row of the list view. */
export function ListRow({ entry, style, showLocation }: RowProps) {
  const ui = useFileManagerUi();
  const { fm, strings, locale, isMobile } = ui;
  const s = useItemState(entry);
  const props = useEntryInteractions(entry);
  const thumb = ui.thumbnails ? fm.provider.thumbnailUrl?.(entry) : undefined;
  return (
    <div {...props} style={style} className={cn(itemClasses(s), 'flex items-center gap-3 rounded-xl px-2.5')}>
      <SelectBox entry={entry} visible={isMobile ? fm.selection.selectionMode : fm.selection.entries.length > 1} />
      <EntryThumb entry={entry} url={thumb} size="md" />
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <EntryName entry={entry} showLocation={showLocation} />
        <span className="truncate text-xs text-fg-subtle">{metaLine(entry, strings, locale)}</span>
      </div>
      <EntryMoreButton entry={entry} />
    </div>
  );
}

/** Card of the grid view. */
export function GridCard({ entry, style }: RowProps) {
  const ui = useFileManagerUi();
  const { fm, strings, locale, isMobile } = ui;
  const s = useItemState(entry);
  const props = useEntryInteractions(entry);
  const thumb = fm.provider.thumbnailUrl?.(entry);
  const kind = entryMediaKind(entry);
  return (
    <div
      {...props}
      style={style}
      className={cn(
        itemClasses(s),
        'flex flex-col gap-2 rounded-2xl border p-2',
        s.selected ? 'border-accent-500/40' : 'border-transparent hover:border-border'
      )}
    >
      <div className="relative flex aspect-square w-full items-center justify-center overflow-hidden rounded-xl bg-surface-2/60">
        {thumb ? (
          <EntryThumb entry={entry} url={thumb} size="xl" className="size-full rounded-none" />
        ) : (
          <EntryThumb entry={entry} size="xl" />
        )}
        {kind === 'video' && thumb && (
          <span className="absolute bottom-1.5 right-1.5 rounded-md bg-black/60 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-white">
            {entry.extension}
          </span>
        )}
        <span className="absolute left-1.5 top-1.5">
          <SelectBox entry={entry} visible={isMobile ? fm.selection.selectionMode : fm.selection.entries.length > 1} />
        </span>
        <span className="absolute right-1 top-1">
          <EntryMoreButton entry={entry} className="bg-surface/80 backdrop-blur" />
        </span>
      </div>
      <div className="min-w-0 px-0.5">
        <EntryName entry={entry} />
        <span className="block truncate text-xs text-fg-subtle">{metaLine(entry, strings, locale)}</span>
      </div>
    </div>
  );
}
