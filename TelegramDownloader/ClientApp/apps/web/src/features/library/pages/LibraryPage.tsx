import type { ApiLibraryGenreDto, ApiLibraryItemDto } from '@tfm/api-client';
import { Button, EmptyState } from '@tfm/ui';
import { ChevronRight, Clapperboard, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo } from 'react';
import { Link } from 'react-router';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';
import { ContinueCard, PosterCard, PosterSkeleton, Row, ROW_POSTER } from '../components/Cards';
import { Hero, LibraryDisabled, LibraryNav, ScanStatus } from '../components/LibraryChrome';
import '../library.css';
import { continueEntries, genreRows, isLibraryDisabled, pickFeatured } from '../libraryModel';
import { playItem } from '../playback';
import { useContinueWatching, useGenres, useItemsRow, useLibraryStats, useRecent } from '../queries';

export default function LibraryPage() {
  const stats = useLibraryStats();
  const enabled = stats.data?.enabled !== false;
  const recent = useRecent(undefined, enabled);
  const recentMovies = useRecent('movie', enabled);
  const recentSeries = useRecent('series', enabled);
  const cont = useContinueWatching();
  const genres = useGenres(undefined, enabled);

  useEffect(() => {
    document.title = `${t('Library')} · TFM`;
  }, []);

  const entries = useMemo(() => continueEntries(cont.data ?? []), [cont.data]);
  const featured = useMemo(() => {
    const inProgress = (cont.data ?? []).map((f) => f.item).filter((i): i is ApiLibraryItemDto => !!i && !!i.backdropUrl);
    return pickFeatured([...inProgress, ...(recent.data ?? [])]);
  }, [cont.data, recent.data]);

  if (stats.data?.enabled === false || isLibraryDisabled(recent.error) || isLibraryDisabled(stats.error)) {
    return (
      <div className="tfm-library">
        <LibraryDisabled />
      </div>
    );
  }

  if (recent.isError && !recent.data) {
    return (
      <div className="tfm-library mx-auto max-w-3xl p-6">
        <EmptyState
          icon={<TriangleAlert />}
          title={t('Could not load the library')}
          description={errorMessage(recent.error)}
          action={<Button onClick={() => void recent.refetch()}>{t('Retry')}</Button>}
        />
      </div>
    );
  }

  const empty = recent.isSuccess && recent.data.length === 0 && entries.length === 0;
  const reviewCount = stats.data?.files?.['review'] ?? 0;

  return (
    <div className="tfm-library flex min-h-full flex-col pb-10">
      <div className="relative">
        {featured ? (
          <Hero item={featured} onPlay={() => void playItem(featured.id)} />
        ) : (
          <div className="h-40 bg-gradient-to-b from-accent-900/40 to-transparent" aria-hidden />
        )}
        <div className="absolute inset-x-0 top-0 flex items-center justify-between gap-3 px-4 pt-4 sm:px-6 lg:px-10">
          <LibraryNav reviewCount={reviewCount} />
          <div className="hidden sm:block">
            <ScanStatus compact />
          </div>
        </div>
      </div>

      {empty ? (
        <div className="mx-auto flex w-full max-w-3xl flex-col gap-4 p-4 sm:p-6">
          <EmptyState
            icon={<Clapperboard />}
            title={t('Your library is empty')}
            description={
              stats.data && !stats.data.ready
                ? t('Add a metadata provider key in the library settings, then scan your channels.')
                : t('Scan your channels to find movies and series among their video files.')
            }
            action={
              stats.data && !stats.data.ready ? (
                <Button asChild>
                  <Link to="/settings/library">{t('Library settings')}</Link>
                </Button>
              ) : undefined
            }
          />
          <ScanStatus />
        </div>
      ) : (
        <div className="relative z-[1] -mt-6 flex flex-col gap-8">
          {entries.length > 0 && (
            <Row title={t('Continue watching')}>
              {entries.map((e) => (
                <ContinueCard key={e.key} entry={e} />
              ))}
            </Row>
          )}
          <ItemsRow title={t('Recently added')} items={recent.data} loading={recent.isPending} to="/library/all?sort=added" />
          <ItemsRow title={t('New movies')} items={recentMovies.data} loading={recentMovies.isPending} to="/library/movies?sort=added" />
          <ItemsRow title={t('New series')} items={recentSeries.data} loading={recentSeries.isPending} to="/library/series?sort=added" />
          {genreRows(genres.data ?? []).map((g) => (
            <GenreRow key={g.name} genre={g} />
          ))}
          <div className="mx-auto w-full max-w-3xl px-4 sm:px-6">
            <ScanStatus />
          </div>
        </div>
      )}
    </div>
  );
}

function ItemsRow({
  title,
  items,
  loading,
  to
}: {
  title: string;
  items: ApiLibraryItemDto[] | undefined;
  loading: boolean;
  to?: string;
}) {
  if (!loading && (!items || items.length === 0)) return null;
  return (
    <Row
      title={title}
      loading={loading}
      action={
        to ? (
          <Button size="sm" variant="ghost" asChild>
            <Link to={to}>
              {t('See all')}
              <ChevronRight />
            </Link>
          </Button>
        ) : undefined
      }
    >
      {loading
        ? Array.from({ length: 8 }, (_, i) => <PosterSkeleton key={i} className={ROW_POSTER} />)
        : (items ?? []).map((i) => <PosterCard key={i.id} item={i} className={ROW_POSTER} />)}
    </Row>
  );
}

function GenreRow({ genre }: { genre: ApiLibraryGenreDto }) {
  const row = useItemsRow({ genre: genre.name, sortBy: 'rating', sortDescending: true });
  return (
    <ItemsRow
      title={genre.name}
      items={row.data?.items}
      loading={row.isPending}
      to={`/library/all?genre=${encodeURIComponent(genre.name)}&sort=rating`}
    />
  );
}
