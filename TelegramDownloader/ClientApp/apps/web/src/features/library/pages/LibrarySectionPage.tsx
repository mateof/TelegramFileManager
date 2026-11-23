import type { LibraryItemsSortBy, LibraryKind } from '@tfm/api-client';
import { Button, EmptyState, IconButton, Input, Select, Spinner } from '@tfm/ui';
import { ArrowDownWideNarrow, ArrowUpNarrowWide, Clapperboard, Film, Search, SearchX, TriangleAlert, Tv, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Navigate, useParams, useSearchParams } from 'react-router';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';
import { PageHeader } from '@/shell/PageHeader';
import { PosterCard, PosterSkeleton } from '../components/Cards';
import { LibraryDisabled, LibraryNav } from '../components/LibraryChrome';
import '../library.css';
import { decades, isLibraryDisabled, matchesDecade, parseBrowseFilters } from '../libraryModel';
import { useGenres, useItemsInfinite, useLibraryStats } from '../queries';

const SECTIONS: Record<string, LibraryKind | 'all'> = { movies: 'movie', series: 'series', all: 'all' };
const ANY = '__any';

export default function LibrarySectionPage() {
  const { section = '' } = useParams();
  const kind = SECTIONS[section];
  if (!kind) return <Navigate to="/library" replace />;
  return <Browse key={section} kind={kind} />;
}

function Browse({ kind }: { kind: LibraryKind | 'all' }) {
  const [params, setParams] = useSearchParams();
  const filters = parseBrowseFilters(params, kind);
  const apiKind = kind === 'all' ? undefined : kind;
  const stats = useLibraryStats();
  const genres = useGenres(apiKind);
  const [text, setText] = useState(filters.search);

  const update = (patch: Record<string, string | null>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        for (const [k, v] of Object.entries(patch)) {
          if (v) next.set(k, v);
          else next.delete(k);
        }
        return next;
      },
      { replace: true }
    );

  useEffect(() => {
    if (text.trim() === filters.search) return;
    const id = window.setTimeout(() => update({ q: text.trim() || null }), 350);
    return () => window.clearTimeout(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text]);

  const items = useItemsInfinite({
    kind: apiKind,
    genre: filters.genre ?? undefined,
    search: filters.search || undefined,
    sortBy: filters.sortBy,
    sortDescending: filters.desc
  });

  const list = useMemo(
    () => (items.data?.pages.flatMap((p) => p.items) ?? []).filter((i) => matchesDecade(i, filters.decade)),
    [items.data, filters.decade]
  );
  const total = items.data?.pages[0]?.page.totalItems ?? 0;

  // Infinite scroll
  const sentinel = useRef<HTMLDivElement>(null);
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = items;
  useEffect(() => {
    const el = sentinel.current;
    if (!el || !hasNextPage || isFetchingNextPage || typeof IntersectionObserver === 'undefined') return;
    const io = new IntersectionObserver((e) => e.some((x) => x.isIntersecting) && void fetchNextPage(), { rootMargin: '800px 0px' });
    io.observe(el);
    return () => io.disconnect();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);

  if (stats.data?.enabled === false || isLibraryDisabled(items.error)) return <LibraryDisabled />;

  const title = kind === 'movie' ? t('Movies') : kind === 'series' ? t('Series') : t('All titles');
  const icon = kind === 'movie' ? <Film /> : kind === 'series' ? <Tv /> : <Clapperboard />;
  const sortOptions: { value: LibraryItemsSortBy; label: string }[] = [
    { value: 'added', label: t('Date added') },
    { value: 'title', label: t('Title') },
    { value: 'year', label: t('Year') },
    { value: 'rating', label: t('Rating') },
    { value: 'lastPlayed', label: t('Last played') }
  ];

  return (
    <div className="tfm-library mx-auto flex w-full max-w-[1600px] flex-col gap-5 p-4 sm:p-6 lg:p-8">
      <LibraryNav reviewCount={stats.data?.files?.['review']} />
      <PageHeader
        title={title}
        icon={icon}
        description={items.isSuccess ? t('{count} titles', { count: filters.decade ? list.length : total }) : undefined}
      />

      <div className="glass flex flex-wrap items-center gap-2 rounded-2xl border border-border p-2.5 shadow-soft">
        <Input
          className="min-w-48 flex-1"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={t('Search titles...')}
          aria-label={t('Search titles')}
          leading={<Search className="size-4" />}
          trailing={
            text ? (
              <IconButton size="xs" label={t('Clear')} onClick={() => setText('')}>
                <X />
              </IconButton>
            ) : undefined
          }
        />
        <Select
          aria-label={t('Genre')}
          className="w-40"
          value={filters.genre ?? ANY}
          onValueChange={(v) => update({ genre: v === ANY ? null : v })}
          options={[{ value: ANY, label: t('All genres') }, ...(genres.data ?? []).map((g) => ({ value: g.name, label: `${g.name} (${g.count})` }))]}
        />
        <Select
          aria-label={t('Decade')}
          className="w-32"
          value={filters.decade ? String(filters.decade) : ANY}
          onValueChange={(v) => update({ decade: v === ANY ? null : v })}
          options={[{ value: ANY, label: t('Any year') }, ...decades().map((d) => ({ value: String(d), label: `${d}s` }))]}
        />
        <Select<LibraryItemsSortBy>
          aria-label={t('Sort by')}
          className="w-40"
          value={filters.sortBy}
          onValueChange={(v) => update({ sort: v, dir: null })}
          options={sortOptions}
        />
        <IconButton
          label={filters.desc ? t('Descending') : t('Ascending')}
          variant="secondary"
          onClick={() => update({ dir: filters.desc ? 'asc' : 'desc' })}
        >
          {filters.desc ? <ArrowDownWideNarrow /> : <ArrowUpNarrowWide />}
        </IconButton>
      </div>

      {items.isPending ? (
        <Grid>
          {Array.from({ length: 18 }, (_, i) => (
            <PosterSkeleton key={i} />
          ))}
        </Grid>
      ) : items.isError ? (
        <EmptyState
          icon={<TriangleAlert />}
          title={t('Could not load the library')}
          description={errorMessage(items.error)}
          action={<Button onClick={() => void items.refetch()}>{t('Retry')}</Button>}
        />
      ) : list.length === 0 && !hasNextPage ? (
        <EmptyState icon={<SearchX />} title={t('No titles')} description={t('Nothing matches these filters.')} />
      ) : (
        <Grid>
          {list.map((item, i) => (
            <PosterCard key={item.id} item={item} className="animate-fade-in" style={{ animationDelay: `${Math.min(i % 60, 20) * 20}ms` }} />
          ))}
        </Grid>
      )}
      <div ref={sentinel} className="flex h-12 items-center justify-center">
        {isFetchingNextPage && <Spinner size="sm" />}
      </div>
    </div>
  );
}

function Grid({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-[repeat(auto-fill,minmax(8.5rem,1fr))] gap-x-4 gap-y-6 sm:grid-cols-[repeat(auto-fill,minmax(10rem,1fr))]">{children}</div>;
}
