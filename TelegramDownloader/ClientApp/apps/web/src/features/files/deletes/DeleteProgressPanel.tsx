import { Button, cn, IconButton, Progress } from '@tfm/ui';
import { AlertTriangle, Ban, CheckCircle2, ChevronDown, Info, Trash2, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { t } from '@/i18n';
import { isActiveDelete, percentOf, useDeletes, type TrackedDelete } from './deleteStore';
import { deleteCountsText, deletePhaseText, deleteTitle } from './deleteText';
import { cancelDelete, dismissDelete } from './deleteTracker';

/** The current time, refreshed every second while a FLOOD_WAIT countdown is shown. */
function useTicker(active: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    const tick = () => setNow(Date.now());
    const first = setTimeout(tick, 0); // the countdown starts from the real time, not the mount time
    const id = setInterval(tick, 1000);
    return () => {
      clearTimeout(first);
      clearInterval(id);
    };
  }, [active]);
  return now;
}

const TONES = {
  starting: 'accent',
  running: 'accent',
  done: 'success',
  partial: 'warning',
  failed: 'danger',
  cancelled: 'warning'
} as const;

function StatusIcon({ op }: { op: TrackedDelete }) {
  const cls = 'mt-0.5 size-4.5 shrink-0';
  if (op.status === 'done') return <CheckCircle2 className={cn(cls, 'text-success')} />;
  if (op.status === 'partial' || op.status === 'failed') return <AlertTriangle className={cn(cls, op.status === 'failed' ? 'text-danger' : 'text-warning')} />;
  if (op.status === 'cancelled') return <Ban className={cn(cls, 'text-warning')} />;
  return <Trash2 className={cn(cls, 'text-accent-400')} />;
}

export function DeleteCard({ op, now }: { op: TrackedDelete; now: number }) {
  const [open, setOpen] = useState(false);
  const active = isActiveDelete(op);
  const percent = op.mode === 'sync' && active ? undefined : percentOf(op);
  const counts = deleteCountsText(op);
  const more = op.failedCount - op.failures.length;

  return (
    <div
      role="status"
      aria-live="polite"
      data-testid="delete-card"
      data-status={op.status}
      className="pointer-events-auto w-full animate-slide-in-left rounded-xl border border-border-strong/50 bg-surface p-3 shadow-elevated"
    >
      <div className="flex items-start gap-2.5">
        <StatusIcon op={op} />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium" title={deleteTitle(op)}>
            {deleteTitle(op)}
          </p>
          {active && <p className="text-xs text-fg-muted">{deletePhaseText(op, now)}</p>}
          {counts && <p className="text-xs tabular-nums text-fg-muted">{counts}</p>}
        </div>
        {!active && <IconButton label={t('Close')} size="xs" onClick={() => dismissDelete(op.key)}><X /></IconButton>}
      </div>

      <Progress className="mt-2.5" size="xs" value={percent} tone={TONES[op.status]} aria-label={t('Delete progress')} />

      {active && op.progress?.currentItem && (
        <p className="mt-1.5 truncate text-xs text-fg-subtle" title={op.progress.currentItem}>
          {op.progress.currentItem}
        </p>
      )}
      {op.progress?.indexOnly && (
        <p className="mt-1.5 flex items-start gap-1.5 text-xs text-fg-muted">
          <Info className="mt-px size-3.5 shrink-0" />
          {t('Only the index is cleaned: this account cannot delete messages in this channel.')}
        </p>
      )}
      {op.status === 'failed' && op.error && <p className="mt-1.5 text-xs text-danger">{op.error}</p>}

      <div className="mt-2 flex items-center justify-between gap-2">
        {op.failedCount > 0 ? (
          <button
            type="button"
            className="flex items-center gap-1 text-xs text-warning hover:underline"
            aria-expanded={open}
            onClick={() => setOpen((v) => !v)}
          >
            {t('{count} entries could not be deleted', { count: op.failedCount })}
            <ChevronDown className={cn('size-3.5 transition-transform', open && 'rotate-180')} />
          </button>
        ) : (
          <span />
        )}
        {active && op.mode === 'tracked' && (
          <Button size="xs" variant="secondary" disabled={!op.operationId || op.cancelling} onClick={() => void cancelDelete(op.key)}>
            {op.cancelling ? t('Cancelling...') : t('Cancel')}
          </Button>
        )}
      </div>

      {open && op.failedCount > 0 && (
        <ul aria-label={t('Details')} className="mt-2 max-h-40 space-y-1 overflow-y-auto rounded-lg bg-surface-2/60 p-2 text-xs">
          {op.failures.map((f, i) => (
            <li key={`${f.path}-${i}`} className="min-w-0">
              <span className="block truncate font-medium" title={f.path}>
                {f.path}
              </span>
              {f.reason && <span className="block truncate text-fg-muted" title={f.reason}>{f.reason}</span>}
            </li>
          ))}
          {more > 0 && <li className="text-fg-muted">{t('and {count} more', { count: more })}</li>}
        </ul>
      )}
    </div>
  );
}

/**
 * The stacked progress cards of the running and just finished deletes
 * (lazy chunk, mounted by <DeleteProgressHost/> while there is one).
 */
export default function DeleteProgressPanel() {
  const ops = useDeletes((s) => s.ops);
  const waiting = ops.some((o) => o.progress?.phase === 'waiting');
  const now = useTicker(waiting);
  return (
    <div
      aria-label={t('Deletes')}
      className="pointer-events-none absolute inset-x-3 bottom-3 z-30 flex max-h-[70%] flex-col gap-2 overflow-y-auto sm:left-4 sm:right-auto sm:w-[22rem]"
    >
      {ops.map((op) => (
        <DeleteCard key={op.key} op={op} now={now} />
      ))}
    </div>
  );
}
