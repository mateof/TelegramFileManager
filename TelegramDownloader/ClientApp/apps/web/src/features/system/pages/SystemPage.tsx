import type { SpeedPointDto, SystemMetricsDto } from '@tfm/api-client';
import { Badge, Button, Card, cn, confirm, Progress, Skeleton, toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Activity, ArrowDownToLine, ArrowUpFromLine, Cpu, Crown, Film, FolderOpen, HardDrive, Image, MemoryStick, Power, RefreshCw, Trash2, User } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { useTelegramStatus } from '@/auth/useTelegramStatus';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { getEventsHub, getTransfersHub, useEventsMessage, useRealtime, useTransfersMessage } from '@/lib/realtime';
import { PageHeader } from '@/shell/PageHeader';
import { ChartLegend, LineChart } from '../components/LineChart';
import { VersionCard } from '../components/VersionCard';

const CAPACITY = 90; // 3 minutes of 2 s samples
const SPEED_CAPACITY = 120;

const ACCENT = 'var(--color-accent-400)';
const INFO = 'var(--color-info)';
const SUCCESS = 'var(--color-success)';
const WARNING = 'var(--color-warning)';

/** Live metrics: subscribes to `Metrics` while mounted (REST polling while the hub is down). */
function useLiveMetrics() {
  const [history, setHistory] = useState<SystemMetricsDto[]>([]);
  const hubStatus = useRealtime((s) => s.eventsStatus);
  const push = (m: SystemMetricsDto) => setHistory((h) => [...h, m].slice(-CAPACITY));

  const initial = useQuery({
    queryKey: ['system', 'metrics'],
    queryFn: ({ signal }) => api.system.metrics(signal),
    refetchInterval: hubStatus === 'connected' ? false : 5000
  });
  const [seen, setSeen] = useState<SystemMetricsDto | undefined>(undefined);
  if (initial.data && initial.data !== seen) {
    setSeen(initial.data);
    setHistory((h) => [...h, initial.data].slice(-CAPACITY));
  }

  useEffect(() => {
    let unsubscribe: (() => void) | undefined;
    let cancelled = false;
    void getEventsHub().then((hub) => {
      if (!cancelled) unsubscribe = hub.subscribeMetrics();
    });
    return () => {
      cancelled = true;
      unsubscribe?.();
    };
  }, []);
  useEventsMessage('Metrics', push);

  return { history, latest: history[history.length - 1], error: initial.error };
}

