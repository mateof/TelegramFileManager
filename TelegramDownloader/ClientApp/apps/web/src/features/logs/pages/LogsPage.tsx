import type { LogEntryDto } from '@tfm/api-client';
import { Badge, Button, cn, confirm, Dialog, DialogContent, EmptyState, Input, Select, Sheet, SheetContent, Spinner, Switch, toast, useMediaQuery } from '@tfm/ui';
import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useVirtualizer } from '@tanstack/react-virtual';
import { AlertTriangle, CalendarRange, Copy, FilterX, Radio, RefreshCw, ScrollText, Search, Trash2, X } from 'lucide-react';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { PageHeader } from '@/shell/PageHeader';
import { buildLogQuery, EMPTY_FILTERS, filtersFromParams, filtersToParams, LOG_LEVELS, levelTone, minutesAgoInput, shortLogger, type LogFilters } from '../model';

const PAGE_SIZE = 100;
const ALL = '__all';
const GRID = 'grid-cols-[7.5rem_6.5rem_5.5rem_11rem_1fr_1.5rem]';

function levelLabel(level: string | null | undefined): string {
  switch (level) {
    case 'Verbose':
      return t('Verbose');
    case 'Debug':
      return t('Debug');
    case 'Information':
      return t('Info');
    case 'Warning':
      return t('Warning');
    case 'Error':
      return t('Error');
    case 'Fatal':
      return t('Fatal');
    default:
      return level ?? '';
  }
}

async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
    toast.success(t('Copied to the clipboard'));
  } catch {
    toast.error(t('Could not copy'));
  }
}

function SearchInput({ initial, onCommit }: { initial: string; onCommit: (value: string) => void }) {
  const [value, setValue] = useState(initial);
  useEffect(() => {
    if (value === initial) return;
    const timer = setTimeout(() => onCommit(value), 350);
    return () => clearTimeout(timer);
  }, [value, initial, onCommit]);
  return (
    <Input
      value={value}
      onChange={(e) => setValue(e.target.value)}
      placeholder={t('Search in messages...')}
      aria-label={t('Search in messages...')}
      leading={<Search />}
      className="min-w-56 flex-1"
      trailing={
        value ? (
          <button type="button" aria-label={t('Clear search')} onClick={() => setValue('')} className="flex size-7 items-center justify-center rounded-md text-fg-subtle hover:text-fg">
            <X className="size-4" />
          </button>
        ) : undefined
      }
    />
  );
}

function Filters({ filters, onChange }: { filters: LogFilters; onChange: (f: LogFilters) => void }) {
  const loggers = useQuery({ queryKey: ['logs', 'loggers'], queryFn: ({ signal }) => api.system.logLoggers(signal), staleTime: 5 * 60_000 });
  const versions = useQuery({ queryKey: ['logs', 'versions'], queryFn: ({ signal }) => api.system.logVersions(signal), staleTime: 5 * 60_000 });
  const fromId = useId();
  const toId = useId();
  const [searchKey, setSearchKey] = useState(0);

  const set = (patch: Partial<LogFilters>) => onChange({ ...filters, ...patch });
  const commitSearch = useCallback((search: string) => onChange({ ...filters, search }), [filters, onChange]);
  const range = (minutes: number) => set({ from: minutesAgoInput(minutes), to: '' });
  const active = Object.keys(filtersToParams(filters)).length > 0;

  return (
    <div className="flex flex-col gap-3 rounded-2xl border border-border bg-surface/60 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <SearchInput key={searchKey} initial={filters.search} onCommit={commitSearch} />
        <Select
          value={filters.level || ALL}
          onValueChange={(v) => set({ level: v === ALL ? '' : v })}
          options={[{ value: ALL, label: t('All levels') }, ...LOG_LEVELS.map((l) => ({ value: l, label: levelLabel(l) }))]}
          className="w-40"
        />
        <Select
          value={filters.logger || ALL}
          onValueChange={(v) => set({ logger: v === ALL ? '' : v })}
          options={[{ value: ALL, label: t('All components') }, ...(loggers.data ?? []).map((l) => ({ value: l, label: shortLogger(l) }))]}
          className="w-48"
        />
        <Select
          value={filters.version || ALL}
          onValueChange={(v) => set({ version: v === ALL ? '' : v })}
          options={[{ value: ALL, label: t('All versions') }, ...(versions.data ?? []).map((v) => ({ value: v, label: v }))]}
          className="w-36"
        />
      </div>
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <CalendarRange className="size-4 text-fg-subtle" />
        <label htmlFor={fromId} className="text-xs text-fg-muted">
          {t('From')}
        </label>
        <Input id={fromId} type="datetime-local" inputSize="sm" value={filters.from} onChange={(e) => set({ from: e.target.value })} className="w-52" />
        <label htmlFor={toId} className="text-xs text-fg-muted">
          {t('To')}
        </label>
        <Input id={toId} type="datetime-local" inputSize="sm" value={filters.to} onChange={(e) => set({ to: e.target.value })} className="w-52" />
        <div className="flex gap-1">
          {[
            { m: 5, label: '5m', title: t('Last 5 minutes') },
            { m: 15, label: '15m', title: t('Last 15 minutes') },
            { m: 60, label: '1h', title: t('Last hour') },
            { m: 1440, label: '24h', title: t('Last 24 hours') }
          ].map((r) => (
            <Button key={r.m} size="xs" variant="ghost" title={r.title} aria-label={r.title} onClick={() => range(r.m)}>
              {r.label}
            </Button>
          ))}
        </div>
        {active && (
          <Button
            size="xs"
            variant="ghost"
            icon={<FilterX />}
            className="ml-auto"
            onClick={() => {
              setSearchKey((k) => k + 1);
              onChange(EMPTY_FILTERS);
            }}
          >
            {t('Clear filters')}
          </Button>
        )}
      </div>
    </div>
  );
}

