import type { ApiLibraryFileDto, LibraryFileStatus, LibraryKind } from '@tfm/api-client';
import { useMutation } from '@tanstack/react-query';
import { Badge, Button, Card, EmptyState, IconButton, Input, SegmentedControl, Skeleton, Spinner, toast, Tooltip } from '@tfm/ui';
import { Check, CircleSlash, Eye, FolderOpen, Lock, Play, Search, SearchCheck, TriangleAlert, Undo2, WandSparkles, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';
import { containingFolderRoute } from '@/features/search/fileMedia';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useSearchText } from '@/lib/useSearchText';
import { PageHeader } from '@/shell/PageHeader';
import { IdentifyDialog, type IdentifyTarget } from '../components/IdentifyDialog';
import { LibraryDisabled, LibraryNav, ScanStatus } from '../components/LibraryChrome';
import { LibraryImage } from '../components/LibraryImage';
import '../library.css';
import { episodeCode, isLibraryDisabled } from '../libraryModel';
import { playLibraryFile } from '../playback';
import { useInvalidateLibrary, useLibraryStats, useReviewFiles } from '../queries';

type Tab = Extract<LibraryFileStatus, 'review' | 'unmatched' | 'ignored'>;

export default function LibraryReviewPage() {
  const [tab, setTab] = useState<Tab>('review');
  const text = useSearchText('', 600);
  const [identify, setIdentify] = useState<IdentifyTarget | null>(null);
  const stats = useLibraryStats();
  const files = useReviewFiles(tab, text.query);
  const counts = stats.data?.files ?? {};

  const sentinel = useRef<HTMLDivElement>(null);
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = files;
  useEffect(() => {
    const el = sentinel.current;
    if (!el || !hasNextPage || isFetchingNextPage || typeof IntersectionObserver === 'undefined') return;
    const io = new IntersectionObserver((e) => e.some((x) => x.isIntersecting) && void fetchNextPage(), { rootMargin: '600px 0px' });
    io.observe(el);
    return () => io.disconnect();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);

  if (stats.data?.enabled === false || isLibraryDisabled(files.error)) return <LibraryDisabled />;

  const list = files.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="tfm-library mx-auto flex w-full max-w-5xl flex-col gap-5 p-4 sm:p-6 lg:p-8">
      <LibraryNav reviewCount={counts['review']} />
      <PageHeader
        title={t('Needs review')}
        icon={<WandSparkles />}
        description={t('Files the scan could not identify with confidence. Confirm the guess, pick the right title, or ignore them.')}
      />
      <ScanStatus />

      <div className="flex flex-wrap items-center gap-2">
        <SegmentedControl<Tab>
          aria-label={t('Status')}
          value={tab}
          onValueChange={setTab}
          options={[
            { value: 'review', label: `${t('To review')} ${counts['review'] ?? ''}`.trim() },
            { value: 'unmatched', label: `${t('Unmatched')} ${counts['unmatched'] ?? ''}`.trim() },
            { value: 'ignored', label: `${t('Ignored')} ${counts['ignored'] ?? ''}`.trim() }
          ]}
        />
        <Input
          className="min-w-48 flex-1"
          value={text.value}
          onChange={(e) => text.setValue(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && text.commit()}
          enterKeyHint="search"
          placeholder={t('Search file names...')}
          aria-label={t('Search file names')}
          leading={<Search className="size-4" />}
          trailing={
            text.value ? (
              <IconButton size="xs" label={t('Clear')} onClick={() => text.commit('')}>
                <X />
              </IconButton>
            ) : undefined
          }
        />
      </div>

      {files.isPending ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-24 w-full rounded-2xl" />
          ))}
        </div>
      ) : files.isError ? (
        <EmptyState
          icon={<TriangleAlert />}
          title={t('Could not load the files')}
          description={errorMessage(files.error)}
          action={<Button onClick={() => void files.refetch()}>{t('Retry')}</Button>}
        />
      ) : list.length === 0 ? (
        <EmptyState
          icon={<SearchCheck />}
          title={tab === 'review' ? t('Nothing to review') : tab === 'unmatched' ? t('No unmatched files') : t('No ignored files')}
          description={tab === 'review' ? t('Every identified file is confident enough.') : undefined}
        />
      ) : (
        <ul className="flex flex-col gap-2">
          {list.map((f) => (
            <ReviewRow key={`${f.channelId}:${f.fileId}`} file={f} onIdentify={() => setIdentify({ type: 'file', file: f })} />
          ))}
        </ul>
      )}
      <div ref={sentinel} className="flex h-10 items-center justify-center">
        {isFetchingNextPage && <Spinner size="sm" />}
      </div>
      <IdentifyDialog target={identify} onClose={() => setIdentify(null)} />
    </div>
  );
}

