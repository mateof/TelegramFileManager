import type { FileEntry, ListFilter, SortSpec } from '../types';
import { entryMediaKind } from './media';

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

function time(iso: string | undefined): number {
  if (!iso) return 0;
  const t = Date.parse(iso);
  return Number.isNaN(t) ? 0 : t;
}

/** Compares two entries by a sort spec. Folders always come first. */
export function compareEntries(a: FileEntry, b: FileEntry, sort: SortSpec): number {
  if (a.kind !== b.kind) return a.kind === 'folder' ? -1 : 1;
  let r = 0;
  switch (sort.field) {
    case 'size':
      r = (a.size ?? 0) - (b.size ?? 0);
      break;
    case 'modifiedAt':
      r = time(a.modifiedAt) - time(b.modifiedAt);
      break;
    case 'createdAt':
      r = time(a.createdAt) - time(b.createdAt);
      break;
    case 'type':
      r = collator.compare(a.extension ?? '', b.extension ?? '');
      break;
    default:
      r = 0;
  }
  if (r === 0) r = collator.compare(a.name, b.name);
  return sort.direction === 'desc' ? -r : r;
}

/** Sorted copy (stable). */
export function sortEntries(entries: readonly FileEntry[], sort: SortSpec): FileEntry[] {
  return [...entries].sort((a, b) => compareEntries(a, b, sort));
}

/** Client side filter. Folders pass the media kind filter (so you can keep navigating). */
export function filterEntries(entries: readonly FileEntry[], filter: ListFilter | undefined): FileEntry[] {
  if (!filter) return [...entries];
  const text = filter.text?.trim().toLocaleLowerCase();
  const kinds = filter.mediaKinds?.length ? new Set(filter.mediaKinds) : null;
  return entries.filter((e) => {
    if (filter.kind && e.kind !== filter.kind) return false;
    if (text && !e.name.toLocaleLowerCase().includes(text)) return false;
    if (kinds && e.kind === 'file' && !kinds.has(entryMediaKind(e))) return false;
    return true;
  });
}
