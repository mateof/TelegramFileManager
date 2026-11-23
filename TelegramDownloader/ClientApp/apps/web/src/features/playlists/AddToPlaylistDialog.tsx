import type { PlaylistModel } from '@tfm/api-client';
import { Button, cn, Dialog, DialogContent, Input, Skeleton, toast } from '@tfm/ui';
import { Check, ListMusic, Plus, Search } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { candidateToWrite, trackKey } from './convert';
import { usePlaylistPicker } from './pickerStore';
import type { PlaylistCandidate } from './public';
import { invalidatePlaylists, usePlaylists } from './queries';

async function addAll(playlist: PlaylistModel, items: PlaylistCandidate[], onProgress: (done: number) => void) {
  const existing = new Set(playlist.tracks.map(trackKey));
  let added = 0;
  let skipped = 0;
  for (const item of items) {
    const body = candidateToWrite(item);
    if (existing.has(trackKey(body))) {
      skipped++;
      continue;
    }
    await api.playlists.addTrack(playlist.id, body);
    existing.add(trackKey(body));
    added++;
    onProgress(added + skipped);
  }
  return { added, skipped };
}

/** "Add to playlist" picker: choose a playlist or create one inline. */
export default function AddToPlaylistDialog() {
  const items = usePlaylistPicker((s) => s.items);
  const open = usePlaylistPicker((s) => s.open);
  const close = () => usePlaylistPicker.setState({ open: false });
  const { data: playlists, isLoading } = usePlaylists('date');
  const [filter, setFilter] = useState('');
  const [newName, setNewName] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [progress, setProgress] = useState(0);

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase();
    return (playlists ?? []).filter((p) => !q || p.name.toLowerCase().includes(q));
  }, [playlists, filter]);

  const pick = async (playlist: PlaylistModel) => {
    setBusy(playlist.id);
    setProgress(0);
    try {
      const { added, skipped } = await addAll(playlist, items, setProgress);
      if (added > 0) toast.success(t('{count} tracks added to "{name}"', { count: added, name: playlist.name }));
      else toast(t('Already in "{name}"', { name: playlist.name }));
      if (skipped > 0 && added > 0) toast(t('{count} tracks were already there', { count: skipped }));
      close();
    } catch (e) {
      toast.error(t('Could not add to the playlist'), { description: errorMessage(e) });
    } finally {
      setBusy(null);
      void invalidatePlaylists();
    }
  };

  const create = async (e: FormEvent) => {
    e.preventDefault();
    const name = newName.trim();
    if (!name) return;
    setBusy('new');
    try {
      const created = await api.playlists.create({ name, tracks: items.map(candidateToWrite) });
      toast.success(t('Playlist "{name}" created with {count} tracks', { name: created.name, count: items.length }));
      close();
    } catch (err) {
      toast.error(t('Could not create the playlist'), { description: errorMessage(err) });
    } finally {
      setBusy(null);
      void invalidatePlaylists();
    }
  };

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !busy && close()}>
      <DialogContent
        size="sm"
        title={t('Add to playlist')}
        description={items.length === 1 ? items[0].fileName : t('{count} tracks', { count: items.length })}
      >
        <form onSubmit={create} className="flex gap-2">
          <Input
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            placeholder={t('New playlist name')}
            aria-label={t('New playlist name')}
            leading={<Plus />}
          />
          <Button type="submit" disabled={!newName.trim() || !!busy} loading={busy === 'new'}>
            {t('Create')}
          </Button>
        </form>

        {(playlists?.length ?? 0) > 6 && (
          <Input
            className="mt-3"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder={t('Filter playlists')}
            aria-label={t('Filter playlists')}
            leading={<Search />}
          />
        )}

        <ul className="-mx-2 mt-3 flex max-h-80 flex-col gap-0.5 overflow-y-auto">
          {isLoading &&
            Array.from({ length: 4 }, (_, i) => (
              <li key={i} className="px-2 py-1">
                <Skeleton className="h-11 w-full rounded-xl" />
              </li>
            ))}
          {!isLoading && shown.length === 0 && <li className="px-3 py-6 text-center text-sm text-fg-muted">{t('No playlists yet')}</li>}
          {shown.map((p) => {
            const has = items.length === 1 && p.tracks.some((tr) => trackKey(tr) === trackKey(candidateToWrite(items[0])));
            return (
              <li key={p.id}>
                <button
                  type="button"
                  disabled={!!busy}
                  onClick={() => void pick(p)}
                  className={cn(
                    'flex w-full items-center gap-3 rounded-xl px-2 py-1.5 text-left transition-colors hover:bg-surface-2 focus-visible:outline-2 focus-visible:outline-ring disabled:opacity-60'
                  )}
                >
                  <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary-soft text-accent-300">
                    <ListMusic className="size-5" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-fg">{p.name}</span>
                    <span className="block text-xs text-fg-muted">{t('{count} tracks', { count: p.trackCount })}</span>
                  </span>
                  {busy === p.id ? (
                    <span className="text-xs tabular-nums text-fg-muted">
                      {progress}/{items.length}
                    </span>
                  ) : (
                    has && <Check className="size-4 text-success" aria-label={t('Already added')} />
                  )}
                </button>
              </li>
            );
          })}
        </ul>
      </DialogContent>
    </Dialog>
  );
}
