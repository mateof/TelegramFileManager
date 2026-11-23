import type { PersistedTaskDto } from '@tfm/api-client';
import { Badge, Button, confirm, EmptyState, IconButton, Progress, Skeleton, toast, type BadgeTone } from '@tfm/ui';
import { useQuery } from '@tanstack/react-query';
import { Database, Play, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes, formatRelative } from '@/lib/format';
import { queryClient } from '@/lib/queryClient';

const KEY = ['transfers', 'persisted'] as const;

function tone(state: string): BadgeTone {
  const s = state.toLowerCase();
  if (s === 'error') return 'danger';
  if (s === 'working') return 'accent';
  if (s === 'paused') return 'warning';
  if (s === 'completed') return 'success';
  return 'neutral';
}

/** Transfers saved in MongoDB (survive restarts): resume or forget them. */
export function PersistedTab() {
  const lang = getLanguage();
  const [busy, setBusy] = useState<string | null>(null);
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: KEY,
    queryFn: async ({ signal }) => (await api.transfers.persisted({ pageSize: 500 }, signal)).items,
    refetchInterval: 10_000
  });

  const resume = async (task: PersistedTaskDto) => {
    setBusy(task.internalId);
    try {
      const r = await api.transfers.resumePersisted(task.internalId);
      if (r.resumed) toast.success(t('"{name}" queued again', { name: task.name ?? task.internalId }), { description: r.message ?? undefined });
      else toast(r.message ?? t('Nothing to resume'), { description: r.action });
    } catch (e) {
      toast.error(t('Could not resume the task'), { description: errorMessage(e) });
    } finally {
      setBusy(null);
      void queryClient.invalidateQueries({ queryKey: KEY });
    }
  };

  const remove = async (task: PersistedTaskDto) => {
    try {
      await api.transfers.deletePersisted(task.internalId);
      void queryClient.invalidateQueries({ queryKey: KEY });
    } catch (e) {
      toast.error(t('Could not delete the task'), { description: errorMessage(e) });
    }
  };

  const clearAll = async () => {
    const ok = await confirm({
      title: t('Forget every saved task?'),
      description: t('They will not be resumed after a restart. Running transfers are not stopped.'),
      confirmLabel: t('Forget all'),
      destructive: true
    });
    if (!ok) return;
    try {
      await api.transfers.clearPersisted();
      void queryClient.invalidateQueries({ queryKey: KEY });
    } catch (e) {
      toast.error(t('Could not delete the tasks'), { description: errorMessage(e) });
    }
  };

  if (isLoading) return <div className="flex flex-col gap-2">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-20 rounded-2xl" />)}</div>;
  if (isError)
    return (
      <EmptyState
        icon={<Database />}
        title={t('Could not load the saved tasks')}
        description={errorMessage(error)}
        action={<Button variant="secondary" onClick={() => void refetch()}>{t('Retry')}</Button>}
      />
    );
  if (!data?.length)
    return <EmptyState icon={<Database />} title={t('No saved tasks')} description={t('Unfinished transfers are saved here so they can continue after a restart.')} />;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-fg-muted">{t('{count} saved tasks', { count: data.length })}</p>
        <Button variant="ghost" size="sm" icon={<Trash2 />} onClick={() => void clearAll()}>
          {t('Forget all')}
        </Button>
      </div>
      <ul className="flex flex-col gap-1.5">
        {data.map((task) => (
          <li key={task.internalId} className="flex items-center gap-3 rounded-2xl border border-border bg-surface/50 px-3 py-2.5">
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <div className="flex min-w-0 items-center gap-2">
                <span className="truncate text-sm font-medium text-fg">{task.name ?? task.internalId}</span>
                <Badge tone={tone(task.state)}>{task.state}</Badge>
                <Badge className="max-sm:hidden">{task.type}</Badge>
              </div>
              <Progress value={task.progress} size="xs" tone={task.state.toLowerCase() === 'error' ? 'danger' : 'accent'} aria-label={t('Progress')} />
              <p className="truncate text-xs text-fg-muted">
                {[
                  task.totalSize ? `${formatBytes(task.transmittedBytes)} / ${formatBytes(task.totalSize)}` : null,
                  task.channelName,
                  task.destinationPath ?? task.sourcePath,
                  formatRelative(task.lastUpdated, lang),
                  task.retryCount ? t('{count} retries', { count: task.retryCount }) : null
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
              {task.lastError && <p className="truncate text-xs text-danger" title={task.lastError}>{task.lastError}</p>}
            </div>
            <IconButton label={t('Resume')} size="sm" loading={busy === task.internalId} onClick={() => void resume(task)}>
              <Play />
            </IconButton>
            <IconButton label={t('Forget')} size="sm" className="hover:text-danger" onClick={() => void remove(task)}>
              <Trash2 />
            </IconButton>
          </li>
        ))}
      </ul>
    </div>
  );
}
