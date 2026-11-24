import { t } from '@/i18n';
import { formatBytes } from '@/lib/format';
import { waitSecondsLeft, type TrackedDelete } from './deleteStore';

/** "Deleting 3 items from Música", "Deleted "a.mp3" from Música", "Delete cancelled"... */
export function deleteTitle(op: TrackedDelete): string {
  const place = op.placeName;
  const one = op.itemCount === 1 && op.firstName;
  switch (op.status) {
    case 'done':
      return one
        ? t('Deleted "{name}" from {place}', { name: op.firstName!, place })
        : t('Deleted {count} items from {place}', { count: op.itemCount, place });
    case 'partial':
      return t('Deleted with errors');
    case 'failed':
      return t('Delete failed');
    case 'cancelled':
      return t('Delete cancelled');
    default:
      return one
        ? t('Deleting "{name}" from {place}', { name: op.firstName!, place })
        : t('Deleting {count} items from {place}', { count: op.itemCount, place });
  }
}

/** What the delete is doing right now (phase text). */
export function deletePhaseText(op: TrackedDelete, now = Date.now()): string {
  if (op.cancelling) return t('Cancelling...');
  if (op.status === 'starting') return t('Starting...');
  if (op.mode === 'sync') return t('Deleting...');
  const p = op.progress;
  if (!p || p.phase === 'counting') return t('Counting...');
  const wait = waitSecondsLeft(p, now);
  if (wait !== null) return t('Waiting for Telegram (flood wait) {seconds} s', { seconds: wait });
  return t('Deleting...');
}

/** "12 of 40 files · 1.2 GB of 3.4 GB" once counted. */
export function deleteCountsText(op: TrackedDelete): string | null {
  const p = op.progress;
  if (!p?.counted) return null;
  const parts: string[] = [];
  if (p.totalFiles > 0) parts.push(t('{done} of {total} files', { done: p.processedFiles, total: p.totalFiles }));
  else if (p.totalFolders > 0) parts.push(t('{done} of {total} folders', { done: p.processedFolders, total: p.totalFolders }));
  if (p.totalBytes > 0) parts.push(`${formatBytes(p.processedBytes)} / ${formatBytes(p.totalBytes)}`);
  return parts.join(' · ') || null;
}
