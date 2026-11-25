import type { ImportProgressEvent } from '@tfm/api-client';
import { Progress } from '@tfm/ui';
import { CheckCircle2, XCircle } from 'lucide-react';
import { t } from '@/i18n';

/** Progress bar + message of an import. */
export function ImportProgressView({ progress, started }: { progress: ImportProgressEvent | null; started: boolean }) {
  if (!started) return null;
  const failed = !!progress?.error;
  const done = !!progress?.done;
  return (
    <div className="flex flex-col gap-2 rounded-xl border border-border bg-surface-2/50 p-3" role="status" aria-live="polite">
      <div className="flex items-center gap-2 text-sm">
        {done && !failed && <CheckCircle2 className="size-4 text-success" />}
        {failed && <XCircle className="size-4 text-danger" />}
        <span className="min-w-0 flex-1 truncate font-medium text-fg">
          {failed ? t('Import failed') : done ? t('Import completed') : t('Importing...')}
        </span>
        {progress && <span className="tabular-nums text-xs text-fg-muted">{Math.round(progress.percent)}%</span>}
      </div>
      <Progress value={progress ? progress.percent : undefined} tone={failed ? 'danger' : done ? 'success' : 'accent'} aria-label={t('Import progress')} />
      <p className="truncate text-xs text-fg-muted">
        {progress?.error ??
          (progress?.total ? t('{completed} of {total} items', { completed: progress.completed ?? 0, total: progress.total }) : progress?.message ?? t('Starting...'))}
      </p>
    </div>
  );
}
