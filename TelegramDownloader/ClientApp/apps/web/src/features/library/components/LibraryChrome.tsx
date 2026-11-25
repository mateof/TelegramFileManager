import type { ApiLibraryItemDto, ApiLibraryScanStateDto } from '@tfm/api-client';
import { Badge, Button, Card, cn, EmptyState, Progress } from '@tfm/ui';
import { Clapperboard, Film, Info, Play, RefreshCw, ScanSearch, Settings, Square, Tv, WandSparkles } from 'lucide-react';
import { NavLink } from 'react-router';
import { t } from '@/i18n';
import { formatRuntime, scanPercent } from '../libraryModel';
import { useScanActions, useScanState } from '../queries';
import { RatingBadge } from './Cards';
import { LibraryImage } from './LibraryImage';

/** Tabs of the library (home, movies, series, review). */
export function LibraryNav({ reviewCount, className }: { reviewCount?: number; className?: string }) {
  const tabs = [
    { to: '/library', label: t('Home'), icon: Clapperboard, end: true },
    { to: '/library/movies', label: t('Movies'), icon: Film },
    { to: '/library/series', label: t('Series'), icon: Tv },
    { to: '/library/review', label: t('Needs review'), icon: WandSparkles, count: reviewCount }
  ];
  return (
    <nav aria-label={t('Library')} className={cn('flex gap-1 overflow-x-auto [scrollbar-width:none]', className)}>
      {tabs.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          end={tab.end}
          className={({ isActive }) =>
            cn(
              'inline-flex h-9 shrink-0 items-center gap-2 rounded-full px-3.5 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring [&_svg]:size-4',
              isActive ? 'bg-fg text-bg shadow-soft' : 'glass text-fg-muted hover:text-fg'
            )
          }
        >
          <tab.icon />
          {tab.label}
          {!!tab.count && <Badge tone="warning" className="px-1.5 py-0 text-[10px]">{tab.count}</Badge>}
        </NavLink>
      ))}
    </nav>
  );
}

/** Shown when the API answers `503 library_disabled`. */
export function LibraryDisabled() {
  return (
    <div className="mx-auto w-full max-w-3xl p-4 sm:p-8">
      <Card glass className="overflow-hidden">
        <div className="h-32 bg-gradient-to-br from-accent-600/40 via-accent-900/30 to-transparent" aria-hidden />
        <EmptyState
          className="-mt-16"
          icon={<Clapperboard />}
          title={t('The media library is switched off')}
          description={t('Turn it on and add a metadata provider key (TMDB or OMDb) to see your movies and series with posters, overviews and progress.')}
          action={
            <Button asChild>
              <NavLink to="/settings/library">
                <Settings />
                {t('Library settings')}
              </NavLink>
            </Button>
          }
        />
      </Card>
    </div>
  );
}

function scanSummary(s: ApiLibraryScanStateDto) {
  return t('{seen} files seen · {matched} matched · {review} to review · {unmatched} unmatched', {
    seen: s.filesSeen,
    matched: s.matched,
    review: s.review,
    unmatched: s.unmatched
  });
}

