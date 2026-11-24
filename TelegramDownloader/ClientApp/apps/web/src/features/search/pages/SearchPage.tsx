import type { ApiFileDto, BrowseFilter, GlobalSearchScope } from '@tfm/api-client';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Avatar,
  Badge,
  Button,
  Card,
  cn,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  IconButton,
  Input,
  SegmentedControl,
  Select,
  Skeleton
} from '@tfm/ui';
import {
  ChevronDown,
  CloudDownload,
  Download,
  Eye,
  FolderOpen,
  HardDrive,
  ListEnd,
  ListMusic,
  ListPlus,
  MoreHorizontal,
  Play,
  Search,
  SearchX,
  X
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { PageHeader } from '@/shell/PageHeader';
import { FileIcon } from '../components/FileIcon';
import { addToPlaylist, downloadToServer, enqueueFiles, playFiles, viewFile } from '../fileActions';
import { containingFolderRoute, mediaKind, type MediaFileDto } from '../fileMedia';
import { groupSearchResults, highlightParts, normaliseQuery, parseFilter, parseScope, type SearchGroup } from '../searchModel';

const LIMIT = 300;
const COLLAPSED_ROWS = 25;

function filterOptions() {
  return [
    { value: 'all' as const, label: t('All types') },
    { value: 'audio' as const, label: t('Audio') },
    { value: 'video' as const, label: t('Video') },
    { value: 'photo' as const, label: t('Photos') },
    { value: 'document' as const, label: t('Documents') },
    { value: 'archive' as const, label: t('Archives') }
  ];
}

export default function SearchPage() {
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const filter = parseFilter(params.get('type'));
  const scope = parseScope(params.get('scope'));
  const [text, setText] = useState(q);
  const inputRef = useRef<HTMLInputElement>(null);

  // The command palette (or the back button) can change ?q= while the page is open
  const [lastQ, setLastQ] = useState(q);
  if (q !== lastQ) {
    setLastQ(q);
    setText(q);
  }

  const update = (patch: Record<string, string | null>) => {
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        for (const [k, v] of Object.entries(patch)) {
          if (v && v !== 'all') next.set(k, v);
          else next.delete(k);
        }
        return next;
      },
      { replace: true }
    );
  };

  // Debounced typing → URL
  useEffect(() => {
    if (text.trim() === q.trim()) return;
    const id = window.setTimeout(() => update({ q: text.trim() || null }), 350);
    return () => window.clearTimeout(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text]);

  useEffect(() => inputRef.current?.focus(), []);

  const query = normaliseQuery(q);
  const results = useQuery({
    queryKey: ['search', 'global', query, filter, scope],
    queryFn: ({ signal }) => api.search.global({ q: query ?? '', filter, scope, limit: LIMIT }, signal),
    enabled: !!query,
    placeholderData: keepPreviousData,
    staleTime: 60_000
  });

  const summary = useMemo(() => groupSearchResults(results.data, t('Local files')), [results.data]);

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader title={t('Search')} description={t('Search files in every channel.')} icon={<Search />} />

      <Card glass className="flex flex-col gap-3 p-3 sm:flex-row sm:items-center">
        <form
          className="min-w-0 flex-1"
          role="search"
          onSubmit={(e) => {
            e.preventDefault();
            update({ q: text.trim() || null });
          }}
        >
          <Input
            ref={inputRef}
            inputSize="lg"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('File name...')}
            aria-label={t('Search files')}
            leading={<Search className="size-4.5" />}
            trailing={
              text ? (
                <IconButton size="xs" label={t('Clear')} onClick={() => setText('')}>
                  <X />
                </IconButton>
              ) : undefined
            }
          />
        </form>
        <div className="flex flex-wrap items-center gap-2">
          <SegmentedControl<GlobalSearchScope>
            aria-label={t('Where to search')}
            value={scope}
            onValueChange={(v) => update({ scope: v })}
            options={[
              { value: 'all', label: t('Everywhere') },
              { value: 'channels', label: t('Channels') },
              { value: 'local', label: t('Local') }
            ]}
          />
          <Select<BrowseFilter>
            aria-label={t('File type')}
            className="w-40"
            value={filter}
            onValueChange={(v) => update({ type: v })}
            options={filterOptions()}
          />
        </div>
      </Card>

      {!query ? (
        <EmptyState
          icon={<Search />}
          title={t('Search across all your files')}
          description={t('Type at least two characters. Every indexed channel and the server local storage are searched at once.')}
        />
      ) : results.isPending ? (
        <ResultsSkeleton />
      ) : results.isError ? (
        <EmptyState
          icon={<SearchX />}
          title={t('The search failed')}
          description={errorMessage(results.error)}
          action={<Button onClick={() => void results.refetch()}>{t('Retry')}</Button>}
        />
      ) : summary.total === 0 ? (
        <EmptyState icon={<SearchX />} title={t('No files found')} description={t('Nothing matches "{query}". Try another word or file type.', { query })} />
      ) : (
        <div className={cn('flex flex-col gap-5 transition-opacity', results.isPlaceholderData && 'opacity-60')} aria-busy={results.isFetching}>
          <p className="text-sm text-fg-muted" aria-live="polite">
            {t('{count} files in {groups} places', { count: summary.total, groups: summary.groups.length })}
            {summary.truncated && <span className="ml-2 text-warning">{t('Showing the first results only: refine the search to see more.')}</span>}
          </p>
          {summary.groups.map((g) => (
            <ResultGroup key={g.key} group={g} query={query} />
          ))}
        </div>
      )}
    </div>
  );
}

