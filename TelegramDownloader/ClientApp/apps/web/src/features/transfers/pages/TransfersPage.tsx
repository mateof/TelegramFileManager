import type { TransferClearState, TransferSummaryDto } from '@tfm/api-client';
import {
  Badge,
  Button,
  Card,
  CountBadge,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  Input,
  SegmentedControl,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger
} from '@tfm/ui';
import { ArrowDown, ArrowDownUp, ArrowUp, Brush, ChevronDown, Database, FolderSync, ListX, Pause, Play, Search, Square } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { t } from '@/i18n';
import { useRealtime } from '@/lib/realtime';
import { PageHeader } from '@/shell/PageHeader';
import { clearFinished, clearQueue, pauseAllDownloads, stopAllDownloads } from '../actions';
import { matchesFilter, tabCounts, tabItems, type StateFilter, type TransferTab } from '../model';
import { PersistedTab } from '../PersistedTab';
import { SpeedChart } from '../SpeedChart';
import { TransferList } from '../TransferList';
import { useTransfersLive } from '../useTransfersLive';

type Tab = TransferTab | 'persisted';

function Stat({ icon, label, value, extra }: { icon: ReactNode; label: string; value: ReactNode; extra?: ReactNode }) {
  return (
    <Card className="flex items-center gap-3 p-4">
      <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-accent-300 [&_svg]:size-5">{icon}</span>
      <div className="min-w-0">
        <p className="text-xs text-fg-muted">{label}</p>
        <p className="truncate text-lg font-semibold tabular-nums text-fg">{value}</p>
        {extra && <p className="truncate text-xs text-fg-subtle">{extra}</p>}
      </div>
    </Card>
  );
}

function Stats({ summary }: { summary: TransferSummaryDto | null | undefined }) {
  const s = summary;
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Stat icon={<ArrowDown />} label={t('Download speed')} value={s?.downloadSpeed ?? '–'} extra={s ? t('{count} running', { count: s.activeDownloads }) : undefined} />
      <Stat icon={<ArrowUp />} label={t('Upload speed')} value={s?.uploadSpeed ?? '–'} extra={s ? t('{count} running', { count: s.activeUploads }) : undefined} />
      <Stat
        icon={<ListX />}
        label={t('In the queue')}
        value={s ? s.queuedDownloads + s.queuedUploads : '–'}
        extra={s ? `${t('{count} downloads', { count: s.queuedDownloads })} · ${t('{count} uploads', { count: s.queuedUploads })}` : undefined}
      />
      <Stat icon={<FolderSync />} label={t('Tasks')} value={s ? `${s.activeTasks} / ${s.totalTasks}` : '–'} extra={s?.downloadsPaused ? t('Downloads paused') : undefined} />
    </div>
  );
}

const CLEAR_OPTIONS: { label: () => string; states?: TransferClearState[] }[] = [
  { label: () => t('Clear completed'), states: ['completed'] },
  { label: () => t('Clear failed'), states: ['error'] },
  { label: () => t('Clear cancelled'), states: ['canceled'] },
  { label: () => t('Clear every finished entry') }
];