/** Live scan state (LibraryScan events) with start / cancel. */
export function ScanStatus({ compact }: { compact?: boolean }) {
  const scan = useScanState();
  const { start, cancel } = useScanActions();
  const s = scan.data;
  const running = !!s?.running;

  if (compact && !running) {
    return (
      <Button size="sm" variant="secondary" icon={<ScanSearch />} loading={start.isPending} onClick={() => start.mutate(false)}>
        {t('Scan')}
      </Button>
    );
  }

  return (
    <Card glass className={cn('flex flex-col gap-3 p-4', running && 'border-accent-500/30')} aria-live="polite">
      <div className="flex flex-wrap items-center gap-3">
        <span className={cn('flex size-9 items-center justify-center rounded-xl', running ? 'bg-primary-soft text-accent-300' : 'bg-surface-2 text-fg-muted')}>
          <RefreshCw className={cn('size-4', running && 'animate-spin')} />
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-fg">
            {running ? t('Scanning the library') : s?.finishedAt ? t('Last scan') : t('Never scanned')}
          </p>
          <p className="truncate text-xs text-fg-subtle">
            {running
              ? s?.filesToProcess
                ? t('Identifying files: {done}/{total}', { done: s.filesProcessed ?? 0, total: s.filesToProcess })
                : s?.currentChannel
                ? t('Channel {done}/{total}: {name}', { done: s.channelsScanned, total: s.channelsTotal, name: s.currentChannel })
                : t('Preparing...')
              : s?.finishedAt
                ? `${new Date(s.finishedAt).toLocaleString()}${s.cancelled ? ` · ${t('Cancelled')}` : ''}`
                : t('Scan your channels to build the library.')}
          </p>
        </div>
        {running ? (
          <Button size="sm" variant="secondary" icon={<Square />} loading={cancel.isPending} onClick={() => cancel.mutate()}>
            {t('Cancel')}
          </Button>
        ) : (
          <div className="flex gap-2">
            <Button size="sm" icon={<ScanSearch />} loading={start.isPending} onClick={() => start.mutate(false)}>
              {t('Scan now')}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => start.mutate(true)} disabled={start.isPending} title={t('Re-identify files matched automatically')}>
              {t('Full rescan')}
            </Button>
          </div>
        )}
      </div>
      {running && <Progress value={scanPercent(s)} aria-label={t('Scan progress')} />}
      {s && (running || s.finishedAt) && <p className="text-xs text-fg-muted">{scanSummary(s)}</p>}
      {s?.error && <p className="text-xs text-danger">{s.error}</p>}
    </Card>
  );
}

/** Big backdrop with the featured item of the home page. */
export function Hero({ item, onPlay }: { item: ApiLibraryItemDto; onPlay?: () => void }) {
  const meta = [item.year, item.kind === 'series' ? t('Series') : formatRuntime(item.runtime), item.genres.slice(0, 3).join(' · ')].filter(Boolean);
  return (
    <section className="relative isolate overflow-hidden">
      <LibraryImage src={item.backdropUrl ?? item.posterUrl} alt="" size="w1280" eager className="absolute inset-0 -z-10 h-full w-full" imgClassName="motion-safe:animate-[tfm-kenburns_30s_ease-out_both]" />
      <div className="absolute inset-0 -z-10 bg-gradient-to-r from-black/85 via-black/45 to-transparent" />
      <div className="absolute inset-0 -z-10 bg-gradient-to-t from-bg via-bg/10 to-transparent light:hidden" />
      {/* Light theme: only a thin fade, the white text needs the dark backdrop */}
      <div className="absolute inset-x-0 bottom-0 -z-10 hidden h-8 bg-gradient-to-t from-bg to-transparent light:block" />
      <div className="flex min-h-[22rem] flex-col justify-end gap-4 px-4 pb-10 pt-24 sm:min-h-[28rem] sm:px-6 lg:min-h-[32rem] lg:px-10">
        <div className="flex max-w-2xl animate-fade-in flex-col gap-3 text-white">
          <span className="text-xs font-semibold uppercase tracking-[0.2em] text-white/60">
            {item.watch?.inProgress ? t('Continue watching') : t('Featured')}
          </span>
          <h1 className="text-3xl font-bold tracking-tight drop-shadow-lg sm:text-5xl">{item.title}</h1>
          <div className="flex flex-wrap items-center gap-3 text-sm text-white/80">
            <RatingBadge rating={item.rating} className="text-sm" />
            {meta.map((m, i) => (
              <span key={i}>{m}</span>
            ))}
          </div>
          {item.overview && <p className="line-clamp-3 max-w-xl text-sm text-white/80 sm:text-base">{item.overview}</p>}
          <div className="mt-2 flex flex-wrap gap-3">
            {onPlay && (
              <Button size="lg" onClick={onPlay} className="bg-white text-black hover:bg-white/90 [background-image:none]">
                <Play className="fill-current" />
                {item.watch?.inProgress ? t('Resume') : t('Play')}
              </Button>
            )}
            <Button size="lg" variant="secondary" asChild className="border-white/20 bg-white/15 text-white backdrop-blur-md hover:bg-white/25">
              <NavLink to={`/library/items/${encodeURIComponent(item.id)}`}>
                <Info />
                {t('More info')}
              </NavLink>
            </Button>
          </div>
        </div>
      </div>
    </section>
  );
}
