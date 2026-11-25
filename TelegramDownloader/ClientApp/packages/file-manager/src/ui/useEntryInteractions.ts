import { useRef, type DragEvent, type HTMLAttributes, type MouseEvent, type PointerEvent } from 'react';
import { isSameOrDescendant } from '../core/paths';
import type { FileEntry } from '../types';
import { DRAG_MIME, useFileManagerUi } from './context';

const LONG_PRESS_MS = 480;
const MOVE_TOLERANCE = 10;
/** Max time between the clicks of a double click that we tie to the entry of the first click. */
const DOUBLE_CLICK_GUARD_MS = 600;

/** DOM id of an entry element (aria-activedescendant). */
export function entryDomId(providerId: string, id: string): string {
  return `fm-${providerId}-${id}`.replace(/[^\w-]/g, '_');
}

function hasFiles(e: DragEvent) {
  return Array.from(e.dataTransfer.types).includes('Files');
}

/**
 * Pointer, touch, keyboard-free and drag & drop behaviour of one entry:
 * - mouse: click selects (Ctrl/Cmd toggles, Shift ranges), double click opens; with
 *   `singleClickFolders` a plain click opens a folder. A double click always acts on the entry of its
 *   first click, even if the list moved under the pointer (selection bar shown, folder opened);
 * - touch: tap opens, long press starts the selection mode, then taps toggle;
 * - drag: drags the selection; folders accept drops (move, Ctrl/Alt = copy) and OS files (upload).
 */
export type EntryInteractionProps = HTMLAttributes<HTMLElement> & { draggable: boolean; 'data-entry-id': string };

export function useEntryInteractions(entry: FileEntry): EntryInteractionProps {
  const ui = useFileManagerUi();
  const { fm } = ui;
  const pointerType = useRef<string>('mouse');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const start = useRef<{ x: number; y: number } | null>(null);
  const longPressed = useRef(false);
  const caps = fm.provider.capabilities;
  const canDrag = !ui.isMobile && ((caps.move && !!fm.provider.move) || (caps.copy && !!fm.provider.copy));
  const selected = fm.selection.isSelected(entry.id);

  const clearTimer = () => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
  };

  const acceptsInternal = () =>
    entry.kind === 'folder' &&
    !!ui.dragging &&
    ui.dragging.length > 0 &&
    !ui.dragging.some((d) => d.id === entry.id || (d.kind === 'folder' && isSameOrDescendant(entry.path, d.path))) &&
    !ui.dragging.every((d) => d.parentPath === entry.path);

  return {
    id: entryDomId(fm.provider.id, entry.id),
    role: 'option',
    'aria-selected': selected,
    'data-entry-id': entry.id,
    draggable: canDrag && fm.rename.editingId !== entry.id,
    onPointerDown: (e: PointerEvent) => {
      pointerType.current = e.pointerType;
      longPressed.current = false;
      if (e.pointerType !== 'touch') return;
      start.current = { x: e.clientX, y: e.clientY };
      clearTimer();
      timer.current = setTimeout(() => {
        longPressed.current = true;
        fm.selection.setSelectionMode(true);
        fm.selection.dispatch({ type: 'toggle', id: entry.id });
        navigator.vibrate?.(15);
      }, LONG_PRESS_MS);
    },
    onPointerMove: (e: PointerEvent) => {
      if (!start.current || !timer.current) return;
      if (Math.abs(e.clientX - start.current.x) > MOVE_TOLERANCE || Math.abs(e.clientY - start.current.y) > MOVE_TOLERANCE) clearTimer();
    },
    onPointerUp: clearTimer,
    onPointerCancel: clearTimer,
    onClick: (e: MouseEvent) => {
      if (longPressed.current) {
        longPressed.current = false;
        e.preventDefault();
        return;
      }
      if (pointerType.current === 'touch' || ui.isMobile) {
        if (fm.selection.selectionMode) fm.selection.dispatch({ type: 'toggle', id: entry.id });
        else fm.open(entry);
        return;
      }
      const ctrl = e.ctrlKey || e.metaKey;
      if (e.detail > 1) {
        // Rest of a double click that landed elsewhere (or after opening a folder): ignore it
        const first = ui.recentClick(DOUBLE_CLICK_GUARD_MS);
        if (first && (first.opened || first.entry.id !== entry.id)) return;
      }
      if (ui.singleClickFolders && entry.kind === 'folder' && !ctrl && !e.shiftKey && !e.altKey) {
        ui.rememberClick(entry, true);
        fm.open(entry);
        return;
      }
      if (e.detail <= 1) ui.rememberClick(entry, false);
      fm.selection.dispatch({ type: 'click', id: entry.id, ctrl, shift: e.shiftKey });
    },
    onDoubleClick: () => {
      if (pointerType.current === 'touch' || ui.isMobile) return;
      const first = ui.recentClick(DOUBLE_CLICK_GUARD_MS);
      if (first?.opened) return;
      fm.open(first?.entry ?? entry);
    },
    onContextMenu: (e: MouseEvent) => {
      if (ui.isMobile) {
        e.preventDefault();
        return;
      }
      if (!selected) fm.selection.dispatch({ type: 'click', id: entry.id });
    },
    onDragStart: (e: DragEvent) => {
      const list = selected ? fm.selection.entries : [entry];
      if (!selected) fm.selection.dispatch({ type: 'click', id: entry.id });
      ui.setDragging(list);
      e.dataTransfer.effectAllowed = 'copyMove';
      e.dataTransfer.setData(DRAG_MIME, JSON.stringify({ providerId: fm.provider.id, ids: list.map((x) => x.id) }));
      e.dataTransfer.setData('text/plain', list.map((x) => x.name).join('\n'));
    },
    onDragEnd: () => {
      ui.setDragging(null);
      ui.setDropTargetId(null);
    },
    onDragOver: (e: DragEvent) => {
      if (entry.kind !== 'folder') return;
      const files = hasFiles(e) && caps.upload;
      if (!files && !acceptsInternal()) return;
      e.preventDefault();
      e.stopPropagation();
      e.dataTransfer.dropEffect = files || e.ctrlKey || e.altKey ? 'copy' : 'move';
      if (ui.dropTargetId !== entry.id) ui.setDropTargetId(entry.id);
    },
    onDragLeave: (e: DragEvent) => {
      if (ui.dropTargetId === entry.id && !e.currentTarget.contains(e.relatedTarget as Node)) ui.setDropTargetId(null);
    },
    onDrop: (e: DragEvent) => {
      if (entry.kind !== 'folder') return;
      const files = Array.from(e.dataTransfer.files ?? []);
      if (files.length && caps.upload) {
        e.preventDefault();
        e.stopPropagation();
        ui.setDropTargetId(null);
        fm.uploads.add(files, entry.path);
        return;
      }
      if (!acceptsInternal() || !ui.dragging) return;
      e.preventDefault();
      e.stopPropagation();
      const list = ui.dragging;
      ui.setDropTargetId(null);
      ui.setDragging(null);
      const copy = (e.ctrlKey || e.altKey) && caps.copy;
      void fm.moveEntries(list, entry.path, copy ? 'copy' : 'cut');
    }
  };
}