function Toolbar({ tab, summary, filter, onFilter, query, onQuery }: { tab: TransferTab; summary: TransferSummaryDto | null | undefined; filter: StateFilter; onFilter: (f: StateFilter) => void; query: string; onQuery: (q: string) => void }) {
  const paused = !!summary?.downloadsPaused;
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Input className="w-full sm:w-60" value={query} onChange={(e) => onQuery(e.target.value)} placeholder={t('Filter by name')} aria-label={t('Filter by name')} leading={<Search />} />
      <SegmentedControl
        aria-label={t('State')}
        size="sm"
        value={filter}
        onValueChange={onFilter}
        options={[
          { value: 'all', label: t('All') },
          { value: 'active', label: t('Running') },
          { value: 'queued', label: t('Queued') },
          { value: 'finished', label: t('Finished') },
          { value: 'failed', label: t('Failed') }
        ]}
      />
      <div className="flex-1" />
      {tab === 'downloads' && (
        <>
          <Button variant="secondary" size="sm" icon={paused ? <Play /> : <Pause />} onClick={() => void pauseAllDownloads(paused)}>
            {paused ? t('Resume all') : t('Pause all')}
          </Button>
          <Button variant="secondary" size="sm" icon={<Square />} onClick={() => void stopAllDownloads()}>
            {t('Stop all')}
          </Button>
        </>
      )}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="sm" icon={<Brush />} iconRight={<ChevronDown />}>
            {t('Clean up')}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {CLEAR_OPTIONS.map((o) => (
            <DropdownMenuItem key={o.label()} onSelect={() => void clearFinished(tab, o.states)}>
              {o.label()}
            </DropdownMenuItem>
          ))}
          {tab !== 'tasks' && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuItem destructive onSelect={() => void clearQueue(tab)}>
                {t('Empty the queue')}
              </DropdownMenuItem>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}

export default function TransfersPage() {
  const { snapshot, metrics, series, windowSeconds } = useTransfersLive();
  const status = useRealtime((s) => s.transfersStatus);
  const liveSummary = useRealtime((s) => s.transferSummary);
  const summary = liveSummary ?? snapshot?.summary;
  const [tab, setTab] = useState<Tab>('downloads');
  const [filter, setFilter] = useState<StateFilter>('all');
  const [query, setQuery] = useState('');
  const counts = useMemo(() => tabCounts(snapshot), [snapshot]);

  const items = useMemo(() => {
    if (tab === 'persisted') return [];
    const q = query.trim().toLowerCase();
    return tabItems(snapshot, tab).filter((i) => matchesFilter(i, filter) && (!q || i.name.toLowerCase().includes(q) || i.path?.toLowerCase().includes(q)));
  }, [snapshot, tab, filter, query]);

  const tabLabel = (id: TransferTab, label: string, icon: ReactNode) => (
    <TabsTrigger value={id}>
      {icon}
      {label}
      {counts[id].total > 0 && <span className="text-xs tabular-nums text-fg-subtle">{counts[id].total}</span>}
      <CountBadge count={counts[id].active} />
    </TabsTrigger>
  );

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={t('Transfers')}
        description={t('Downloads, uploads and background tasks in real time.')}
        icon={<ArrowDownUp />}
        actions={
          <Badge tone={status === 'connected' ? 'success' : 'warning'} dot>
            {status === 'connected' ? t('Live') : t('Reconnecting...')}
          </Badge>
        }
      />

      <Stats summary={summary} />

      <Card className="p-4">
        <SpeedChart series={series} windowSeconds={windowSeconds} />
      </Card>

      <Tabs value={tab} onValueChange={(v) => setTab(v as Tab)} className="flex flex-col gap-4">
        <TabsList className="w-fit max-w-full overflow-x-auto">
          {tabLabel('downloads', t('Downloads'), <ArrowDown />)}
          {tabLabel('uploads', t('Uploads'), <ArrowUp />)}
          {tabLabel('tasks', t('Tasks'), <FolderSync />)}
          <TabsTrigger value="persisted">
            <Database />
            {t('Saved')}
          </TabsTrigger>
        </TabsList>

        {(['downloads', 'uploads', 'tasks'] as const).map((id) => (
          <TabsContent key={id} value={id} className="flex flex-col gap-3">
            <Toolbar tab={id} summary={summary} filter={filter} onFilter={setFilter} query={query} onQuery={setQuery} />
            {tab === id && (
              <TransferList
                items={items}
                metrics={metrics}
                empty={
                  <EmptyState
                    icon={id === 'uploads' ? <ArrowUp /> : id === 'tasks' ? <FolderSync /> : <ArrowDown />}
                    title={snapshot ? (filter !== 'all' || query ? t('Nothing matches the filter') : t('Nothing here right now')) : t('Connecting to the server...')}
                    description={snapshot && filter === 'all' && !query ? t('Transfers you start from the channels and local files show up here.') : undefined}
                  />
                }
              />
            )}
          </TabsContent>
        ))}
        <TabsContent value="persisted">
          <PersistedTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
