import type { PlaylistModel } from '@tfm/api-client';
import { Button, Dialog, DialogContent, Field, Input, Textarea, toast } from '@tfm/ui';
import { useState, type FormEvent } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { storePlaylist } from './queries';

/** Create (no `playlist`) or rename/describe a playlist. */
export function PlaylistNameDialog({ playlist, onClose, onSaved }: { playlist?: PlaylistModel; onClose: () => void; onSaved?: (p: PlaylistModel) => void }) {
  const [name, setName] = useState(playlist?.name ?? '');
  const [description, setDescription] = useState(playlist?.description ?? '');
  const [saving, setSaving] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    setSaving(true);
    try {
      const saved = playlist
        ? await api.playlists.update(playlist.id, { name: name.trim(), description: description.trim() })
        : await api.playlists.create({ name: name.trim(), description: description.trim() || null });
      storePlaylist(saved);
      toast.success(playlist ? t('Playlist updated') : t('Playlist created'));
      onSaved?.(saved);
      onClose();
    } catch (err) {
      toast.error(playlist ? t('Could not update the playlist') : t('Could not create the playlist'), { description: errorMessage(err) });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        size="sm"
        title={playlist ? t('Edit playlist') : t('New playlist')}
        footer={
          <>
            <Button variant="ghost" onClick={onClose}>
              {t('Cancel')}
            </Button>
            <Button type="submit" form="tfm-playlist-name" loading={saving} disabled={!name.trim()}>
              {playlist ? t('Save') : t('Create')}
            </Button>
          </>
        }
      >
        <form id="tfm-playlist-name" onSubmit={submit} className="flex flex-col gap-4">
          <Field label={t('Name')}>{({ id }) => <Input id={id} autoFocus value={name} onChange={(e) => setName(e.target.value)} />}</Field>
          <Field label={t('Description')}>
            {({ id }) => <Textarea id={id} rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />}
          </Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Download every track of the playlist to the server's local storage. */
export function DownloadPlaylistDialog({ playlist, onClose }: { playlist: PlaylistModel; onClose: () => void }) {
  const [folder, setFolder] = useState(playlist.name);
  const [busy, setBusy] = useState(false);
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.playlists.download(playlist.id, { destinationFolder: folder.trim() || undefined });
      toast.success(t('Download started'), { description: t('Follow it in Transfers.') });
      onClose();
    } catch (err) {
      toast.error(t('Could not start the download'), { description: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        size="sm"
        title={t('Download to the server')}
        description={t('Copies the {count} tracks into the local storage of the server.', { count: playlist.trackCount })}
        footer={
          <>
            <Button variant="ghost" onClick={onClose}>
              {t('Cancel')}
            </Button>
            <Button type="submit" form="tfm-playlist-download" loading={busy}>
              {t('Download')}
            </Button>
          </>
        }
      >
        <form id="tfm-playlist-download" onSubmit={submit}>
          <Field label={t('Destination folder')} hint={t('Relative to the local storage root.')}>
            {({ id, describedBy }) => <Input id={id} aria-describedby={describedBy} value={folder} onChange={(e) => setFolder(e.target.value)} />}
          </Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}
