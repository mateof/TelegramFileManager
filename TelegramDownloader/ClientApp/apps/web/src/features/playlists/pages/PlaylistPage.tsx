import type { PlaylistModel, PlaylistTrackModel } from '@tfm/api-client';
import { isApiError } from '@tfm/api-client';
import {
  Button,
  cn,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  IconButton,
  Input,
  Skeleton,
  toast
} from '@tfm/ui';
import { ArrowLeft, GripVertical, HardDrive, HardDriveDownload, ListEnd, ListMusic, ListPlus, MoreHorizontal, MoreVertical, Pencil, Play, Search, Shuffle, Trash2, X } from 'lucide-react';
import { useMemo, useState, type KeyboardEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { enqueueAudio } from '@/features/player/public';
import { fallbackGradient } from '@/features/player/metadata';
import { displayTitle } from '@/features/player/queue';
import { selectCurrent, usePlayer } from '@/features/player/store';
import { useDragReorder } from '@/features/player/ui/useDragReorder';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { deletePlaylist, playPlaylist, queuePlaylist } from '../actions';
import { playlistTrackToAudio } from '../convert';
import { DownloadPlaylistDialog, PlaylistNameDialog } from '../dialogs';
import { storePlaylist, usePlaylist } from '../queries';

const ROW = 56;

function TrackRow({
  track,
  index,
  playing,
  offset,
  dragging,
  draggable,
  handle,
  onPlay,
  onRemove,
  onKeyDown
}: {
  track: PlaylistTrackModel;
  index: number;
  playing: boolean;
  offset: number;
  dragging: boolean;
  draggable: boolean;
  handle: ReturnType<ReturnType<typeof useDragReorder>['handleProps']>;
  onPlay: () => void;
  onRemove: () => void;
  onKeyDown: (e: KeyboardEvent<HTMLLIElement>) => void;
}) {
  const audio = playlistTrackToAudio(track);
  return (
    <li
      data-row={index}
      tabIndex={0}
      onKeyDown={onKeyDown}
      onDoubleClick={onPlay}
      className={cn(
        'group relative flex items-center gap-2 rounded-xl pr-1 outline-none hover:bg-surface-2/70 focus-visible:ring-2 focus-visible:ring-ring',
        playing && 'bg-primary-soft',
        dragging ? 'z-10 bg-surface-2 shadow-elevated' : 'transition-transform duration-150'
      )}
      style={{ height: ROW, transform: offset ? `translateY(${offset}px)` : undefined }}
    >
      {draggable ? (
        <span {...handle} aria-hidden className="flex h-full w-6 shrink-0 items-center justify-center text-fg-subtle opacity-50 group-hover:opacity-100">
          <GripVertical className="size-4" />
        </span>
      ) : (
        <span className="w-6 shrink-0" />
      )}
      <button type="button" tabIndex={-1} onClick={onPlay} className="flex min-w-0 flex-1 items-center gap-3 text-left">
        <span className="w-6 shrink-0 text-right text-xs tabular-nums text-fg-subtle group-hover:hidden">{index + 1}</span>
        <Play className="hidden size-4 shrink-0 fill-current text-fg group-hover:block" />
        <span className="min-w-0 flex-1">
          <span className={cn('block truncate text-sm font-medium', playing ? 'text-accent-300' : 'text-fg')}>{displayTitle({ title: track.fileName })}</span>
          <span className="flex items-center gap-1.5 truncate text-xs text-fg-muted">
            {track.isLocalFile && <HardDrive className="size-3 shrink-0" aria-label={t('Local file')} />}
            <span className="truncate">{track.isLocalFile ? track.filePath : track.channelName}</span>
          </span>
        </span>
        <span className="hidden w-20 shrink-0 text-right text-xs tabular-nums text-fg-subtle sm:block">{track.fileSize ? formatBytes(track.fileSize) : ''}</span>
      </button>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <IconButton label={t('Track options')} size="xs" className="opacity-60 group-hover:opacity-100">
            <MoreHorizontal />
          </IconButton>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem icon={<Play />} onSelect={onPlay}>
            {t('Play')}
          </DropdownMenuItem>
          <DropdownMenuItem icon={<ListPlus />} onSelect={() => enqueueAudio([audio], 'next')}>
            {t('Play next')}
          </DropdownMenuItem>
          <DropdownMenuItem icon={<ListEnd />} onSelect={() => enqueueAudio([audio], 'end')}>
            {t('Add to queue')}
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem icon={<X />} destructive onSelect={onRemove}>
            {t('Remove from the playlist')}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </li>
  );
}

function PlaylistBody({ playlist }: { playlist: PlaylistModel }) {
  const navigate = useNavigate();
  const [filter, setFilter] = useState('');
  const [dialog, setDialog] = useState<'rename' | 'download' | null>(null);
  const current = usePlayer(selectCurrent);
  const tracks = playlist.tracks;
  const size = tracks.reduce((s, tr) => s + (tr.fileSize || 0), 0);

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase();
    return tracks.map((track, index) => ({ track, index })).filter(({ track }) => !q || track.fileName.toLowerCase().includes(q) || track.channelName?.toLowerCase().includes(q));
  }, [tracks, filter]);
  const canReorder = !filter.trim();

  const reorder = async (from: number, to: number) => {
    const next = tracks.slice();
    const [moved] = next.splice(from, 1);
    next.splice(to, 0, moved);
    const previous = playlist;
    storePlaylist({ ...playlist, tracks: next }); // optimistic
    try {
      storePlaylist(await api.playlists.reorder(playlist.id, next.map((tr) => tr.fileId)));
    } catch (e) {
      storePlaylist(previous);
      toast.error(t('Could not reorder the playlist'), { description: errorMessage(e) });
    }
  };

  const remove = async (track: PlaylistTrackModel) => {
    try {
      storePlaylist(await api.playlists.removeTrack(playlist.id, track.fileId));
      toast.success(t('Removed from the playlist'));
    } catch (e) {
      toast.error(t('Could not remove the track'), { description: errorMessage(e) });
    }
  };

  const { drag, handleProps, onRowKeyDown, offset } = useDragReorder({
    count: tracks.length,
    rowHeight: ROW,
    onMove: (from, to) => void reorder(from, to),
    getScrollElement: () => document.getElementById('tfm-main')
  });

  const isPlaying = (track: PlaylistTrackModel) => !!current && current.id === playlistTrackToAudio(track).id;

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <Link to="/playlists" className="inline-flex w-fit items-center gap-1.5 text-sm text-fg-muted hover:text-fg">
        <ArrowLeft className="size-4" />
        {t('Playlists')}
      </Link>

      <header className="flex flex-col gap-5 sm:flex-row sm:items-end">
        <div className="relative aspect-square w-40 shrink-0 overflow-hidden rounded-3xl shadow-elevated sm:w-48" style={{ backgroundImage: fallbackGradient(playlist.name) }}>
          <ListMusic aria-hidden className="absolute left-1/2 top-1/2 size-1/4 -translate-x-1/2 -translate-y-1/2 text-white/75" />
        </div>
        <div className="flex min-w-0 flex-1 flex-col gap-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-fg-subtle">{t('Playlist')}</p>
            <h1 className="break-words text-2xl font-bold tracking-tight text-fg sm:text-4xl">{playlist.name}</h1>
            {playlist.description && <p className="mt-1 text-sm text-fg-muted">{playlist.description}</p>}
            <p className="mt-1 text-sm text-fg-muted">
              {t('{count} tracks', { count: tracks.length })}
              {size > 0 && ` · ${formatBytes(size)}`}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button size="lg" icon={<Play className="fill-current" />} disabled={!tracks.length} onClick={() => playPlaylist(playlist)}>
              {t('Play all')}
            </Button>
            <Button size="lg" variant="secondary" icon={<Shuffle />} disabled={!tracks.length} onClick={() => playPlaylist(playlist, { shuffle: true })}>
              {t('Shuffle')}
            </Button>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <IconButton label={t('Playlist options')} size="lg" variant="ghost">
                  <MoreVertical />
                </IconButton>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="start">
                <DropdownMenuItem icon={<ListPlus />} disabled={!tracks.length} onSelect={() => queuePlaylist(playlist, 'next')}>
                  {t('Play next')}
                </DropdownMenuItem>
                <DropdownMenuItem icon={<ListEnd />} disabled={!tracks.length} onSelect={() => queuePlaylist(playlist, 'end')}>
                  {t('Add to queue')}
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem icon={<Pencil />} onSelect={() => setDialog('rename')}>
                  {t('Edit name and description')}
                </DropdownMenuItem>
                <DropdownMenuItem icon={<HardDriveDownload />} disabled={!tracks.length} onSelect={() => setDialog('download')}>
                  {t('Download to the server')}
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  icon={<Trash2 />}
                  destructive
                  onSelect={() => void deletePlaylist(playlist).then((ok) => ok && navigate('/playlists'))}
                >
                  {t('Delete playlist')}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </header>

      {tracks.length === 0 ? (
        <EmptyState icon={<ListMusic />} title={t('This playlist is empty')} description={t('Add tracks from any channel or from the local files with "Add to playlist".')} />
      ) : (
        <section className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <Input className="w-full sm:w-72" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t('Filter tracks')} aria-label={t('Filter tracks')} leading={<Search />} />
            <span className="text-xs text-fg-subtle">{canReorder ? t('Drag or Alt+↑/↓ to reorder') : t('Clear the filter to reorder')}</span>
          </div>
          <ul aria-label={t('Tracks')} className="flex flex-col" data-no-player-keys>
            {shown.map(({ track, index }) => (
              <TrackRow
                key={track.fileId}
                track={track}
                index={index}
                playing={isPlaying(track)}
                offset={canReorder ? offset(index) : 0}
                dragging={drag?.from === index}
                draggable={canReorder}
                handle={handleProps(index)}
                onPlay={() => playPlaylist(playlist, { start: index })}
                onRemove={() => void remove(track)}
                onKeyDown={(e) => {
                  if (canReorder && onRowKeyDown(index, e)) {
                    const to = e.key === 'ArrowUp' ? index - 1 : index + 1;
                    requestAnimationFrame(() => document.querySelector<HTMLElement>(`[data-row="${to}"]`)?.focus());
                  } else if (e.key === 'Enter') playPlaylist(playlist, { start: index });
                  else if (e.key === 'Delete') void remove(track);
                }}
              />
            ))}
          </ul>
        </section>
      )}

      {dialog === 'rename' && <PlaylistNameDialog playlist={playlist} onClose={() => setDialog(null)} />}
      {dialog === 'download' && <DownloadPlaylistDialog playlist={playlist} onClose={() => setDialog(null)} />}
    </div>
  );
}

export default function PlaylistPage() {
  const { id = '' } = useParams();
  const { data, isLoading, isError, error, refetch } = usePlaylist(id);

  if (isLoading) {
    return (
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-4 p-4 sm:p-6 lg:p-8">
        <Skeleton className="size-48 rounded-3xl" />
        {Array.from({ length: 8 }, (_, i) => (
          <Skeleton key={i} className="h-12 rounded-xl" />
        ))}
      </div>
    );
  }
  if (isError || !data) {
    const notFound = isApiError(error) && error.status === 404;
    return (
      <EmptyState
        className="py-24"
        icon={<ListMusic />}
        title={notFound ? t('Playlist not found') : t('Could not load the playlist')}
        description={notFound ? undefined : errorMessage(error)}
        action={
          <>
            <Button asChild variant="secondary">
              <Link to="/playlists">{t('Back to playlists')}</Link>
            </Button>
            {!notFound && <Button onClick={() => void refetch()}>{t('Retry')}</Button>}
          </>
        }
      />
    );
  }
  return <PlaylistBody playlist={data} />;
}
