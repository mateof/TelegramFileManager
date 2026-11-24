import type { ApiLibraryEpisodeDto, ApiLibraryFileDto, ApiLibraryItemDetailDto, ApiLibrarySeasonDto } from '@tfm/api-client';
import { useMutation } from '@tanstack/react-query';
import {
  Badge,
  Button,
  cn,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  IconButton,
  Skeleton,
  toast,
  Tooltip
} from '@tfm/ui';
import {
  ArrowLeft,
  Check,
  CheckCheck,
  CircleDashed,
  ExternalLink,
  FolderOpen,
  Lock,
  MoreHorizontal,
  Play,
  RefreshCw,
  RotateCcw,
  TriangleAlert,
  WandSparkles
} from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { containingFolderRoute } from '@/features/search/fileMedia';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { RatingBadge, WatchProgress } from '../components/Cards';
import { IdentifyDialog, type IdentifyTarget } from '../components/IdentifyDialog';
import { LibraryDisabled } from '../components/LibraryChrome';
import { LibraryImage } from '../components/LibraryImage';
import '../library.css';
import { episodeCode, episodeFile, formatRuntime, isLibraryDisabled, minutesLeft, progressPercent, versionLabel } from '../libraryModel';
import { playLibraryFile, primaryFile } from '../playback';
import { useInvalidateLibrary, useItem } from '../queries';

export default function LibraryItemPage() {
  const { id = '' } = useParams();
  const item = useItem(id);
  const navigate = useNavigate();
  const [identify, setIdentify] = useState<IdentifyTarget | null>(null);

  useEffect(() => {
    if (item.data) document.title = `${item.data.title} · TFM`;
  }, [item.data]);

  if (isLibraryDisabled(item.error)) return <LibraryDisabled />;
  if (item.isPending) return <ItemSkeleton />;
  if (item.isError) {
    return (
      <div className="mx-auto max-w-3xl p-6">
        <EmptyState
          icon={<TriangleAlert />}
          title={t('Could not load the title')}
          description={errorMessage(item.error)}
          action={
            <>
              <Button variant="secondary" onClick={() => void navigate('/library')}>
                {t('Back to the library')}
              </Button>
              <Button onClick={() => void item.refetch()}>{t('Retry')}</Button>
            </>
          }
        />
      </div>
    );
  }

  const data = item.data;
  return (
    <div className="tfm-library flex min-h-full shrink-0 flex-col pb-12">
      <ItemHeader item={data} onIdentify={() => setIdentify({ type: 'item', item: data })} />
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8 px-4 sm:px-6 lg:px-10">
        {data.kind === 'series' ? (
          <Seasons item={data} onIdentifyFile={(file) => setIdentify({ type: 'file', file })} />
        ) : (
          <Versions item={data} files={data.files} onIdentifyFile={(file) => setIdentify({ type: 'file', file })} />
        )}
      </div>
      <IdentifyDialog target={identify} onClose={() => setIdentify(null)} />
    </div>
  );
}

/** Release status of the metadata providers (TMDB / OMDb answer in English). */
function statusLabel(status: string | null): string | null {
  if (!status) return null;
  const labels: Record<string, string> = {
    Released: t('Released'),
    'Post Production': t('Post-production'),
    'In Production': t('In production'),
    Planned: t('Planned'),
    Rumored: t('Rumored'),
    Canceled: t('Cancelled'),
    Ended: t('Ended'),
    'Returning Series': t('Returning series'),
    Pilot: t('Pilot')
  };
  return labels[status] ?? status;
}

function ItemSkeleton() {
  return (
    <div className="flex flex-col">
      <Skeleton className="h-[26rem] w-full rounded-none" />
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-3 px-4 py-6 sm:px-10">
        <Skeleton className="h-6 w-60" />
        <Skeleton className="h-20 w-full" />
      </div>
    </div>
  );
}

function useItemMutations(item: ApiLibraryItemDetailDto) {
  const invalidate = useInvalidateLibrary();
  const watched = useMutation({
    mutationFn: ({ value, season }: { value: boolean; season?: number }) =>
      value ? api.library.markItemWatched(item.id, { season }) : api.library.markItemUnwatched(item.id, season),
    onSuccess: () => void invalidate(),
    onError: (error) => toast.error(t('Could not update the watched state'), { description: errorMessage(error) })
  });
  const refresh = useMutation({
    mutationFn: () => api.library.refreshItem(item.id),
    onSuccess: () => {
      toast.success(t('Metadata refreshed'));
      void invalidate();
    },
    onError: (error) => toast.error(t('Could not refresh the metadata'), { description: errorMessage(error) })
  });
  return { watched, refresh };
}

