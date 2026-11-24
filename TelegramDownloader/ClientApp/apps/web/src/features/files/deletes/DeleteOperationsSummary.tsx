import { Progress } from '@tfm/ui';
import { t } from '@/i18n';
import { isActiveDelete, percentOf, useDeletes } from './deleteStore';
import { deletePhaseText, deleteTitle } from './deleteText';

/** "Other operations" of the transfers popover: the deletes in flight (store only, no lazy chunk). */
export function DeleteOperationsSummary() {
  const ops = useDeletes((s) => s.ops);
  const active = ops.filter(isActiveDelete);
  if (active.length === 0) return null;
  return (
    <div className="mt-3 flex flex-col gap-2 border-t border-border pt-3">
      <h4 className="text-xs font-semibold uppercase tracking-wide text-fg-subtle">{t('Other operations')}</h4>
      {active.map((op) => (
        <div key={op.key} className="flex flex-col gap-1">
          <div className="flex items-center justify-between gap-2 text-xs">
            <span className="truncate font-medium">{deleteTitle(op)}</span>
            <span className="shrink-0 text-fg-muted">{deletePhaseText(op)}</span>
          </div>
          <Progress size="xs" value={op.mode === 'sync' ? undefined : percentOf(op)} aria-label={deleteTitle(op)} />
        </div>
      ))}
    </div>
  );
}
