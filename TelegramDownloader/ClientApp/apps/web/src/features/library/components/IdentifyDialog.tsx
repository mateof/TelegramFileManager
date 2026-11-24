import type { ApiLibraryFileDto, ApiLibraryItemDto, ApiProviderCandidateDto, LibraryKind } from '@tfm/api-client';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Badge, Button, cn, Dialog, DialogContent, EmptyState, Input, SegmentedControl, Skeleton, toast } from '@tfm/ui';
import { Check, EyeOff, Search, SearchX } from 'lucide-react';
import { useDeferredValue, useState } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { libraryKeys, useInvalidateLibrary } from '../queries';

export type IdentifyTarget = { type: 'file'; file: ApiLibraryFileDto } | { type: 'item'; item: ApiLibraryItemDto };

export interface IdentifyDialogProps {
  target: IdentifyTarget | null;
  onClose: () => void;
}

/** Fix an identification: search the providers and assign a file (match) or a whole item (identify). */
export function IdentifyDialog({ target, onClose }: IdentifyDialogProps) {
  return (
    <Dialog open={!!target} onOpenChange={(open) => !open && onClose()}>
      {target && (
        <DialogContent
          size="lg"
          title={target.type === 'file' ? t('Identify file') : t('Identify title')}
          description={target.type === 'file' ? target.file.fileName : t('Every file of "{title}" moves to the title you choose.', { title: target.item.title })}
        >
          <IdentifyBody key={target.type === 'file' ? `${target.file.channelId}:${target.file.fileId}` : target.item.id} target={target} onDone={onClose} />
        </DialogContent>
      )}
    </Dialog>
  );
}

function initial(target: IdentifyTarget) {
  if (target.type === 'item') {
    return { q: target.item.title, kind: (target.item.kind === 'series' ? 'series' : 'movie') as LibraryKind, year: target.item.year ? String(target.item.year) : '', season: '', episode: '' };
  }
  const f = target.file;
  const isSeries = f.kind === 'series' || f.parsed?.isSeries;
  return {
    q: f.item?.title ?? f.parsed?.title ?? f.fileName,
    kind: (isSeries ? 'series' : 'movie') as LibraryKind,
    year: f.parsed?.year ? String(f.parsed.year) : '',
    season: String(f.season ?? f.parsed?.season ?? (isSeries ? 1 : '')),
    episode: String(f.episode ?? f.parsed?.episode ?? '')
  };
}

function IdentifyBody({ target, onDone }: { target: IdentifyTarget; onDone: () => void }) {
  const [form, setForm] = useState(() => initial(target));
  const invalidate = useInvalidateLibrary();
  const q = useDeferredValue(form.q.trim());
  const year = useDeferredValue(form.year.trim());
  const isFile = target.type === 'file';
  const needsEpisode = isFile && form.kind === 'series';

  const candidates = useQuery({
    queryKey: libraryKeys.candidates(q, form.kind, year),
    queryFn: ({ signal }) => api.library.searchProviders({ q, kind: form.kind, year: /^\d{4}$/.test(year) ? Number(year) : undefined }, signal),
    enabled: q.length >= 2,
    staleTime: 5 * 60_000
  });

  const choose = useMutation({
    mutationFn: async (c: ApiProviderCandidateDto) => {
      if (target.type === 'file') {
        const season = Number(form.season);
        const episode = Number(form.episode);
        if (needsEpisode && (!Number.isInteger(season) || !Number.isInteger(episode) || !form.season || !form.episode)) {
          throw new Error(t('Enter the season and the episode to assign a series file'));
        }
        await api.library.matchFile(target.file.channelId, target.file.fileId, {
          provider: c.provider,
          providerId: c.providerId,
          kind: form.kind,
          season: needsEpisode ? season : null,
          episode: needsEpisode ? episode : null
        });
      } else {
        await api.library.identifyItem(target.item.id, { provider: c.provider, providerId: c.providerId, kind: form.kind });
      }
    },
    onSuccess: (_d, c) => {
      toast.success(t('Identified as {title}', { title: c.title }));
      void invalidate();
      onDone();
    },
    onError: (error) => toast.error(t('Could not save the identification'), { description: errorMessage(error) })
  });

  const ignore = useMutation({
    mutationFn: () => (target.type === 'file' ? api.library.ignoreFile(target.file.channelId, target.file.fileId) : Promise.resolve(null)),
    onSuccess: () => {
      toast.success(t('File ignored'));
      void invalidate();
      onDone();
    },
    onError: (error) => toast.error(t('Could not ignore the file'), { description: errorMessage(error) })
  });

  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));
  const digits = (v: string) => v.replace(/\D/g, '');

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-2 sm:flex-row">
        <Input
          className="flex-1"
          value={form.q}
          onChange={(e) => set({ q: e.target.value })}
          placeholder={t('Title')}
          aria-label={t('Title')}
          leading={<Search className="size-4" />}
          autoFocus
        />
        <Input className="sm:w-24" value={form.year} onChange={(e) => set({ year: digits(e.target.value).slice(0, 4) })} placeholder={t('Year')} aria-label={t('Year')} inputMode="numeric" />
        <SegmentedControl<LibraryKind>
          aria-label={t('Type')}
          value={form.kind}
          onValueChange={(kind) => set({ kind })}
          options={[
            { value: 'movie', label: t('Movie') },
            { value: 'series', label: t('Series') }
          ]}
        />
      </div>
      {needsEpisode && (
        <div className="flex items-center gap-2 rounded-xl border border-border bg-surface-2/50 p-2.5 text-sm text-fg-muted">
          <span className="flex-1">{t('Episode of this file')}</span>
          <Input className="w-20" value={form.season} onChange={(e) => set({ season: digits(e.target.value) })} aria-label={t('Season')} placeholder={t('Season')} inputMode="numeric" />
          <Input className="w-20" value={form.episode} onChange={(e) => set({ episode: digits(e.target.value) })} aria-label={t('Episode')} placeholder={t('Episode')} inputMode="numeric" />
        </div>
      )}

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
          <ul className="divide-y divide-border/60">
            {candidates.data.map((c) => (
              <li key={`${c.provider}:${c.providerId}`}>
                <button
                  type="button"
                  disabled={choose.isPending}
                  onClick={() => choose.mutate(c)}
                  className={cn(
                    'group flex w-full items-start gap-3 p-3 text-left transition-colors hover:bg-surface-2/70 focus-visible:bg-surface-2/70 focus-visible:outline-none disabled:opacity-60'
                  )}
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

      {isFile && (
        <div className="flex justify-between gap-2">
          <Button variant="ghost" icon={<EyeOff />} loading={ignore.isPending} onClick={() => ignore.mutate()}>
            {t('Ignore this file')}
          </Button>
          <span className="self-center text-xs text-fg-subtle">{t('Manual identifications are locked: scans leave them alone.')}</span>
        </div>
      )}
    </div>
  );
}
