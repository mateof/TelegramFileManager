import type { ApiProviderCandidateDto, LibraryKind } from '@tfm/api-client';
import { useQuery } from '@tanstack/react-query';
import { Badge, cn, EmptyState, IconButton, Input, SegmentedControl, Skeleton } from '@tfm/ui';
import { Check, Search, SearchX } from 'lucide-react';
import type { ReactNode } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useSearchText } from '@/lib/useSearchText';
import { SEARCH_DELAY_MS } from '../libraryModel';
import { libraryKeys } from '../queries';

export interface CandidateSearchProps {
  initialTitle: string;
  initialYear?: string;
  kind: LibraryKind;
  /** Offers the choice between movie and series. */
  onKindChange?: (kind: LibraryKind) => void;
  disabled?: boolean;
  onChoose: (candidate: ApiProviderCandidateDto) => void;
  /** Between the inputs and the results. */
  children?: ReactNode;
}

/** Title search in the metadata providers: after a pause in the typing, or with Enter. */
export function CandidateSearch({ initialTitle, initialYear = '', kind, onKindChange, disabled, onChoose, children }: CandidateSearchProps) {
  const title = useSearchText(initialTitle, SEARCH_DELAY_MS);
  const year = useSearchText(initialYear, SEARCH_DELAY_MS);
  const q = title.query;
  const searchYear = /^\d{4}$/.test(year.query) ? year.query : '';
  const pending = title.pending || year.pending;
  const searchNow = () => {
    title.commit();
    year.commit();
  };

  const candidates = useQuery({
    queryKey: libraryKeys.candidates(q, kind, searchYear),
    queryFn: ({ signal }) => api.library.searchProviders({ q, kind, year: searchYear ? Number(searchYear) : undefined }, signal),
    enabled: q.length >= 2,
    staleTime: 5 * 60_000
  });

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1.5">
        <form
          role="search"
          className="flex flex-col gap-2 sm:flex-row"
          onSubmit={(e) => {
            e.preventDefault();
            searchNow();
          }}
        >
          <Input
            className="flex-1"
            value={title.value}
            onChange={(e) => title.setValue(e.target.value)}
            placeholder={t('Title')}
            aria-label={t('Title')}
            enterKeyHint="search"
            leading={<Search className="size-4" />}
            trailing={
              pending ? (
                <IconButton size="xs" label={t('Search now')} onClick={searchNow}>
                  <Search />
                </IconButton>
              ) : undefined
            }
            autoFocus
          />
          <Input
            className="sm:w-24"
            value={year.value}
            onChange={(e) => year.setValue(e.target.value.replace(/\D/g, '').slice(0, 4))}
            placeholder={t('Year')}
            aria-label={t('Year')}
            enterKeyHint="search"
            inputMode="numeric"
          />
          {/* With two fields, Enter only submits when there is a submit button */}
          <button type="submit" hidden />
          {onKindChange && (
            <SegmentedControl<LibraryKind>
              aria-label={t('Type')}
              value={kind}
              onValueChange={(next) => {
                searchNow();
                onKindChange(next);
              }}
              options={[
                { value: 'movie', label: t('Movie') },
                { value: 'series', label: t('Series') }
              ]}
            />
          )}
        </form>
        <p className="text-xs text-fg-subtle">{t('Press Enter to search at once.')}</p>
      </div>
      {children}

      <div className="max-h-[50vh] min-h-48 overflow-y-auto rounded-xl border border-border">
        {q.length < 2 ? (
          <EmptyState icon={<Search />} title={t('Type a title to search the providers')} />
        ) : candidates.isPending ? (
          <div className="flex flex-col gap-2 p-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-20 w-full" />
            ))}
          </div>
        ) : candidates.isError ? (
          <EmptyState icon={<SearchX />} title={t('The search failed')} description={errorMessage(candidates.error)} />
        ) : !candidates.data.length ? (
          <EmptyState icon={<SearchX />} title={t('No matches')} description={t('Try the original title, another year or the other type.')} />
        ) : (
          <ul className={cn('divide-y divide-border/60 transition-opacity', pending && 'opacity-50')}>
            {candidates.data.map((c) => (
              <li key={`${c.provider}:${c.providerId}`}>
                <button
                  type="button"
                  disabled={disabled}
                  onClick={() => onChoose(c)}
                  className="group flex w-full items-start gap-3 p-3 text-left transition-colors hover:bg-surface-2/70 focus-visible:bg-surface-2/70 focus-visible:outline-none disabled:opacity-60"
                >
                  <span className="relative block h-21 w-14 shrink-0 overflow-hidden rounded-md bg-surface-3">
                    {c.posterUrl && (
                      <img
                        src={c.posterUrl}
                        alt=""
                        loading="lazy"
                        decoding="async"
                        onError={(e) => (e.currentTarget.style.visibility = 'hidden')}
                        className="size-full object-cover"
                      />
                    )}
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="flex flex-wrap items-center gap-2">
                      <span className="font-medium text-fg">{c.title}</span>
                      {c.year && <span className="text-sm text-fg-subtle">{c.year}</span>}
                      <Badge className="uppercase">{c.provider}</Badge>
                    </span>
                    {c.originalTitle && c.originalTitle !== c.title && <span className="block text-xs text-fg-subtle">{c.originalTitle}</span>}
                    {c.overview && <span className="mt-1 line-clamp-2 block text-xs text-fg-muted">{c.overview}</span>}
                  </span>
                  <Check className="mt-1 size-4 shrink-0 text-accent-400 opacity-0 transition-opacity group-hover:opacity-100" aria-hidden />
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