function LogDetail({ log, onClose }: { log: LogEntryDto | null; onClose: () => void }) {
  const desktop = useMediaQuery('(min-width: 768px)');
  return (
    <Sheet open={!!log} onOpenChange={(open) => !open && onClose()}>
      <SheetContent
        side={desktop ? 'right' : 'bottom'}
        title={t('Log entry')}
        description={log ? new Date(log.timestamp).toLocaleString(getLanguage()) : undefined}
        className={desktop ? 'w-[min(40rem,92vw)]' : undefined}
      >
        {log && (
          <div className="flex flex-col gap-4 px-5 pb-6">
            <div className="flex flex-wrap gap-2">
              <Badge tone={levelTone(log.level)} dot>
                {levelLabel(log.level)}
              </Badge>
              {log.version && <Badge>v{log.version}</Badge>}
            </div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
              <dt className="text-fg-muted">{t('Component')}</dt>
              <dd className="break-all font-mono text-xs text-fg">{log.logger ?? '–'}</dd>
              <dt className="text-fg-muted">{t('Time')}</dt>
              <dd className="font-mono text-xs text-fg">{new Date(log.timestamp).toISOString()}</dd>
            </dl>
            <section className="flex flex-col gap-1.5">
              <div className="flex items-center justify-between">
                <h3 className="text-xs font-semibold uppercase tracking-wide text-fg-subtle">{t('Message')}</h3>
                <Button size="xs" variant="ghost" icon={<Copy />} onClick={() => void copyText(log.message ?? '')}>
                  {t('Copy')}
                </Button>
              </div>
              <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-words rounded-xl border border-border bg-surface-2 p-3 font-mono text-xs text-fg">{log.message}</pre>
            </section>
            {log.exception && (
              <section className="flex flex-col gap-1.5">
                <div className="flex items-center justify-between">
                  <h3 className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-danger">
                    <AlertTriangle className="size-3.5" /> {t('Exception')}
                  </h3>
                  <Button size="xs" variant="ghost" icon={<Copy />} onClick={() => void copyText(log.exception ?? '')}>
                    {t('Copy')}
                  </Button>
                </div>
                <pre className="max-h-[50dvh] overflow-auto whitespace-pre rounded-xl border border-danger/30 bg-danger-soft p-3 font-mono text-[11px] leading-relaxed text-fg">{log.exception}</pre>
              </section>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}

function DeleteOldDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const qc = useQueryClient();
  const [days, setDays] = useState(30);
  const id = useId();
  const del = useMutation({
    mutationFn: () => api.system.deleteLogs({ daysToKeep: days }),
    onSuccess: (count) => {
      toast.success(t('{count} log entries deleted', { count }));
      onOpenChange(false);
      void qc.invalidateQueries({ queryKey: ['logs'] });
    },
    onError: (error) => toast.error(t('Could not delete the logs'), { description: errorMessage(error) })
  });
  const valid = Number.isInteger(days) && days >= 0;
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t('Delete old logs')}
        description={t('Entries older than the number of days below are deleted for good.')}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button
              variant="danger"
              icon={<Trash2 />}
              loading={del.isPending}
              disabled={!valid}
              onClick={async () => {
                if (days === 0 && !(await confirm({ title: t('Delete every log entry?'), destructive: true, confirmLabel: t('Delete') }))) return;
                del.mutate();
              }}
            >
              {t('Delete')}
            </Button>
          </>
        }
      >
        <div className="flex items-center gap-3">
          <label htmlFor={id} className="text-sm text-fg">
            {t('Keep the last')}
          </label>
          <Input id={id} type="number" min={0} value={Number.isNaN(days) ? '' : days} onChange={(e) => setDays(e.target.value === '' ? Number.NaN : Number(e.target.value))} className="w-24" />
          <span className="text-sm text-fg-muted">{t('days')}</span>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function LogRow({ log, onOpen, desktop }: { log: LogEntryDto; onOpen: () => void; desktop: boolean }) {
  const date = new Date(log.timestamp);
  const lang = getLanguage();
  if (!desktop) {
    return (
      <button type="button" onClick={onOpen} className="flex w-full flex-col gap-1 border-b border-border px-3 py-2.5 text-left hover:bg-surface-2 focus-visible:bg-surface-2 focus-visible:outline-none">
        <span className="flex items-center gap-2 text-[11px] text-fg-subtle">
          <Badge tone={levelTone(log.level)} className="px-1.5 py-0 text-[10px]">
            {levelLabel(log.level)}
          </Badge>
          {date.toLocaleString(lang)}
          <span className="truncate">{shortLogger(log.logger)}</span>
          {log.exception && <AlertTriangle className="ml-auto size-3.5 shrink-0 text-danger" />}
        </span>
        <span className="line-clamp-2 text-sm text-fg">{log.message}</span>
      </button>
    );
  }
  return (
    <button
      type="button"
      onClick={onOpen}
      className={cn('grid w-full items-center gap-3 border-b border-border px-3 py-2 text-left text-sm hover:bg-surface-2 focus-visible:bg-surface-2 focus-visible:outline-none', GRID)}
    >
      <span className="flex flex-col leading-tight">
        <span className="font-mono text-xs text-fg">{date.toLocaleTimeString(lang)}</span>
        <span className="text-[10px] text-fg-subtle">{date.toLocaleDateString(lang)}</span>
      </span>
      <span>
        <Badge tone={levelTone(log.level)} dot>
          {levelLabel(log.level)}
        </Badge>
      </span>
      <span className="truncate text-xs text-fg-muted">{log.version}</span>
      <span className="truncate text-xs text-fg-muted" title={log.logger ?? ''}>
        {shortLogger(log.logger)}
      </span>
      <span className="truncate text-fg">{log.message}</span>
      <span>{log.exception && <AlertTriangle className="size-4 text-danger" aria-label={t('Has an exception')} />}</span>
    </button>
  );
}

export default function LogsPage() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => filtersFromParams(params), [params]);
  const desktop = useMediaQuery('(min-width: 768px)');
  const [live, setLive] = useState(false);
  const [selected, setSelected] = useState<LogEntryDto | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const qc = useQueryClient();
  const scrollRef = useRef<HTMLDivElement>(null);
  const liveId = useId();

  const setFilters = useCallback((f: LogFilters) => setParams(filtersToParams(f), { replace: true }), [setParams]);

  const logs = useInfiniteQuery({
    queryKey: ['logs', 'list', filters],
    queryFn: ({ pageParam, signal }) => api.system.logs(buildLogQuery(filters, pageParam, PAGE_SIZE), signal),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page.hasNext ? last.page.page + 1 : undefined),
    placeholderData: keepPreviousData,
    // Live tail: keep only the newest page and poll it
    refetchInterval: live ? 5000 : false,
    maxPages: live ? 1 : undefined
  });

  const toggleLive = (on: boolean) => {
    setLive(on);
    if (on) {
      scrollRef.current?.scrollTo({ top: 0 });
      void qc.resetQueries({ queryKey: ['logs', 'list'] });
    }
  };

  const rows = useMemo(() => logs.data?.pages.flatMap((p) => p.items) ?? [], [logs.data]);
  const total = logs.data?.pages[0]?.page.totalItems ?? 0;

  // TanStack Virtual returns functions that cannot be memoized; the compiler lint knows it
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => (desktop ? 45 : 66),
    overscan: 12
  });
  const items = virtualizer.getVirtualItems();
  const lastIndex = items[items.length - 1]?.index ?? 0;
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = logs;
  useEffect(() => {
    if (!live && hasNextPage && !isFetchingNextPage && lastIndex >= rows.length - 20) void fetchNextPage();
  }, [lastIndex, rows.length, hasNextPage, isFetchingNextPage, fetchNextPage, live]);

  const lang = getLanguage();

  return (
    <div className="mx-auto flex h-full w-full max-w-7xl flex-col gap-4 p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={t('Logs')}
        description={t('Server log viewer.')}
        icon={<ScrollText />}
        actions={
          <>
            <label htmlFor={liveId} className="flex cursor-pointer items-center gap-2 rounded-lg border border-border px-2.5 py-1.5 text-sm text-fg">
              <Radio className={cn('size-4', live ? 'animate-pulse text-success' : 'text-fg-subtle')} />
              {t('Live tail')}
              <Switch id={liveId} checked={live} onCheckedChange={toggleLive} />
            </label>
            <Button variant="secondary" size="sm" icon={<RefreshCw />} loading={logs.isRefetching && !isFetchingNextPage && !live} onClick={() => void logs.refetch()}>
              {t('Refresh')}
            </Button>
            <Button variant="outline" size="sm" icon={<Trash2 />} onClick={() => setDeleteOpen(true)}>
              {t('Delete old')}
            </Button>
          </>
        }
      />

      <Filters filters={filters} onChange={setFilters} />

      <div className="flex items-center justify-between text-xs text-fg-muted">
        <span aria-live="polite">{logs.isPending ? t('Loading...') : t('{shown} of {total} entries', { shown: rows.length.toLocaleString(lang), total: total.toLocaleString(lang) })}</span>
        {logs.isFetching && <Spinner size="sm" />}
      </div>

      <div className="flex min-h-80 flex-1 flex-col overflow-hidden rounded-2xl border border-border bg-surface/70">
        {desktop && (
          <div className={cn('grid gap-3 border-b border-border bg-surface-2/80 px-3 py-2 text-[11px] font-semibold uppercase tracking-wide text-fg-subtle', GRID)} aria-hidden>
            <span>{t('Time')}</span>
            <span>{t('Level')}</span>
            <span>{t('Version')}</span>
            <span>{t('Component')}</span>
            <span>{t('Message')}</span>
            <span />
          </div>
        )}
        <div ref={scrollRef} className="min-h-0 flex-1 overflow-y-auto">
          {logs.isError ? (
            <EmptyState icon={<AlertTriangle />} title={t('Could not load the logs')} description={errorMessage(logs.error)} action={<Button onClick={() => void logs.refetch()}>{t('Retry')}</Button>} />
          ) : logs.isPending ? (
            <div className="flex justify-center py-16">
              <Spinner size="lg" />
            </div>
          ) : rows.length === 0 ? (
            <EmptyState icon={<ScrollText />} title={t('No logs found')} description={t('Try adjusting your filters or wait for new log entries.')} />
          ) : (
            <div role="list" aria-label={t('Log entries')} style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
              {items.map((item) => {
                const log = rows[item.index];
                if (!log) return null;
                return (
                  <div key={item.key} role="listitem" data-index={item.index} ref={virtualizer.measureElement} className="absolute inset-x-0 top-0" style={{ transform: `translateY(${item.start}px)` }}>
                    <LogRow log={log} desktop={desktop} onOpen={() => setSelected(log)} />
                  </div>
                );
              })}
            </div>
          )}
          {isFetchingNextPage && (
            <div className="flex justify-center py-3">
              <Spinner size="sm" />
            </div>
          )}
        </div>
      </div>

      <LogDetail log={selected} onClose={() => setSelected(null)} />
      <DeleteOldDialog open={deleteOpen} onOpenChange={setDeleteOpen} />
    </div>
  );
}
