import type { ApiLibraryItemDto } from '@tfm/api-client';
import { cn, IconButton, Skeleton } from '@tfm/ui';
import { Check, ChevronLeft, ChevronRight, Play, Star } from 'lucide-react';
import { useRef, type CSSProperties, type ReactNode } from 'react';
import { Link } from 'react-router';
import { t } from '@/i18n';
import { formatClock, minutesLeft, type ContinueEntry } from '../libraryModel';
import { playLibraryFile } from '../playback';
import { LibraryImage } from './LibraryImage';

/** Thin progress bar at the bottom of a card. */
export function WatchProgress({ percent, className }: { percent: number; className?: string }) {
  if (percent <= 0) return null;
  return (
    <div
      className={cn('h-1 w-full overflow-hidden rounded-full bg-black/40', className)}
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(percent)}
      aria-label={t('Watched')}
    >
      <div className="h-full rounded-full bg-primary shadow-glow" style={{ width: `${percent}%` }} />
    </div>
  );
}

export function RatingBadge({ rating, className }: { rating: number | null | undefined; className?: string }) {
  if (!rating) return null;
  return (
    <span className={cn('inline-flex items-center gap-1 text-xs font-semibold text-warning', className)}>
      <Star className="size-3.5 fill-current" aria-hidden />
      {rating.toFixed(1)}
    </span>
  );
}

/** Poster card of a movie or series. */
export function PosterCard({ item, className, style }: { item: ApiLibraryItemDto; className?: string; style?: CSSProperties }) {
  const watch = item.watch;
  const percent =
    item.kind === 'series' && watch?.episodesTotal
      ? (watch.episodesWatched / watch.episodesTotal) * 100
      : watch?.inProgress
        ? watch.progress * 100
        : 0;
  return (
    <Link
      to={`/library/items/${encodeURIComponent(item.id)}`}
      className={cn('group flex flex-col gap-2 rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-ring', className)}
      style={style}
      title={item.title}
    >
      <div className="relative overflow-hidden rounded-xl shadow-soft ring-1 ring-white/5 transition-[transform,box-shadow] duration-300 ease-out-soft group-hover:-translate-y-1 group-hover:shadow-elevated group-focus-visible:-translate-y-1">
        <LibraryImage src={item.posterUrl} alt={item.title} size="w342" className="aspect-[2/3] w-full" imgClassName="transition-transform duration-500 group-hover:scale-105" />
        <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-black/70 via-transparent to-transparent opacity-0 transition-opacity duration-300 group-hover:opacity-100" />
        {watch?.completed && (
          <span className="absolute right-2 top-2 flex size-6 items-center justify-center rounded-full bg-primary text-on-accent shadow-soft" title={t('Watched')}>
            <Check className="size-3.5" strokeWidth={3} />
          </span>
        )}
        {item.kind === 'series' && item.seasonCount > 0 && (
          <span className="absolute left-2 top-2 rounded-md bg-black/60 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-white backdrop-blur-sm">
            {item.seasonCount === 1 ? t('1 season') : t('{count} seasons', { count: item.seasonCount })}
          </span>
        )}
        <div className="absolute inset-x-2 bottom-2">
          <WatchProgress percent={percent} />
        </div>
      </div>
      <div className="min-w-0 px-0.5">
        <p className="truncate text-sm font-medium text-fg">{item.title}</p>
        <p className="flex items-center gap-2 text-xs text-fg-subtle">
          {item.year && <span>{item.year}</span>}
          <RatingBadge rating={item.rating} />
        </p>
      </div>
    </Link>
  );
}

export function PosterSkeleton({ className }: { className?: string }) {
  return (
    <div className={cn('flex flex-col gap-2', className)}>
      <Skeleton className="aspect-[2/3] w-full rounded-xl" />
      <Skeleton className="h-3.5 w-3/4" />
    </div>
  );
}

/** Backdrop card of "continue watching": plays straight away. */
export function ContinueCard({ entry }: { entry: ContinueEntry }) {
  const left = minutesLeft(entry.file.watch);
  return (
    <div className="group relative w-72 shrink-0 snap-start sm:w-80">
      <button
        type="button"
        onClick={() => playLibraryFile(entry.file)}
        className="relative block w-full overflow-hidden rounded-xl text-left shadow-soft ring-1 ring-white/5 outline-none transition-[transform,box-shadow] duration-300 ease-out-soft hover:-translate-y-1 hover:shadow-elevated focus-visible:ring-2 focus-visible:ring-ring"
        aria-label={t('Resume {title}', { title: entry.title })}
      >
        <LibraryImage src={entry.imageUrl} alt="" size="w780" className="aspect-video w-full" />
        <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-transparent" />
        <span className="absolute left-1/2 top-1/2 flex size-12 -translate-x-1/2 -translate-y-1/2 scale-90 items-center justify-center rounded-full bg-white/90 text-black opacity-0 shadow-elevated transition-all duration-300 group-hover:scale-100 group-hover:opacity-100">
          <Play className="size-5 translate-x-px fill-current" />
        </span>
        <div className="absolute inset-x-3 bottom-2.5 flex flex-col gap-1.5">
          <div className="flex items-end justify-between gap-2 text-white">
            <span className="min-w-0">
              <span className="block truncate text-sm font-semibold drop-shadow">{entry.title}</span>
              {entry.subtitle && <span className="block text-xs text-white/70">{entry.subtitle}</span>}
            </span>
            {entry.file.watch && (
              <span className="shrink-0 text-[11px] text-white/70">
                {left ? t('{count} min left', { count: left }) : formatClock(entry.file.watch.positionMs)}
              </span>
            )}
          </div>
          <WatchProgress percent={entry.percent} />
        </div>
      </button>
      {entry.itemId && (
        <Link
          to={`/library/items/${encodeURIComponent(entry.itemId)}`}
          className="mt-1.5 block truncate px-0.5 text-xs text-fg-subtle hover:text-fg-muted"
        >
          {t('Details')}
        </Link>
      )}
    </div>
  );
}

/** Horizontal, snap-scrolling row with a title and arrow buttons. */
export function Row({ title, action, children, loading }: { title: ReactNode; action?: ReactNode; children: ReactNode; loading?: boolean }) {
  const ref = useRef<HTMLDivElement>(null);
  const scroll = (dir: 1 | -1) => {
    const el = ref.current;
    if (el) el.scrollBy({ left: dir * el.clientWidth * 0.85, behavior: 'smooth' });
  };
  return (
    <section className="group/row flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3 px-4 sm:px-6 lg:px-10">
        <h2 className="text-lg font-semibold tracking-tight text-fg">{title}</h2>
        <div className="flex items-center gap-1">
          {action}
          <IconButton size="sm" label={t('Scroll left')} onClick={() => scroll(-1)} className="hidden md:inline-flex">
            <ChevronLeft />
          </IconButton>
          <IconButton size="sm" label={t('Scroll right')} onClick={() => scroll(1)} className="hidden md:inline-flex">
            <ChevronRight />
          </IconButton>
        </div>
      </div>
      <div
        ref={ref}
        className="flex snap-x snap-mandatory scroll-px-4 gap-4 overflow-x-auto px-4 pb-3 pt-1 [scrollbar-width:none] sm:scroll-px-6 sm:px-6 lg:scroll-px-10 lg:px-10 [&::-webkit-scrollbar]:hidden"
        aria-busy={loading || undefined}
      >
        {children}
      </div>
    </section>
  );
}

/** Poster cards inside a row keep a fixed width. */
export const ROW_POSTER = 'w-32 shrink-0 snap-start sm:w-40 lg:w-44';
