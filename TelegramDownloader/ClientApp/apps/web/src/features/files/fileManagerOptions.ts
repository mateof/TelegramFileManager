import type { FileEntry, FileOperation } from '@tfm/file-manager';
import { confirm, toast } from '@tfm/ui';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';

/** Asks before deleting (shared by every TFM file manager). */
export function confirmDelete(entries: FileEntry[], extra?: string): Promise<boolean> {
  const one = entries.length === 1 ? entries[0] : null;
  return confirm({
    title: one ? t('Delete "{name}"?', { name: one.name }) : t('Delete {count} items?', { count: entries.length }),
    description: [extra, t('This cannot be undone.')].filter(Boolean).join(' '),
    confirmLabel: t('Delete'),
    destructive: true
  });
}

const FAILURES: Record<FileOperation, () => string> = {
  list: () => t('Could not load this folder'),
  createFolder: () => t('Could not create the folder'),
  rename: () => t('Could not rename'),
  move: () => t('Could not move'),
  copy: () => t('Could not copy'),
  delete: () => t('Could not delete'),
  upload: () => t('Upload failed'),
  download: () => t('Could not download')
};

/** Toast for a failed file manager operation. */
export function reportError(error: unknown, operation: FileOperation): void {
  toast.error(FAILURES[operation](), { description: errorMessage(error) });
}

/** Toast for the operations worth confirming. */
export function reportSuccess(operation: FileOperation, entries: FileEntry[]): void {
  const count = entries.length;
  if (operation === 'move') toast.success(t('{count} items moved', { count }));
  else if (operation === 'copy') toast.success(t('{count} items copied', { count }));
  // A delete reports its own outcome in the delete progress panel
}

/** The locale of dates in the file manager. */
export function fmLocale(lang: string): string {
  return lang === 'es' ? 'es-ES' : 'en-GB';
}