function ItemHeader({ item, onIdentify }: { item: ApiLibraryItemDetailDto; onIdentify: () => void }) {
  const { watched, refresh } = useItemMutations(item);
  const play = primaryFile(item);
  const inProgress = !!play?.watch && !play.watch.completed && play.watch.positionMs > 0;
  const isSeries = item.kind === 'series';
  const left = minutesLeft(play?.watch);
  const imdb = item.imdbId ?? item.externalIds?.['imdb'];
  const tmdb = item.externalIds?.['tmdb'];
  const meta = [item.year, formatRuntime(item.runtime), isSeries && item.seasonCount ? (item.seasonCount === 1 ? t('1 season') : t('{count} seasons', { count: item.seasonCount })) : null, statusLabel(item.status)].filter(Boolean);

  return (
    <section className="relative isolate overflow-hidden">
      <LibraryImage src={item.backdropUrl ?? item.posterUrl} alt="" size="w1280" eager className="absolute inset-0 -z-10 size-full" imgClassName="motion-safe:animate-[tfm-kenburns_30s_ease-out_both]" />
      <div className="absolute inset-0 -z-10 bg-gradient-to-r from-black/90 via-black/60 to-black/20" />
      <div className="absolute inset-0 -z-10 bg-gradient-to-t from-bg via-transparent to-transparent light:hidden" />
      {/* Light theme: only a thin fade, the white text needs the dark backdrop */}
      <div className="absolute inset-x-0 bottom-0 -z-10 hidden h-8 bg-gradient-to-t from-bg to-transparent light:block" />

      <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 pb-10 pt-5 sm:px-6 lg:px-10">
        <Link to={isSeries ? '/library/series' : '/library/movies'} className="inline-flex w-fit items-center gap-1.5 rounded-full bg-black/30 px-3 py-1.5 text-sm text-white/80 backdrop-blur-md hover:text-white">
          <ArrowLeft className="size-4" />
          {isSeries ? t('Series') : t('Movies')}
        </Link>
        <div className="flex flex-col gap-6 sm:flex-row sm:items-end">
          <LibraryImage src={item.posterUrl} alt={item.title} size="w500" eager className="hidden aspect-[2/3] w-44 shrink-0 rounded-2xl shadow-elevated ring-1 ring-white/10 sm:block lg:w-56" />
          <div className="flex min-w-0 animate-fade-in flex-col gap-3 text-white">
            <h1 className="text-3xl font-bold tracking-tight drop-shadow-lg sm:text-4xl lg:text-5xl">{item.title}</h1>
            {item.originalTitle && item.originalTitle !== item.title && <p className="-mt-2 text-sm text-white/60">{item.originalTitle}</p>}
            {item.tagline && <p className="text-base italic text-white/70">{item.tagline}</p>}
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-white/80">
              <RatingBadge rating={item.rating} className="text-sm" />
              {item.voteCount ? <span className="text-white/50">({item.voteCount.toLocaleString()})</span> : null}
              {meta.map((m, i) => (
                <span key={i}>{m}</span>
              ))}
              {item.locked && (
                <Tooltip content={t('Identified by hand: scans leave it alone')}>
                  <span className="inline-flex items-center gap-1 text-white/60">
                    <Lock className="size-3.5" />
                    {t('Locked')}
                  </span>
                </Tooltip>
              )}
            </div>
            {item.genres.length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {item.genres.map((g) => (
                  <Link key={g} to={`/library/all?genre=${encodeURIComponent(g)}`} className="rounded-full border border-white/15 bg-white/10 px-2.5 py-0.5 text-xs text-white/85 backdrop-blur-sm hover:bg-white/20">
                    {g}
                  </Link>
                ))}
              </div>
            )}
            {item.overview && <p className="max-w-3xl text-sm leading-relaxed text-white/85 sm:text-base">{item.overview}</p>}

            <div className="mt-2 flex flex-wrap items-center gap-3">
              {play && (
                <Button size="lg" onClick={() => playLibraryFile(play, item)} className="bg-white text-black hover:bg-white/90 [background-image:none]">
                  <Play className="fill-current" />
                  {inProgress ? t('Resume') : t('Play')}
                  {isSeries && play.season != null && <span className="text-black/60">{episodeCode(play.season, play.episode)}</span>}
                </Button>
              )}
              {play && inProgress && (
                <Button size="lg" variant="secondary" onClick={() => playLibraryFile(play, item, true)} className="border-white/20 bg-white/15 text-white backdrop-blur-md hover:bg-white/25">
                  <RotateCcw />
                  {t('From the start')}
                </Button>
              )}
              <Button
                size="lg"
                variant="secondary"
                loading={watched.isPending}
                onClick={() => watched.mutate({ value: !item.watch.completed })}
                className="border-white/20 bg-white/15 text-white backdrop-blur-md hover:bg-white/25"
              >
                {item.watch.completed ? <CheckCheck /> : <Check />}
                {item.watch.completed ? t('Watched') : t('Mark as watched')}
              </Button>
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <IconButton size="lg" variant="secondary" label={t('More actions')} className="border-white/20 bg-white/15 text-white backdrop-blur-md hover:bg-white/25">
                    <MoreHorizontal />
                  </IconButton>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start">
                  <DropdownMenuItem onSelect={onIdentify}>
                    <WandSparkles /> {t('Identify as another title')}
                  </DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => refresh.mutate()} disabled={refresh.isPending}>
                    <RefreshCw /> {t('Refresh metadata')}
                  </DropdownMenuItem>
                  {item.watch.completed || item.watch.inProgress ? (
                    <DropdownMenuItem onSelect={() => watched.mutate({ value: false })}>
                      <CircleDashed /> {t('Mark as not watched')}
                    </DropdownMenuItem>
                  ) : null}
                  {(imdb || tmdb) && <DropdownMenuSeparator />}
                  {imdb && (
                    <DropdownMenuItem asChild>
                      <a href={`https://www.imdb.com/title/${encodeURIComponent(imdb)}/`} target="_blank" rel="noreferrer">
                        <ExternalLink /> IMDb
                      </a>
                    </DropdownMenuItem>
                  )}
                  {tmdb && (
                    <DropdownMenuItem asChild>
                      <a href={`https://www.themoviedb.org/${isSeries ? 'tv' : 'movie'}/${encodeURIComponent(tmdb)}`} target="_blank" rel="noreferrer">
                        <ExternalLink /> TMDB
                      </a>
                    </DropdownMenuItem>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
            {play?.watch && inProgress && (
              <div className="flex max-w-sm items-center gap-3 text-xs text-white/70">
                <WatchProgress percent={progressPercent(play.watch)} className="flex-1 bg-white/20" />
                {left > 0 && <span>{t('{count} min left', { count: left })}</span>}
              </div>
            )}
            {isSeries && item.watch.episodesTotal > 0 && (
              <p className="text-xs text-white/60">{t('{watched} of {total} episodes watched', { watched: item.watch.episodesWatched, total: item.watch.episodesTotal })}</p>
            )}
          </div>
        </div>
      </div>
    </section>
  );
}

