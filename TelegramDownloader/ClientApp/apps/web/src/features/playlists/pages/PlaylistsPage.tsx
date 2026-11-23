import type { PlaylistModel, PlaylistSortBy } from '@tfm/api-client';
import {
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  IconButton,
  Input,
  SegmentedControl,
  Skeleton
} from '@tfm/ui';
import { HardDriveDownload, ListMusic, ListPlus, MoreVertical, Pencil, Play, Plus, Search, Shuffle, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { fallbackGradient } from '@/features/player/metadata';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { PageHeader } from '@/shell/PageHeader';
import { deletePlaylist, playPlaylist, queuePlaylist } from '../actions';
import { DownloadPlaylistDialog, PlaylistNameDialog } from '../dialogs';
import { usePlaylists } from '../queries';

type DialogState = { kind: 'create' } | { kind: 'rename' | 'download'; playlist: PlaylistModel } | null;

function PlaylistCard({ p, onDialog }: { p: PlaylistModel; onDialog: (d: DialogState) => void }) {
  const size = p.tracks.reduce((sum, tr) => sum + (tr.fileSize || 0), 0);
  return (
    <div className="group relative flex flex-col gap-3 rounded-2xl border border-border bg-surface/60 p-3 shadow-soft transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-0.5 hover:border-border-strong/60 hover:shadow-elevated">
      <Link to={`/playlists/${p.id}`} className="absolute inset-0 z-0 rounded-2xl focus-visible:outline-2 focus-visible:outline-ring" aria-label={p.name} />
      <div className="relative aspect-square overflow-hidden rounded-xl shadow-soft" style={{ backgroundImage: fallbackGradient(p.name) }}>
        <ListMusic aria-hidden className="absolute left-1/2 top-1/2 size-1/4 -translate-x-1/2 -translate-y-1/2 text-white/70" />
        <button
          type="button"
          onClick={() => playPlaylist(p)}
          aria-label={t('Play "{name}"', { name: p.name })}
          disabled={p.trackCount === 0}
          className="absolute bottom-2.5 right-2.5 z-10 flex size-11 translate-y-2 items-center justify-center rounded-full bg-primary text-on-accent opacity-0 shadow-elevated transition-all duration-200 hover:scale-105 focus-visible:translate-y-0 focus-visible:opacity-100 group-hover:translate-y-0 group-hover:opacity-100 disabled:hidden max-md:translate-y-0 max-md:opacity-100"
        >
          <Play className="size-5 translate-x-px fill-current" />
        </button>
      </div>
      <div className="flex items-start gap-1">
        <div className="min-w-0 flex-1">
          <h3 className="truncate text-sm font-semibold text-fg">{p.name}</h3>
          <p className="truncate text-xs text-fg-muted">
            {t('{count} tracks', { count: p.trackCount })}
            {size > 0 && ` · ${formatBytes(size)}`}
          </p>
        </div>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <IconButton label={t('Playlist options')} size="xs" className="relative z-10 -mr-1">
              <MoreVertical />
            </IconButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem icon={<Play />} disabled={!p.trackCount} onSelect={() => playPlaylist(p)}>
              {t('Play')}
            </DropdownMenuItem>
            <DropdownMenuItem icon={<Shuffle />} disabled={!p.trackCount} onSelect={() => playPlaylist(p, { shuffle: true })}>
              {t('Shuffle')}
            </DropdownMenuItem>
            <DropdownMenuItem icon={<ListPlus />} disabled={!p.trackCount} onSelect={() => queuePlaylist(p, 'end')}>
              {t('Add to queue')}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem icon={<Pencil />} onSelect={() => onDialog({ kind: 'rename', playlist: p })}>
              {t('Rename')}
            </DropdownMenuItem>
            <DropdownMenuItem icon={<HardDriveDownload />} disabled={!p.trackCount} onSelect={() => onDialog({ kind: 'download', playlist: p })}>
              {t('Download to the server')}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem icon={<Trash2 />} destructive onSelect={() => void deletePlaylist(p)}>
              {t('Delete')}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </div>
  );
}

export default function PlaylistsPage() {
  const [sort, setSort] = useState<PlaylistSortBy>('name');
  const [filter, setFilter] = useState('');
  const [dialog, setDialog] = useState<DialogState>(null);
  const navigate = useNavigate();
  const { data, isLoading, isError, error, refetch } = usePlaylists(sort);

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase();
    return (data ?? []).filter((p) => !q || p.name.toLowerCase().includes(q) || p.description?.toLowerCase().includes(q));
  }, [data, filter]);

  return (
    <div className="mx-auto flex w-full max-w-7xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={t('Playlists')}
        description={t('Your audio playlists.')}
        icon={<ListMusic />}
        actions={
          <Button icon={<Plus />} onClick={() => setDialog({ kind: 'create' })}>
            {t('New playlist')}
          </Button>
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <Input className="w-full sm:w-72" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t('Filter playlists')} aria-label={t('Filter playlists')} leading={<Search />} />
        <SegmentedControl
          aria-label={t('Sort by')}
          size="sm"
          value={sort}
          onValueChange={setSort}
          options={[
            { value: 'name', label: t('Name') },
            { value: 'date', label: t('Recent') },
            { value: 'tracks', label: t('Tracks') }
          ]}
        />
      </div>

      {isLoading && (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
          {Array.from({ length: 10 }, (_, i) => (
            <Skeleton key={i} className="aspect-[4/5] rounded-2xl" />
          ))}
        </div>
      )}
      {isError && (
        <EmptyState
          icon={<ListMusic />}
          title={t('Could not load the playlists')}
          description={errorMessage(error)}
          action={<Button variant="secondary" onClick={() => void refetch()}>{t('Retry')}</Button>}
        />
      )}
      {data && shown.length === 0 && (
        <EmptyState
          icon={<ListMusic />}
          title={filter ? t('No playlist matches the filter') : t('No playlists yet')}
          description={filter ? undefined : t('Create one, or add tracks from any channel with "Add to playlist".')}
          action={!filter && <Button icon={<Plus />} onClick={() => setDialog({ kind: 'create' })}>{t('New playlist')}</Button>}
        />
      )}
      {shown.length > 0 && (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
          {shown.map((p) => (
            <PlaylistCard key={p.id} p={p} onDialog={setDialog} />
          ))}
        </div>
      )}

      {dialog?.kind === 'create' && <PlaylistNameDialog onClose={() => setDialog(null)} onSaved={(p) => navigate(`/playlists/${p.id}`)} />}
      {dialog?.kind === 'rename' && <PlaylistNameDialog playlist={dialog.playlist} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'download' && <DownloadPlaylistDialog playlist={dialog.playlist} onClose={() => setDialog(null)} />}
    </div>
  );
}
