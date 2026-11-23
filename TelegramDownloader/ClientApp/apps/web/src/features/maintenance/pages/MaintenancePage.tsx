import type { DatabaseStatsDto, PathAnalysisDto } from '@tfm/api-client';
import {
  Badge,
  Button,
  Card,
  Checkbox,
  cn,
  confirm,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  IconButton,
  Skeleton,
  toast
} from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Database,
  DoorOpen,
  Eraser,
  HeartCrack,
  Link2,
  ListChecks,
  LogIn,
  MoreHorizontal,
  Plus,
  RefreshCw,
  ScanSearch,
  Trash2,
  Wrench
} from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { queryKeys } from '@/lib/queryKeys';
import { PageHeader } from '@/shell/PageHeader';
import { CreateChannelDialog, InvitationDialog, JoinChannelDialog, LeaveChannelDialog } from '../components/ChannelDialogs';
import { isMaintenanceDialog, isOrphan } from '../model';

const dbKey = ['maintenance', 'databases'] as const;

interface RowState {
  analysis?: PathAnalysisDto;
  analyzing?: boolean;
  repairing?: boolean;
  repaired?: number;
}

function Stat({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="rounded-xl border border-border bg-surface-2/60 px-4 py-3">
      <p className="text-[11px] uppercase tracking-wide text-fg-subtle">{label}</p>
      <p className="mt-0.5 text-lg font-semibold tabular-nums text-fg">{value}</p>
    </div>
  );
}