function ReviewRow({ file, onIdentify }: { file: ApiLibraryFileDto; onIdentify: () => void }) {
  const invalidate = useInvalidateLibrary();
  const item = file.item;
  const done = (message: string) => () => {
    toast.success(message);
    void invalidate();
  };
  const fail = (error: unknown) => toast.error(t('The change could not be saved'), { description: errorMessage(error) });

  // Accept the current guess: assigning it by hand locks it
  const confirm = useMutation({
    mutationFn: () => {
      if (!item) throw new Error(t('This file has no guess to confirm'));
      const kind: LibraryKind = item.kind === 'series' ? 'series' : 'movie';
      if (kind === 'series' && (file.season == null || file.episode == null)) throw new Error(t('Enter the season and the episode to assign a series file'));
      return api.library.matchFile(file.channelId, file.fileId, { provider: item.provider, providerId: item.providerId, kind, season: file.season, episode: file.episode });
    },
    onSuccess: done(t('Identification confirmed')),
    onError: (error) => {
      fail(error);
      if (item?.kind === 'series') onIdentify();
    }
  });
  const ignore = useMutation({ mutationFn: () => api.library.ignoreFile(file.channelId, file.fileId), onSuccess: done(t('File ignored')), onError: fail });
  const unignore = useMutation({ mutationFn: () => api.library.unignoreFile(file.channelId, file.fileId), onSuccess: done(t('File back in the library')), onError: fail });

  const guess = file.parsed;
  const code = episodeCode(file.season ?? guess?.season, file.episode ?? guess?.episode);

  return (
    <li>
      <Card glass className="flex flex-col gap-3 p-3 sm:flex-row sm:items-center">
        <div className="flex min-w-0 flex-1 items-center gap-3">
          <LibraryImage src={item?.posterUrl} alt="" size="w92" className="aspect-[2/3] w-12 shrink-0 rounded-md" />
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-fg" title={file.fileName}>
              {file.fileName}
            </p>
            <p className="truncate text-xs text-fg-subtle" title={file.folderPath}>
              {file.folderPath}
            </p>
            <div className="mt-1 flex flex-wrap items-center gap-1.5 text-xs">
              {item ? (
                <Link to={`/library/items/${encodeURIComponent(item.id)}`} className="inline-flex items-center gap-1 text-accent-300 hover:underline">
                  {item.title}
                  {item.year ? ` (${item.year})` : ''}
                  {code && ` · ${code}`}
                </Link>
              ) : (
                <span className="text-fg-muted">
                  {t('Read as')}: <span className="text-fg">{guess?.title || '—'}</span>
                  {guess?.year ? ` (${guess.year})` : ''}
                  {code && ` · ${code}`}
                </span>
              )}
              {file.confidence > 0 && <Badge tone={file.confidence >= 0.85 ? 'success' : file.confidence >= 0.6 ? 'warning' : 'danger'}>{Math.round(file.confidence * 100)}%</Badge>}
              {file.locked && (
                <Tooltip content={t('Identified by hand: scans leave it alone')}>
                  <Badge>
                    <Lock className="size-3" /> {t('Locked')}
                  </Badge>
                </Tooltip>
              )}
              {file.error && <span className="text-danger">{file.error}</span>}
            </div>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <IconButton size="sm" label={t('Play')} onClick={() => playLibraryFile(file)}>
            <Play />
          </IconButton>
          <IconButton size="sm" label={t('Open containing folder')} asChild>
            <Link to={containingFolderRoute(String(file.channelId), file.folderPath)}>
              <FolderOpen />
            </Link>
          </IconButton>
          {file.status === 'ignored' ? (
            <Button size="sm" variant="secondary" icon={<Undo2 />} loading={unignore.isPending} onClick={() => unignore.mutate()}>
              {t('Back to the library')}
            </Button>
          ) : (
            <>
              {file.status === 'review' && item && (
                <Button size="sm" variant="soft" icon={<Check />} loading={confirm.isPending} onClick={() => confirm.mutate()}>
                  {t('Confirm')}
                </Button>
              )}
              <Button size="sm" variant="secondary" icon={<Eye />} onClick={onIdentify}>
                {t('Identify')}
              </Button>
              <IconButton size="sm" label={t('Ignore this file')} loading={ignore.isPending} onClick={() => ignore.mutate()}>
                <CircleSlash />
              </IconButton>
            </>
          )}
        </div>
      </Card>
    </li>
  );
}