function FileMenu({ file, onIdentify }: { file: ApiLibraryFileDto; onIdentify: () => void }) {
  const invalidate = useInvalidateLibrary();
  const navigate = useNavigate();
  const toggle = useMutation({
    mutationFn: () => (file.watch?.completed ? api.library.markUnwatched(file.channelId, file.fileId) : api.library.markWatched(file.channelId, file.fileId)),
    onSuccess: () => void invalidate(),
    onError: (error) => toast.error(t('Could not update the watched state'), { description: errorMessage(error) })
  });
  const forget = useMutation({
    mutationFn: () => api.library.deleteWatch(file.channelId, file.fileId),
    onSuccess: () => void invalidate()
  });
  const ignore = useMutation({
    mutationFn: () => api.library.ignoreFile(file.channelId, file.fileId),
    onSuccess: () => {
      toast.success(t('File ignored'));
      void invalidate();
    },
    onError: (error) => toast.error(t('Could not ignore the file'), { description: errorMessage(error) })
  });
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <IconButton size="sm" label={t('File actions')}>
          <MoreHorizontal />
        </IconButton>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="max-w-80">
        <DropdownMenuLabel className="truncate">{file.fileName}</DropdownMenuLabel>
        <DropdownMenuItem onSelect={() => playLibraryFile(file, file.item)}>
          <Play /> {t('Play')}
        </DropdownMenuItem>
        {file.watch && file.watch.positionMs > 0 && !file.watch.completed && (
          <DropdownMenuItem onSelect={() => playLibraryFile(file, file.item, true)}>
            <RotateCcw /> {t('From the start')}
          </DropdownMenuItem>
        )}
        <DropdownMenuItem onSelect={() => toggle.mutate()}>
          {file.watch?.completed ? <CircleDashed /> : <Check />} {file.watch?.completed ? t('Mark as not watched') : t('Mark as watched')}
        </DropdownMenuItem>
        {file.watch && (
          <DropdownMenuItem onSelect={() => forget.mutate()}>
            <RotateCcw /> {t('Forget the progress')}
          </DropdownMenuItem>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={onIdentify}>
          <WandSparkles /> {t('Identify this file')}
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={() => ignore.mutate()}>
          <CircleDashed /> {t('Ignore this file')}
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={() => void navigate(containingFolderRoute(String(file.channelId), file.folderPath))}>
          <FolderOpen /> {t('Open containing folder')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function Versions({ item, files, onIdentifyFile }: { item: ApiLibraryItemDetailDto; files: ApiLibraryFileDto[]; onIdentifyFile: (f: ApiLibraryFileDto) => void }) {
  if (!files.length) return null;
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold tracking-tight text-fg">{files.length === 1 ? t('File') : t('Versions')}</h2>
      <ul className="flex flex-col gap-2">
        {files.map((f) => {
          const percent = progressPercent(f.watch);
          return (
            <li key={`${f.channelId}:${f.fileId}`} className="glass flex items-center gap-3 rounded-xl border border-border p-3 shadow-soft">
              <IconButton variant="soft" label={t('Play')} onClick={() => playLibraryFile(f, item)}>
                <Play className="fill-current" />
              </IconButton>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-fg" title={f.fileName}>
                  {versionLabel(f)}
                </p>
                <p className="truncate text-xs text-fg-subtle" title={`${f.folderPath}${f.fileName}`}>
                  {f.fileName}
                </p>
                {percent > 0 && !f.watch?.completed && <WatchProgress percent={percent} className="mt-1.5 max-w-xs bg-surface-3" />}
              </div>
              {f.watch?.completed && (
                <Badge tone="success">
                  <Check className="size-3" /> {t('Watched')}
                </Badge>
              )}
              {f.status === 'review' && <Badge tone="warning">{t('Review')}</Badge>}
              <FileMenu file={f} onIdentify={() => onIdentifyFile(f)} />
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function Seasons({ item, onIdentifyFile }: { item: ApiLibraryItemDetailDto; onIdentifyFile: (f: ApiLibraryFileDto) => void }) {
  const initialSeason = item.nextUp?.season ?? item.seasons[0]?.number ?? 1;
  const [selected, setSelected] = useState(initialSeason);
  const season = item.seasons.find((s) => s.number === selected) ?? item.seasons[0];
  const { watched } = useItemMutations(item);

  if (!season) return <EmptyState title={t('No episodes')} description={t('No episode of this series has a file yet.')} />;

  return (
    <section className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <div role="tablist" aria-label={t('Seasons')} className="flex gap-1.5 overflow-x-auto [scrollbar-width:none]">
          {item.seasons.map((s) => (
            <button
              key={s.number}
              role="tab"
              type="button"
              aria-selected={s.number === season.number}
              onClick={() => setSelected(s.number)}
              className={cn(
                'inline-flex h-9 shrink-0 items-center gap-2 rounded-full px-3.5 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring',
                s.number === season.number ? 'bg-fg text-bg shadow-soft' : 'bg-surface-2 text-fg-muted hover:text-fg'
              )}
            >
              {s.name || (s.number === 0 ? t('Specials') : t('Season {n}', { n: s.number }))}
              <span className="text-xs opacity-60">
                {s.episodesWatched}/{s.episodes.length}
              </span>
            </button>
          ))}
        </div>
        <div className="ml-auto">
          <SeasonWatchedButton season={season} pending={watched.isPending} onToggle={(value) => watched.mutate({ value, season: season.number })} />
        </div>
      </div>
      {season.overview && <p className="max-w-3xl text-sm text-fg-muted">{season.overview}</p>}
      <ol className="flex flex-col gap-3">
        {season.episodes.map((ep) => (
          <EpisodeRow key={`${ep.season}-${ep.number}`} item={item} ep={ep} isNext={item.nextUp?.season === ep.season && item.nextUp.number === ep.number} onIdentifyFile={onIdentifyFile} />
        ))}
      </ol>
    </section>
  );
}

function SeasonWatchedButton({ season, pending, onToggle }: { season: ApiLibrarySeasonDto; pending: boolean; onToggle: (value: boolean) => void }) {
  const all = season.episodes.length > 0 && season.episodesWatched >= season.episodes.length;
  return (
    <Button size="sm" variant="secondary" loading={pending} icon={all ? <CircleDashed /> : <CheckCheck />} onClick={() => onToggle(!all)}>
      {all ? t('Mark season as not watched') : t('Mark season as watched')}
    </Button>
  );
}

function EpisodeRow({ item, ep, isNext, onIdentifyFile }: { item: ApiLibraryItemDetailDto; ep: ApiLibraryEpisodeDto; isNext: boolean; onIdentifyFile: (f: ApiLibraryFileDto) => void }) {
  const file = episodeFile(ep);
  const percent = ep.completed ? 100 : progressPercent(ep.watch);
  return (
    <li
      className={cn(
        'group glass flex flex-col gap-3 rounded-2xl border border-border p-3 shadow-soft transition-colors sm:flex-row sm:items-start',
        isNext && 'border-accent-500/40 ring-1 ring-accent-500/20'
      )}
    >
      <button
        type="button"
        disabled={!file}
        onClick={() => file && playLibraryFile(file, item)}
        className="relative w-full shrink-0 overflow-hidden rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-ring sm:w-56"
        aria-label={t('Play episode {code}', { code: episodeCode(ep.season, ep.number) })}
      >
        <LibraryImage src={ep.stillUrl ?? item.backdropUrl} alt="" size="w300" size2x="w780" className="aspect-video w-full" imgClassName="transition-transform duration-500 group-hover:scale-105" />
        <span className="absolute inset-0 flex items-center justify-center bg-black/0 transition-colors group-hover:bg-black/35">
          <span className="flex size-10 scale-90 items-center justify-center rounded-full bg-white/90 text-black opacity-0 shadow-elevated transition-all group-hover:scale-100 group-hover:opacity-100">
            <Play className="size-4 translate-x-px fill-current" />
          </span>
        </span>
        {ep.completed && (
          <span className="absolute right-2 top-2 flex size-6 items-center justify-center rounded-full bg-primary text-on-accent shadow-soft">
            <Check className="size-3.5" strokeWidth={3} />
          </span>
        )}
        <span className="absolute inset-x-2 bottom-2">
          <WatchProgress percent={percent} />
        </span>
      </button>
      <div className="min-w-0 flex-1">
        <div className="flex items-start gap-2">
          <div className="min-w-0 flex-1">
            <p className="text-xs font-semibold uppercase tracking-wide text-accent-300">
              {episodeCode(ep.season, ep.number)}
              {isNext && <span className="ml-2 text-fg-subtle normal-case tracking-normal">{t('Up next')}</span>}
            </p>
            <h3 className="truncate font-medium text-fg">{ep.title ?? t('Episode {n}', { n: ep.number })}</h3>
            <p className="flex flex-wrap gap-x-3 text-xs text-fg-subtle">
              {ep.airDate && <span>{new Date(ep.airDate).toLocaleDateString()}</span>}
              {ep.runtime ? <span>{formatRuntime(ep.runtime)}</span> : null}
              <RatingBadge rating={ep.rating} />
              {ep.files.length > 1 && <span>{t('{count} versions', { count: ep.files.length })}</span>}
            </p>
          </div>
          {file && <FileMenu file={file} onIdentify={() => onIdentifyFile(file)} />}
        </div>
        {ep.overview && <p className="mt-1.5 line-clamp-3 text-sm text-fg-muted">{ep.overview}</p>}
        {ep.files.length > 1 && (
          <div className="mt-2 flex flex-wrap gap-1.5">
            {ep.files.map((f) => (
              <Button key={`${f.channelId}:${f.fileId}`} size="xs" variant="secondary" icon={<Play />} onClick={() => playLibraryFile(f, item)} title={f.fileName}>
                {versionLabel(f)}
              </Button>
            ))}
          </div>
        )}
      </div>
    </li>
  );
}