function ResultsSkeleton() {
  return (
    <div className="flex flex-col gap-4">
      {[0, 1].map((i) => (
        <Card key={i} className="flex flex-col gap-3 p-4">
          <div className="flex items-center gap-3">
            <Skeleton className="size-8 rounded-lg" />
            <Skeleton className="h-4 w-40" />
          </div>
          {[0, 1, 2, 3].map((j) => (
            <Skeleton key={j} className="h-10 w-full" />
          ))}
        </Card>
      ))}
    </div>
  );
}

function ResultGroup({ group, query }: { group: SearchGroup; query: string }) {
  const [open, setOpen] = useState(true);
  const [expanded, setExpanded] = useState(false);
  const audioCount = group.files.filter((f) => mediaKind(f) === 'audio').length;
  const visible = expanded ? group.files : group.files.slice(0, COLLAPSED_ROWS);

  return (
    <Card glass="flat" className="overflow-hidden">
      <div className="flex items-center gap-3 border-b border-border px-4 py-3">
        <button
          type="button"
          className="flex min-w-0 flex-1 items-center gap-3 rounded-lg text-left focus-visible:outline-2 focus-visible:outline-ring"
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
        >
          {group.channelId === null ? (
            <span className="flex size-8 items-center justify-center rounded-lg bg-surface-3 text-fg-muted">
              <HardDrive className="size-4" />
            </span>
          ) : (
            <Avatar src={api.channels.imageUrl(group.channelId)} name={group.title} size="sm" square className="rounded-lg" />
          )}
          <span className="min-w-0">
            <span className="block truncate font-semibold text-fg">{group.title}</span>
            <span className="block text-xs text-fg-subtle">{t('{count} files', { count: group.files.length })}</span>
          </span>
          <ChevronDown className={cn('ml-1 size-4 shrink-0 text-fg-subtle transition-transform', !open && '-rotate-90')} />
        </button>
        {audioCount > 0 && (
          <Button size="sm" variant="soft" icon={<Play />} onClick={() => playFiles(group.files, group.channelId, group.title)} aria-label={t('Play {count}', { count: audioCount })}>
            <span className="hidden sm:inline">{t('Play {count}', { count: audioCount })}</span>
          </Button>
        )}
        <Button size="sm" variant="ghost" asChild>
          <Link to={containingFolderRoute(group.channelId, '/')} aria-label={t('Open')}>
            <FolderOpen />
            <span className="hidden sm:inline">{t('Open')}</span>
          </Link>
        </Button>
      </div>
      {open && (
        <ul className="divide-y divide-border/60">
          {visible.map((f) => (
            <ResultRow key={f.id} file={f} group={group} query={query} />
          ))}
        </ul>
      )}
      {open && group.files.length > COLLAPSED_ROWS && (
        <div className="border-t border-border px-4 py-2 text-center">
          <Button size="sm" variant="link" onClick={() => setExpanded((e) => !e)}>
            {expanded ? t('Show less') : t('Show all {count}', { count: group.files.length })}
          </Button>
        </div>
      )}
    </Card>
  );
}