function DatabasesCard() {
  const qc = useQueryClient();
  const dbs = useQuery({ queryKey: dbKey, queryFn: ({ signal }) => api.system.databases(signal) });
  const [rows, setRows] = useState<Record<string, RowState>>({});
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const list = useMemo(() => [...(dbs.data ?? [])].sort((a, b) => Number(isOrphan(b)) - Number(isOrphan(a)) || b.sizeInBytes - a.sizeInBytes), [dbs.data]);
  const orphans = list.filter(isOrphan);
  const lang = getLanguage();

  const patchRow = (id: string, patch: RowState) => setRows((r) => ({ ...r, [id]: { ...r[id], ...patch } }));
  const toggle = (id: string) =>
    setSelected((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const analyze = async (id: string) => {
    patchRow(id, { analyzing: true });
    try {
      patchRow(id, { analysis: await api.system.analyzeDatabase(id), analyzing: false });
    } catch (e) {
      patchRow(id, { analyzing: false, analysis: { databaseName: id, totalItems: 0, itemsWithIssues: 0, filterPathIssues: 0, filterIdIssues: 0, filePathIssues: 0, hasIssues: false, error: errorMessage(e) } });
    }
  };
  const repair = async (id: string) => {
    patchRow(id, { repairing: true });
    try {
      const count = await api.system.repairDatabase(id);
      patchRow(id, { repairing: false, repaired: count });
      await analyze(id);
      return count;
    } catch (e) {
      patchRow(id, { repairing: false });
      toast.error(t('Could not repair {name}', { name: id }), { description: errorMessage(e) });
      return 0;
    }
  };

  const ids = selected.size > 0 ? [...selected] : list.map((d) => d.channelId);
  const analyzeMany = async () => {
    setBusy(true);
    for (const id of ids) await analyze(id);
    setBusy(false);
  };
  const withIssues = list.filter((d) => rows[d.channelId]?.analysis?.hasIssues);
  const repairAll = async () => {
    const ok = await confirm({ title: t('Repair {count} databases?', { count: withIssues.length }), description: t('Broken folder paths are rewritten. Make a backup first if the data matters to you.'), confirmLabel: t('Repair') });
    if (!ok) return;
    setBusy(true);
    let total = 0;
    for (const d of withIssues) total += await repair(d.channelId);
    setBusy(false);
    toast.success(t('{count} entries repaired', { count: total }));
  };

  const del = useMutation({
    mutationFn: async (targets: string[]) => {
      for (const id of targets) await api.channels.deleteDatabase(id);
      return targets.length;
    },
    onSuccess: (count) => {
      toast.success(t('{count} databases deleted', { count }));
      setSelected(new Set());
    },
    onError: (error) => toast.error(t('Could not delete the database'), { description: errorMessage(error) }),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: dbKey });
      void qc.invalidateQueries({ queryKey: queryKeys.channels });
    }
  });
  const deleteDbs = async (targets: DatabaseStatsDto[]) => {
    if (targets.length === 0) return;
    const size = targets.reduce((s, d) => s + d.sizeInBytes, 0);
    const ok = await confirm({
      title: targets.length === 1 ? t('Delete the index of "{name}"?', { name: targets[0]!.channelName ?? targets[0]!.channelId }) : t('Delete {count} indexes?', { count: targets.length }),
      description: t('Only the local index ({size}) is removed: the files stay in Telegram, but the folder structure is forgotten until the channel is refreshed again.', { size: formatBytes(size) }),
      destructive: true,
      confirmLabel: t('Delete')
    });
    if (ok) del.mutate(targets.map((d) => d.channelId));
  };

  const totalSize = list.reduce((s, d) => s + d.sizeInBytes, 0);
  const totalDocs = list.reduce((s, d) => s + d.documentCount, 0);
  const selectedDbs = list.filter((d) => selected.has(d.channelId));

  return (
    <Card className="overflow-hidden">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-5 py-4">
        <div>
          <h2 className="flex items-center gap-2 text-sm font-semibold text-fg">
            <Database className="size-4.5 text-accent-400" /> {t('Channel databases')}
          </h2>
          <p className="mt-0.5 text-xs text-fg-muted">{t('Every channel index is a MongoDB database. Check folder-path integrity, repair it and remove the indexes of channels you no longer have.')}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" icon={<ScanSearch />} disabled={busy || list.length === 0} loading={busy && withIssues.length === 0} onClick={() => void analyzeMany()}>
            {selected.size > 0 ? t('Analyze selected ({count})', { count: selected.size }) : t('Analyze all')}
          </Button>
          <Button size="sm" variant="soft" icon={<Wrench />} disabled={busy || withIssues.length === 0} onClick={() => void repairAll()}>
            {t('Repair all ({count})', { count: withIssues.length })}
          </Button>
          <IconButton label={t('Refresh')} size="sm" variant="ghost" onClick={() => void dbs.refetch()}>
            <RefreshCw className={cn(dbs.isFetching && 'animate-spin')} />
          </IconButton>
        </div>
      </div>

      {dbs.isPending ? (
        <div className="flex flex-col gap-2 p-5">
          <Skeleton className="h-14" />
          <Skeleton className="h-14" />
          <Skeleton className="h-14" />
        </div>
      ) : dbs.isError ? (
        <EmptyState icon={<Database />} title={t('Could not load the databases')} description={errorMessage(dbs.error)} action={<Button onClick={() => void dbs.refetch()}>{t('Retry')}</Button>} />
      ) : list.length === 0 ? (
        <EmptyState icon={<Database />} title={t('No databases found.')} />
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 p-5 sm:grid-cols-4">
            <Stat label={t('Databases')} value={list.length} />
            <Stat label={t('Total size')} value={formatBytes(totalSize)} />
            <Stat label={t('Records')} value={totalDocs.toLocaleString(lang)} />
            <Stat label={t('Orphaned')} value={<span className={cn(orphans.length > 0 && 'text-warning')}>{orphans.length}</span>} />
          </div>
          {(orphans.length > 0 || selected.size > 0) && (
            <div className="flex flex-wrap items-center gap-2 border-y border-border bg-surface-2/50 px-5 py-2.5 text-sm">
              {orphans.length > 0 && (
                <>
                  <HeartCrack className="size-4 text-warning" />
                  <span className="text-fg-muted">{t('{count} databases without an active Telegram channel ({size}).', { count: orphans.length, size: formatBytes(orphans.reduce((s, d) => s + d.sizeInBytes, 0)) })}</span>
                  <Button size="xs" variant="ghost" onClick={() => setSelected(new Set(orphans.map((o) => o.channelId)))}>
                    {t('Select orphaned')}
                  </Button>
                </>
              )}
              {selected.size > 0 && (
                <div className="ml-auto flex gap-2">
                  <Button size="xs" variant="ghost" onClick={() => setSelected(new Set())}>
                    {t('Deselect all')}
                  </Button>
                  <Button size="xs" variant="danger" icon={<Trash2 />} loading={del.isPending} onClick={() => void deleteDbs(selectedDbs)}>
                    {t('Delete selected ({count})', { count: selected.size })}
                  </Button>
                </div>
              )}
            </div>
          )}
          <ul className="divide-y divide-border">
            {list.map((d) => {
              const r = rows[d.channelId] ?? {};
              const orphan = isOrphan(d);
              const a = r.analysis;
              return (
                <li key={d.channelId} className={cn('flex items-start gap-3 px-5 py-3', a?.hasIssues && 'border-l-2 border-l-warning', r.repaired !== undefined && !a?.hasIssues && 'border-l-2 border-l-success')}>
                  <Checkbox checked={selected.has(d.channelId)} onCheckedChange={() => toggle(d.channelId)} aria-label={t('Select {name}', { name: d.channelName ?? d.channelId })} className="mt-1" />
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-fg">
                      <span className="truncate">{d.channelName ?? t('Unknown channel')}</span>
                      {orphan && <Badge tone="warning">{t('Orphaned')}</Badge>}
                      {r.analyzing && <Badge tone="info">{t('Analyzing...')}</Badge>}
                      {r.repairing && <Badge tone="info">{t('Repairing...')}</Badge>}
                      {a && !a.error && (a.hasIssues ? <Badge tone="warning" dot>{t('{count} issues', { count: a.itemsWithIssues })}</Badge> : <Badge tone="success" dot>OK</Badge>)}
                      {r.repaired !== undefined && <Badge tone="success">{t('Repaired ({count})', { count: r.repaired })}</Badge>}
                    </p>
                    <p className="mt-0.5 flex flex-wrap gap-x-3 gap-y-0.5 text-[11px] text-fg-subtle">
                      <span>ID {d.channelId}</span>
                      <span>{d.sizeText}</span>
                      <span>{t('{count} records', { count: d.documentCount.toLocaleString(lang) })}</span>
                      {d.createdAt && <span>{t('Created {date}', { date: new Date(d.createdAt).toLocaleDateString(lang) })}</span>}
                      {d.lastModified && <span>{t('Modified {date}', { date: new Date(d.lastModified).toLocaleString(lang) })}</span>}
                    </p>
                    {a?.hasIssues && (
                      <p className="mt-1 text-[11px] text-warning">
                        {t('Total {total} · FilterPath {fp} · FilterId {fi} · FilePath {fl}', { total: a.totalItems, fp: a.filterPathIssues, fi: a.filterIdIssues, fl: a.filePathIssues })}
                      </p>
                    )}
                    {a?.error && <p className="mt-1 text-[11px] text-danger">{a.error}</p>}
                  </div>
                  {a?.hasIssues && (
                    <Button size="xs" variant="soft" icon={<Wrench />} loading={r.repairing} disabled={busy} onClick={() => void repair(d.channelId)}>
                      {t('Repair')}
                    </Button>
                  )}
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <IconButton label={t('Actions')} size="sm" variant="ghost">
                        <MoreHorizontal />
                      </IconButton>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onSelect={() => void analyze(d.channelId)}>
                        <ScanSearch /> {t('Analyze paths')}
                      </DropdownMenuItem>
                      <DropdownMenuItem disabled={!a?.hasIssues} onSelect={() => void repair(d.channelId)}>
                        <Wrench /> {t('Repair paths')}
                      </DropdownMenuItem>
                      <DropdownMenuSeparator />
                      <DropdownMenuItem className="text-danger" onSelect={() => void deleteDbs([d])}>
                        <Trash2 /> {t('Delete index database')}
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </Card>
  );
}

function ToolCard({ icon, title, description, children }: { icon: ReactNode; title: string; description: string; children: ReactNode }) {
  return (
    <Card className="flex flex-col gap-4 p-5">
      <div className="flex items-start gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-accent-300 [&_svg]:size-5">{icon}</span>
        <div>
          <h2 className="text-sm font-semibold text-fg">{title}</h2>
          <p className="mt-0.5 text-xs leading-relaxed text-fg-muted">{description}</p>
        </div>
      </div>
      <div className="mt-auto flex flex-wrap items-center gap-2">{children}</div>
    </Card>
  );
}

function OrphanedFavoritesCard() {
  const qc = useQueryClient();
  const scan = useQuery({ queryKey: ['maintenance', 'orphaned-favorites'], queryFn: ({ signal }) => api.system.orphanedFavorites(signal), enabled: false });
  const clean = useMutation({
    mutationFn: () => api.system.cleanOrphanedFavorites(),
    onSuccess: (removed) => {
      toast.success(t('{count} orphaned favourites removed', { count: removed.length }));
      qc.setQueryData(['maintenance', 'orphaned-favorites'], []);
      void qc.invalidateQueries({ queryKey: queryKeys.channels });
    },
    onError: (error) => toast.error(t('Could not clean the favourites'), { description: errorMessage(error) })
  });
  const count = scan.data?.length;
  return (
    <ToolCard icon={<HeartCrack />} title={t('Orphaned favourites')} description={t('Favourite channels you can no longer reach in Telegram (left or deleted).')}>
      <Button size="sm" variant="secondary" icon={<ScanSearch />} loading={scan.isFetching} onClick={() => void scan.refetch()}>
        {t('Analyze')}
      </Button>
      {count !== undefined && (count === 0 ? <Badge tone="success" dot>{t('All good')}</Badge> : <Badge tone="warning">{t('{count} orphaned', { count })}</Badge>)}
      {scan.isError && <span className="text-xs text-danger">{errorMessage(scan.error)}</span>}
      <Button size="sm" variant="outline" icon={<Eraser />} disabled={!count} loading={clean.isPending} onClick={() => clean.mutate()} className="ml-auto">
        {t('Clean')}
      </Button>
    </ToolCard>
  );
}

function CleanupTasksCard() {
  const run = useMutation({
    mutationFn: () => api.system.cleanupTasks(),
    onSuccess: () => toast.success(t('Stale tasks cleaned')),
    onError: (error) => toast.error(t('Could not clean the tasks'), { description: errorMessage(error) })
  });
  return (
    <ToolCard icon={<ListChecks />} title={t('Stale tasks')} description={t('Drop persisted tasks that failed or got stuck longer than the cleanup period.')}>
      <Button size="sm" variant="secondary" icon={<Eraser />} loading={run.isPending} onClick={() => run.mutate()}>
        {t('Clean up tasks')}
      </Button>
    </ToolCard>
  );
}

type Dialogs = 'create-channel' | 'join-channel' | 'invitation' | 'leave' | null;

export default function MaintenancePage() {
  const [params, setParams] = useSearchParams();
  const fromUrl = params.get('dialog');
  const [dialog, setDialog] = useState<Dialogs>(null);
  const open: Dialogs = isMaintenanceDialog(fromUrl) ? fromUrl : dialog;
  const close = (o: boolean) => {
    if (o) return;
    setDialog(null);
    if (fromUrl) {
      const next = new URLSearchParams(params);
      next.delete('dialog');
      setParams(next, { replace: true });
    }
  };

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader title={t('Maintenance')} description={t('Database tools, import/export and cleanup.')} icon={<Wrench />} />

      <section aria-label={t('Channel management')} className="flex flex-col gap-3">
        <h2 className="text-xs font-semibold uppercase tracking-wider text-fg-subtle">{t('Channel management')}</h2>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {[
            { id: 'create-channel' as const, icon: <Plus />, label: t('Create channel'), note: t('New private channel for your files') },
            { id: 'join-channel' as const, icon: <LogIn />, label: t('Join channel'), note: t('With an invite link') },
            { id: 'invitation' as const, icon: <Link2 />, label: t('Get invitation link'), note: t('Share a channel you manage') },
            { id: 'leave' as const, icon: <DoorOpen />, label: t('Leave or delete'), note: t('Leave a channel or delete yours') }
          ].map((a) => (
            <Card key={a.id} interactive className="p-0">
              <button type="button" onClick={() => setDialog(a.id)} className="flex w-full items-center gap-3 p-4 text-left">
                <span className={cn('flex size-10 items-center justify-center rounded-xl [&_svg]:size-5', a.id === 'leave' ? 'bg-danger-soft text-danger' : 'bg-primary-soft text-accent-300')}>{a.icon}</span>
                <span className="min-w-0">
                  <span className="block text-sm font-medium text-fg">{a.label}</span>
                  <span className="block truncate text-xs text-fg-muted">{a.note}</span>
                </span>
              </button>
            </Card>
          ))}
        </div>
      </section>

      <DatabasesCard />

      <div className="grid gap-4 md:grid-cols-2">
        <OrphanedFavoritesCard />
        <CleanupTasksCard />
      </div>

      <CreateChannelDialog open={open === 'create-channel'} onOpenChange={close} />
      <JoinChannelDialog open={open === 'join-channel'} onOpenChange={close} />
      <InvitationDialog open={open === 'invitation'} onOpenChange={close} />
      <LeaveChannelDialog open={open === 'leave'} onOpenChange={close} />
    </div>
  );
}
