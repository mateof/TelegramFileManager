import type { ApiChannelDetailDto } from '@tfm/api-client';
import { useQueryClient } from '@tanstack/react-query';
import {
  Avatar,
  Badge,
  Button,
  Card,
  confirm,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  IconButton,
  Popover,
  PopoverContent,
  PopoverTrigger,
  Progress,
  Skeleton,
  toast
} from '@tfm/ui';
import { BarChart3, Database, DatabaseZap, FileDown, FileUp, MessageSquare, MoreHorizontal, Network, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { downloadUrls } from '@/features/files/browser';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { queryKeys } from '@/lib/queryKeys';
import { useRefreshProgress } from '../lib/useRefreshProgress';
import { ImportIndexDialog, WebDavDialog } from './ChannelDialogs';
import { RefreshDialog } from './RefreshDialog';

function Stat({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-xl border border-border bg-surface-2/50 px-3 py-2">
      <p className="text-[11px] font-medium uppercase tracking-wider text-fg-subtle">{label}</p>
      <p className="text-base font-semibold tabular-nums text-fg">{value}</p>
    </div>
  );
}

/** Statistics card of the channel index. */
export function ChannelStatsCard({ detail }: { detail: ApiChannelDetailDto }) {
  return (
    <Card className="grid grid-cols-2 gap-2 border-0 p-0 shadow-none sm:grid-cols-3">
      <Stat label={t('Files')} value={detail.fileCount.toLocaleString()} />
      <Stat label={t('Folders')} value={detail.folderCount.toLocaleString()} />
      <Stat label={t('Total size')} value={detail.totalSizeText || formatBytes(detail.totalSize)} />
      <Stat label={t('Audio')} value={detail.audioCount.toLocaleString()} />
      <Stat label={t('Video')} value={detail.videoCount.toLocaleString()} />
      <Stat label={t('Photos')} value={detail.photoCount.toLocaleString()} />
      <Stat label={t('Documents')} value={detail.documentCount.toLocaleString()} />
    </Card>
  );
}

/** Channel title bar: avatar, stats, live refresh state and the channel tools. */
export function ChannelHeader({ channelId, detail, loading }: { channelId: string; detail?: ApiChannelDetailDto; loading: boolean }) {
  const qc = useQueryClient();
  const { progress } = useRefreshProgress(channelId);
  const [refreshOpen, setRefreshOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [webdavOpen, setWebdavOpen] = useState(false);
  const name = detail?.name ?? '';
  const indexed = detail?.hasDatabase ?? false;

  const deleteDatabase = async () => {
    const ok = await confirm({
      title: t('Delete the index of "{name}"?', { name }),
      description: t('The app forgets the folders and files of this channel. Nothing is deleted from Telegram; a refresh rebuilds it.'),
      confirmLabel: t('Delete index'),
      destructive: true
    });
    if (!ok) return;
    try {
      await api.channels.deleteDatabase(channelId);
      toast.success(t('Index deleted'));
      void qc.invalidateQueries({ queryKey: queryKeys.channel(channelId) });
      void qc.invalidateQueries({ queryKey: queryKeys.channels });
    } catch (error) {
      toast.error(t('Could not delete the index'), { description: errorMessage(error) });
    }
  };

  const fetched = progress?.filters.reduce((s, f) => s + f.fetched, 0) ?? 0;
  const total = progress?.filters.reduce((s, f) => s + (f.total ?? 0), 0) ?? 0;

  return (
    <div className="flex shrink-0 flex-wrap items-center gap-3 border-b border-border px-3 py-2.5 sm:px-4">
      {loading ? (
        <>
          <Skeleton className="size-10 rounded-xl" />
          <Skeleton className="h-5 w-48" />
        </>
      ) : (
        <>
          <Avatar src={api.channels.imageUrl(channelId)} name={name || channelId} square />
          <div className="min-w-0 flex-1">
            <h1 className="truncate text-base font-semibold tracking-tight text-fg sm:text-lg">{name}</h1>
            <p className="truncate text-xs text-fg-muted">
              {indexed
                ? t('{files} files · {size}', { files: (detail?.fileCount ?? 0).toLocaleString(), size: detail?.totalSizeText ?? '' })
                : t('Not indexed yet')}
            </p>
          </div>
        </>
      )}

      {progress && (
        <button
          type="button"
          onClick={() => setRefreshOpen(true)}
          className="flex min-w-40 items-center gap-2 rounded-xl border border-accent-500/30 bg-primary-soft px-2.5 py-1.5 text-left"
        >
          <RefreshCw className="size-4 shrink-0 animate-spin text-accent-300" />
          <span className="flex min-w-0 flex-1 flex-col gap-1">
            <span className="truncate text-xs font-medium text-fg">
              {progress.phase === 'indexing' ? t('Indexing {done}/{total}', { done: progress.indexed, total: progress.newFiles }) : t('Refreshing...')}
            </span>
            <Progress
              size="xs"
              value={progress.phase === 'indexing' ? (progress.newFiles ? (progress.indexed / progress.newFiles) * 100 : undefined) : total ? (fetched / total) * 100 : undefined}
              aria-label={t('Refresh progress')}
            />
          </span>
        </button>
      )}

      <div className="flex items-center gap-1">
        {indexed && !progress && (
          <Button size="sm" variant="secondary" icon={<RefreshCw />} onClick={() => setRefreshOpen(true)} disabled={detail?.canRefresh === false}>
            <span className="hidden sm:inline">{t('Refresh')}</span>
          </Button>
        )}
        <Button size="sm" variant="ghost" icon={<MessageSquare />} asChild>
          <Link to={`/chat/${channelId}`}>
            <span className="hidden sm:inline">{t('Messages')}</span>
          </Link>
        </Button>
        {indexed && detail && (
          <Popover>
            <PopoverTrigger asChild>
              <IconButton label={t('Statistics')} size="sm">
                <BarChart3 />
              </IconButton>
            </PopoverTrigger>
            <PopoverContent align="end" className="w-[min(24rem,92vw)]">
              <p className="mb-2 text-sm font-semibold text-fg">{t('Statistics')}</p>
              <ChannelStatsCard detail={detail} />
              {detail.isRefreshing && (
                <Badge tone="accent" dot className="mt-2">
                  {t('Refreshing...')}
                </Badge>
              )}
            </PopoverContent>
          </Popover>
        )}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <IconButton label={t('Channel tools')} size="sm">
              <MoreHorizontal />
            </IconButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="min-w-56">
            {indexed && (
              <DropdownMenuItem icon={<RefreshCw />} onSelect={() => setRefreshOpen(true)}>
                {t('Refresh index...')}
              </DropdownMenuItem>
            )}
            {indexed && (
              <DropdownMenuItem
                icon={<FileDown />}
                onSelect={() => void downloadUrls([{ url: api.files.exportUrl(channelId), name: `${name || channelId}.json` }])}
              >
                {t('Export index')}
              </DropdownMenuItem>
            )}
            <DropdownMenuItem icon={<FileUp />} onSelect={() => setImportOpen(true)}>
              {t('Import index...')}
            </DropdownMenuItem>
            <DropdownMenuItem icon={<Network />} onSelect={() => setWebdavOpen(true)}>
              {t('WebDAV address')}
            </DropdownMenuItem>
            {indexed && (
              <>
                <DropdownMenuSeparator />
                <DropdownMenuItem icon={<DatabaseZap />} destructive onSelect={() => void deleteDatabase()}>
                  {t('Delete index')}
                </DropdownMenuItem>
              </>
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <RefreshDialog channelId={channelId} open={refreshOpen} onOpenChange={setRefreshOpen} />
      <ImportIndexDialog channelId={channelId} open={importOpen} onOpenChange={setImportOpen} />
      <WebDavDialog channelId={channelId} open={webdavOpen} onOpenChange={setWebdavOpen} />
    </div>
  );
}

/** Shown instead of the file manager when the channel has no index. */
export function NotIndexed({ channelId, name }: { channelId: string; name: string }) {
  const qc = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [refreshOpen, setRefreshOpen] = useState(false);
  const create = async () => {
    setBusy(true);
    try {
      await api.channels.createDatabase(channelId);
      await qc.invalidateQueries({ queryKey: queryKeys.channel(channelId) });
      void qc.invalidateQueries({ queryKey: queryKeys.channels });
      setRefreshOpen(true);
    } catch (error) {
      toast.error(t('Could not create the index'), { description: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-center">
      <div className="flex size-14 items-center justify-center rounded-2xl border border-border bg-surface-2 text-fg-muted shadow-soft">
        <Database className="size-6" />
      </div>
      <h2 className="text-base font-semibold text-fg">{t('"{name}" is not indexed yet', { name })}</h2>
      <p className="max-w-md text-sm text-fg-muted">{t('Create its index and refresh it to browse, play and download its files here.')}</p>
      <Button icon={<DatabaseZap />} loading={busy} onClick={() => void create()}>
        {t('Create index')}
      </Button>
      <RefreshDialog channelId={channelId} open={refreshOpen} onOpenChange={setRefreshOpen} />
    </div>
  );
}