function ResultRow({ file, group, query }: { file: ApiFileDto; group: SearchGroup; query: string }) {
  const navigate = useNavigate();
  const kind = mediaKind(file);
  const isAudio = kind === 'audio';
  const canView = kind !== 'other' && kind !== 'audio';
  const folder = containingFolderRoute(group.channelId, file.path);
  const downloadHref = (file as MediaFileDto).contentDownloadUrl ?? file.downloadUrl;

  const primary = () => {
    if (isAudio) playFiles(group.files, group.channelId, group.title, file);
    else if (canView) viewFile(file, group.files, group.channelId);
    else void navigate(folder);
  };

  return (
    <li className="group flex items-center gap-3 px-4 py-2 transition-colors hover:bg-surface-2/60">
      <button
        type="button"
        onClick={primary}
        className="flex min-w-0 flex-1 items-center gap-3 rounded-lg text-left focus-visible:outline-2 focus-visible:outline-ring"
        title={file.name}
      >
        <FileIcon category={file.category} />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm text-fg">
            {highlightParts(file.name, query).map((p, i) =>
              p.match ? (
                <mark key={i} className="rounded-sm bg-primary-soft text-accent-200">
                  {p.text}
                </mark>
              ) : (
                <span key={i}>{p.text}</span>
              )
            )}
          </span>
          <span className="block truncate text-xs text-fg-subtle">{file.path}</span>
        </span>
      </button>
      <Badge className="hidden sm:inline-flex">{file.sizeText}</Badge>
      {(isAudio || canView) && (
        <IconButton size="sm" label={isAudio ? t('Play') : t('View')} onClick={primary} className="opacity-70 group-hover:opacity-100">
          {isAudio ? <Play /> : <Eye />}
        </IconButton>
      )}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <IconButton size="sm" label={t('More actions')}>
            <MoreHorizontal />
          </IconButton>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {isAudio && (
            <>
              <DropdownMenuItem onSelect={() => playFiles([file], group.channelId, group.title)}>
                <Play /> {t('Play')}
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => enqueueFiles([file], group.channelId, group.title, 'next')}>
                <ListPlus /> {t('Play next')}
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => enqueueFiles([file], group.channelId, group.title, 'end')}>
                <ListEnd /> {t('Add to queue')}
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => addToPlaylist([file], group.channelId)}>
                <ListMusic /> {t('Add to playlist')}
              </DropdownMenuItem>
              <DropdownMenuSeparator />
            </>
          )}
          {canView && (
            <DropdownMenuItem onSelect={() => viewFile(file, group.files, group.channelId)}>
              <Eye /> {t('View')}
            </DropdownMenuItem>
          )}
          <DropdownMenuItem onSelect={() => void navigate(folder)}>
            <FolderOpen /> {t('Open containing folder')}
          </DropdownMenuItem>
          {group.channelId !== null && (
            <DropdownMenuItem onSelect={() => void downloadToServer([file], group.channelId)}>
              <CloudDownload /> {t('Download to server')}
            </DropdownMenuItem>
          )}
          {downloadHref && (
            <DropdownMenuItem asChild>
              <a href={downloadHref} download={file.name}>
                <Download /> {t('Download to this device')}
              </a>
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>
    </li>
  );
}