/** Speed history of the transfers hub (primed with `GetSpeedHistory`). */
function useSpeedHistory() {
  const [down, setDown] = useState<SpeedPointDto[]>([]);
  const [up, setUp] = useState<SpeedPointDto[]>([]);
  useEffect(() => {
    let cancelled = false;
    void getTransfersHub()
      .then((hub) => hub.getSpeedHistory())
      .then((h) => {
        if (cancelled) return;
        setDown(h.download.slice(-SPEED_CAPACITY));
        setUp(h.upload.slice(-SPEED_CAPACITY));
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, []);
  useTransfersMessage('SpeedHistoryPoint', (d, u) => {
    setDown((s) => [...s, d].slice(-SPEED_CAPACITY));
    setUp((s) => [...s, u].slice(-SPEED_CAPACITY));
  });
  return { down, up };
}

function MetricCard({ icon, title, value, sub, children, className }: { icon: ReactNode; title: string; value: ReactNode; sub?: ReactNode; children?: ReactNode; className?: string }) {
  return (
    <Card className={cn('flex flex-col gap-3 p-5', className)}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">
            <span className="text-accent-400 [&_svg]:size-4">{icon}</span>
            {title}
          </p>
          <p className="mt-1.5 text-2xl font-semibold tabular-nums tracking-tight text-fg">{value}</p>
          {sub && <p className="text-xs text-fg-muted">{sub}</p>}
        </div>
      </div>
      {children}
    </Card>
  );
}

const pct = (n: number | undefined) => `${(n ?? 0).toFixed(1)}%`;

function Resources() {
  const { history, latest: m, error } = useLiveMetrics();
  if (!m) {
    return error ? (
      <Card className="p-5 text-sm text-danger">{errorMessage(error)}</Card>
    ) : (
      <div className="grid gap-4 md:grid-cols-3">
        <Skeleton className="h-52" />
        <Skeleton className="h-52" />
        <Skeleton className="h-52" />
      </div>
    );
  }
  const diskTone = m.diskUsagePercent > 90 ? 'danger' : m.diskUsagePercent > 75 ? 'warning' : 'accent';
  const tempPercent = m.diskTotalBytes > 0 ? (m.tempFolderSizeBytes / m.diskTotalBytes) * 100 : 0;
  return (
    <div className="grid gap-4 md:grid-cols-3">
      <MetricCard icon={<Cpu />} title={t('CPU')} value={pct(m.systemCpuUsage)} sub={t('{count} cores · app {value}', { count: m.processorCount, value: pct(m.appCpuUsage) })}>
        <LineChart
          label={t('CPU usage')}
          capacity={CAPACITY}
          max={100}
          formatValue={pct}
          series={[
            { label: t('System'), values: history.map((h) => h.systemCpuUsage), color: ACCENT },
            { label: t('Application'), values: history.map((h) => h.appCpuUsage), color: INFO }
          ]}
        />
        <ChartLegend items={[{ label: t('System'), color: ACCENT, value: pct(m.systemCpuUsage) }, { label: t('Application'), color: INFO, value: pct(m.appCpuUsage) }]} />
      </MetricCard>
      <MetricCard icon={<MemoryStick />} title={t('Memory')} value={pct(m.memoryUsagePercent)} sub={t('{used} of {total} · {free} available', { used: formatBytes(m.usedMemoryBytes), total: formatBytes(m.totalMemoryBytes), free: formatBytes(m.availableMemoryBytes) })}>
        <LineChart
          label={t('Memory usage')}
          capacity={CAPACITY}
          max={100}
          formatValue={pct}
          series={[
            { label: t('System'), values: history.map((h) => h.memoryUsagePercent), color: ACCENT },
            { label: t('Application'), values: history.map((h) => (h.totalMemoryBytes > 0 ? (h.appMemoryBytes / h.totalMemoryBytes) * 100 : 0)), color: SUCCESS }
          ]}
        />
        <ChartLegend items={[{ label: t('System'), color: ACCENT, value: pct(m.memoryUsagePercent) }, { label: t('Application'), color: SUCCESS, value: formatBytes(m.appMemoryBytes) }]} />
      </MetricCard>
      <MetricCard icon={<HardDrive />} title={t('Disk')} value={pct(m.diskUsagePercent)} sub={t('{used} of {total} · {free} free', { used: formatBytes(m.diskUsedBytes), total: formatBytes(m.diskTotalBytes), free: formatBytes(m.diskFreeBytes) })}>
        <div className="mt-auto flex flex-col gap-3">
          <div>
            <div className="mb-1 flex justify-between text-xs text-fg-muted">
              <span>{t('Disk used')}</span>
              <span className="tabular-nums">{formatBytes(m.diskUsedBytes)}</span>
            </div>
            <Progress value={m.diskUsagePercent} tone={diskTone} aria-label={t('Disk used')} />
          </div>
          <div>
            <div className="mb-1 flex justify-between text-xs text-fg-muted">
              <span>{t('Temporary folder')}</span>
              <span className="tabular-nums">{formatBytes(m.tempFolderSizeBytes)}</span>
            </div>
            <Progress value={Math.max(tempPercent, m.tempFolderSizeBytes > 0 ? 1 : 0)} tone="warning" aria-label={t('Temporary folder')} />
          </div>
          {m.tempFolderPath && <p className="truncate font-mono text-[11px] text-fg-subtle" title={m.tempFolderPath}>{m.tempFolderPath}</p>}
        </div>
      </MetricCard>
    </div>
  );
}

function SpeedCard() {
  const { down, up } = useSpeedHistory();
  const summary = useRealtime((s) => s.transferSummary);
  return (
    <Card className="flex flex-col gap-3 p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">
          <Activity className="size-4 text-accent-400" /> {t('Transfer speed history')}
        </p>
        <ChartLegend
          items={[
            { label: t('Downloads'), color: ACCENT, value: summary?.downloadSpeed ?? down[down.length - 1]?.speedText },
            { label: t('Uploads'), color: WARNING, value: summary?.uploadSpeed ?? up[up.length - 1]?.speedText }
          ]}
        />
      </div>
      <LineChart
        label={t('Transfer speed history')}
        capacity={SPEED_CAPACITY}
        height={140}
        formatValue={(v) => `${formatBytes(v)}/s`}
        series={[
          { label: t('Downloads'), values: down.map((p) => p.bytesPerSecond), color: ACCENT },
          { label: t('Uploads'), values: up.map((p) => p.bytesPerSecond), color: WARNING }
        ]}
      />
      <div className="flex flex-wrap gap-4 text-xs text-fg-muted">
        <span className="flex items-center gap-1.5">
          <ArrowDownToLine className="size-3.5" /> {t('{count} downloading', { count: summary?.activeDownloads ?? 0 })}
        </span>
        <span className="flex items-center gap-1.5">
          <ArrowUpFromLine className="size-3.5" /> {t('{count} uploading', { count: summary?.activeUploads ?? 0 })}
        </span>
        <Link to="/transfers" className="ml-auto text-accent-400 hover:text-accent-300">
          {t('Open transfers')}
        </Link>
      </div>
    </Card>
  );
}

function AccountCard() {
  const status = useTelegramStatus();
  const user = status.data?.user;
  const name = [user?.firstName, user?.lastName].filter(Boolean).join(' ') || user?.username || '–';
  return (
    <Card className="flex flex-col gap-4 p-5">
      <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">
        <User className="size-4 text-accent-400" /> {t('Telegram account')}
      </p>
      {status.isPending ? (
        <Skeleton className="h-14" />
      ) : (
        <div className="flex items-center gap-3">
          <span className="flex size-12 items-center justify-center rounded-full bg-primary-soft text-lg font-semibold text-accent-300">{name.slice(0, 1).toUpperCase()}</span>
          <div className="min-w-0">
            <p className="truncate font-semibold text-fg">{name}</p>
            <p className="truncate text-xs text-fg-muted">{[user?.username && `@${user.username}`, user?.phone && `+${user.phone.replace(/^\+/, '')}`].filter(Boolean).join(' · ')}</p>
          </div>
        </div>
      )}
      {user && (
        <div className={cn('flex items-center gap-3 rounded-xl border p-3', user.isPremium ? 'border-warning/30 bg-warning-soft' : 'border-border bg-surface-2')}>
          <Crown className={cn('size-5', user.isPremium ? 'text-warning' : 'text-fg-subtle')} />
          <div className="min-w-0 flex-1 text-sm">
            <p className="font-medium text-fg">{user.isPremium ? t('Telegram Premium') : t('Standard account')}</p>
            <p className="text-xs text-fg-muted">{user.isPremium ? t('Files up to 4 GB per upload.') : t('Files up to 2 GB per upload. Premium raises it to 4 GB.')}</p>
          </div>
          <Badge tone={user.isPremium ? 'warning' : 'neutral'}>{user.isPremium ? '4 GB' : '2 GB'}</Badge>
        </div>
      )}
    </Card>
  );
}

function StorageCard() {
  const qc = useQueryClient();
  const metrics = useQuery({ queryKey: ['system', 'metrics'], queryFn: ({ signal }) => api.system.metrics(signal) });
  const cache = useQuery({ queryKey: ['system', 'image-cache'], queryFn: ({ signal }) => api.system.imageCache(signal) });
  const clearTemp = useMutation({
    mutationFn: () => api.local.clearCache(),
    onSuccess: () => {
      toast.success(t('Temporary files deleted'));
      void qc.invalidateQueries({ queryKey: ['system', 'metrics'] });
    },
    onError: (error) => toast.error(t('Could not clean the temporary files'), { description: errorMessage(error) })
  });
  const clearImages = useMutation({
    mutationFn: () => api.system.clearImageCache(),
    onSuccess: (info) => {
      qc.setQueryData(['system', 'image-cache'], info);
      toast.success(t('Image cache cleared'));
    },
    onError: (error) => toast.error(t('Could not clear the image cache'), { description: errorMessage(error) })
  });
  const temp = metrics.data?.tempFolderSizeBytes ?? 0;
  return (
    <Card className="flex flex-col divide-y divide-border">
      <div className="flex items-center gap-3 p-4">
        <span className="flex size-10 items-center justify-center rounded-xl bg-surface-2 text-accent-300">
          <FolderOpen className="size-5" />
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium text-fg">{t('Temporary files')}</p>
          <p className="text-xs text-fg-muted">{t('{size} · cached files and temporary data', { size: formatBytes(temp) })}</p>
        </div>
        <Button
          size="sm"
          variant="outline"
          icon={<Trash2 />}
          loading={clearTemp.isPending}
          disabled={temp === 0}
          onClick={async () => {
            if (await confirm({ title: t('Clean the temporary files?'), description: t('Media cached for playback is downloaded again when needed.'), confirmLabel: t('Clean') })) clearTemp.mutate();
          }}
        >
          {t('Clean')}
        </Button>
      </div>
      <div className="flex items-center gap-3 p-4">
        <span className="flex size-10 items-center justify-center rounded-xl bg-surface-2 text-accent-300">
          <Image className="size-5" />
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium text-fg">{t('Image cache')}</p>
          <p className="text-xs text-fg-muted">
            {cache.data ? t('{channels} channel images · {thumbs} thumbnails · {size}', { channels: cache.data.channelImages, thumbs: cache.data.thumbnails, size: cache.data.sizeText }) : t('Loading...')}
          </p>
        </div>
        <Button size="sm" variant="outline" icon={<Trash2 />} loading={clearImages.isPending} disabled={!cache.data || cache.data.sizeInBytes === 0} onClick={() => clearImages.mutate()}>
          {t('Clear')}
        </Button>
      </div>
      <FfmpegRow />
    </Card>
  );
}

function FfmpegRow() {
  const qc = useQueryClient();
  const ffmpeg = useQuery({ queryKey: ['system', 'ffmpeg'], queryFn: ({ signal }) => api.system.ffmpeg(false, signal) });
  const recheck = useMutation({ mutationFn: () => api.system.ffmpeg(true), onSuccess: (d) => qc.setQueryData(['system', 'ffmpeg'], d) });
  const f = ffmpeg.data;
  return (
    <div className="flex items-center gap-3 p-4">
      <span className="flex size-10 items-center justify-center rounded-xl bg-surface-2 text-accent-300">
        <Film className="size-5" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-fg">
          FFmpeg
          {f && (f.available ? <Badge tone="success" dot>{f.version ?? t('Available')}</Badge> : <Badge tone="danger" dot>{t('Not found')}</Badge>)}
        </p>
        <p className="text-xs text-fg-muted">
          {f ? (f.transcodingEnabled ? t('Video transcoding is enabled.') : t('Video transcoding is disabled.')) : t('Loading...')}{' '}
          <Link to="/settings/streaming" className="text-accent-400 hover:text-accent-300">
            {t('Settings')}
          </Link>
        </p>
      </div>
      <Button size="sm" variant="ghost" icon={<RefreshCw />} loading={recheck.isPending} onClick={() => recheck.mutate()}>
        {t('Check again')}
      </Button>
    </div>
  );
}

/** Restart button: confirms, then waits for the server to answer ping again. */
function RestartCard() {
  const qc = useQueryClient();
  const [phase, setPhase] = useState<'idle' | 'restarting' | 'waiting'>('idle');
  const restart = async () => {
    const ok = await confirm({
      title: t('Restart the server?'),
      description: t('Running transfers are interrupted. The server only comes back on its own when something restarts it (Docker restart policy, systemd, a Windows service...).'),
      confirmLabel: t('Restart'),
      destructive: true
    });
    if (!ok) return;
    setPhase('restarting');
    try {
      await api.system.restart();
    } catch (e) {
      setPhase('idle');
      toast.error(t('Could not restart the server'), { description: errorMessage(e) });
      return;
    }
    setPhase('waiting');
    await new Promise((r) => setTimeout(r, 4000));
    const deadline = Date.now() + 3 * 60_000;
    while (Date.now() < deadline) {
      try {
        await api.system.ping();
        setPhase('idle');
        toast.success(t('The server is back'));
        void qc.invalidateQueries();
        return;
      } catch {
        await new Promise((r) => setTimeout(r, 2000));
      }
    }
    setPhase('idle');
    toast.error(t('The server did not come back'), { description: t('Start it again by hand if it is not managed by Docker or a service.') });
  };
  return (
    <Card className="flex flex-wrap items-center gap-3 p-5">
      <span className="flex size-10 items-center justify-center rounded-xl bg-danger-soft text-danger">
        <Power className="size-5" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-fg">{t('Restart the server')}</p>
        <p className="text-xs text-fg-muted">
          {phase === 'waiting' ? t('Waiting for the server to come back...') : t('Useful after configuration changes or to resolve issues.')}
        </p>
      </div>
      <Button variant="danger" size="sm" icon={<Power />} loading={phase !== 'idle'} onClick={() => void restart()}>
        {phase === 'idle' ? t('Restart') : t('Restarting...')}
      </Button>
    </Card>
  );
}

export default function SystemPage() {
  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader title={t('System')} description={t('Server information, resources and live metrics.')} icon={<Activity />} />
      <Resources />
      <SpeedCard />
      <div className="grid gap-4 lg:grid-cols-2">
        <AccountCard />
        <VersionCard />
      </div>
      <div className="grid gap-4 lg:grid-cols-[1.4fr_1fr]">
        <StorageCard />
        <RestartCard />
      </div>
    </div>
  );
}
