import type { ShareFilesModel } from '@tfm/api-client';
import { fileManagerQueryKey } from '@tfm/file-manager';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Dialog, DialogContent, Field, IconButton, Input, RadioGroup, Textarea, toast } from '@tfm/ui';
import { Clapperboard, Copy, FileUp, Share2 } from 'lucide-react';
import { useState } from 'react';
import { absoluteUrl, copyText, downloadUrls, saveBlob } from '@/features/files/browser';
import { toApiFolder } from '@/features/files/entries';
import { FolderBrowser } from '@/features/files/FolderBrowser';
import { ImportProgressView } from '@/features/files/ImportProgress';
import { useImportProgress } from '@/features/files/useImportProgress';
import { createLocalFolder, listLocalFolders, localRel } from '@/features/files/local';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { channelProviderId } from '../lib/channelProvider';

function CopyField({ value, label }: { value: string; label: string }) {
  return (
    <Field label={label}>
      {({ id }) => (
        <Input
          id={id}
          readOnly
          value={value}
          onFocus={(e) => e.target.select()}
          trailing={
            <IconButton
              label={t('Copy')}
              size="xs"
              onClick={() =>
                copyText(value).then(
                  () => toast.success(t('Copied to the clipboard')),
                  () => toast.error(t('Could not copy'))
                )
              }
            >
              <Copy />
            </IconButton>
          }
        />
      )}
    </Field>
  );
}

/** Emby/Kodi/Jellyfin `.strm` export of a channel folder: zip download or written to the local files. */
export function StrmDialog({ channelId, request, onClose }: { channelId: string; request: { path: string; name: string } | null; onClose(): void }) {
  const [mode, setMode] = useState<'zip' | 'local'>('zip');
  const [host, setHost] = useState('');
  const [dest, setDest] = useState('/');
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    if (!request) return;
    setBusy(true);
    try {
      const res = await api.shares.createStrm(channelId, {
        path: toApiFolder(request.path),
        host: host.trim() || null,
        destinationFolder: mode === 'local' ? localRel(dest) || '/' : null
      });
      if (mode === 'local') toast.success(t('STRM files written to the local files'));
      else if (res) {
        await downloadUrls([{ url: absoluteUrl(res), name: `${request.name || 'strm'}.zip` }]);
        toast.success(t('STRM archive created'));
      }
      onClose();
    } catch (error) {
      toast.error(t('Could not create the STRM files'), { description: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!request} onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('Export STRM files')}
        description={t('Media servers (Emby, Jellyfin, Kodi) can stream the files of "{name}" through these links.', { name: request?.name ?? '' })}
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              {t('Cancel')}
            </Button>
            <Button icon={<Clapperboard />} loading={busy} onClick={() => void submit()}>
              {t('Export')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <RadioGroup
            value={mode}
            onValueChange={(v) => setMode(v as 'zip' | 'local')}
            options={[
              { value: 'zip', label: t('Download a zip archive') },
              { value: 'local', label: t('Write them to a folder of the local files') }
            ]}
          />
          {mode === 'local' && (
            <FolderBrowser
              queryKey={['local']}
              rootName={t('Local files')}
              path={dest}
              onPathChange={setDest}
              listFolders={listLocalFolders}
              createFolder={createLocalFolder}
              className="h-56"
            />
          )}
          <Field label={t('Server address written in the files')} hint={t('Leave it empty to use the address of this page.')}>
            {({ id }) => <Input id={id} value={host} onChange={(e) => setHost(e.target.value)} placeholder={window.location.origin} />}
          </Field>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Builds a share of a folder (or the channel): a file another TFM instance imports in "Shared". */
export function ShareDialog({ channelId, request, onClose }: { channelId: string; request: { folderId?: string; name: string } | null; onClose(): void }) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [share, setShare] = useState<ShareFilesModel | null>(null);
  const [seen, setSeen] = useState(request);
  if (seen !== request) {
    setSeen(request);
    setName(request?.name ?? '');
    setDescription('');
    setShare(null);
  }

  const submit = async () => {
    if (!request) return;
    setBusy(true);
    try {
      const res = await api.shares.export({ channelId, folderId: request.folderId, name: name.trim() || request.name });
      const payload = { ...res, description: description.trim() || res.description };
      setShare(payload);
      saveBlob(new Blob([JSON.stringify({ share: payload })], { type: 'application/json' }), `${(name.trim() || request.name).replace(/[\\/:*?"<>|]/g, '_')}.tfm.json`);
      toast.success(t('Share file saved'));
    } catch (error) {
      toast.error(t('Could not create the share'), { description: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!request} onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('Share')}
        description={t('Creates a share file with the list of files and an invitation to the channel. The files stay on Telegram.')}
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              {share ? t('Close') : t('Cancel')}
            </Button>
            {!share && (
              <Button icon={<Share2 />} loading={busy} onClick={() => void submit()}>
                {t('Create share file')}
              </Button>
            )}
          </>
        }
      >
        {share ? (
          <div className="flex flex-col gap-3">
            <p className="text-sm text-fg-muted">
              {t('{count} files in the share. Import the file in "Shared" on the other server.', { count: share.files?.length ?? 0 })}
            </p>
            {share.invitation?.invitationLink && <CopyField label={t('Channel invitation')} value={share.invitation.invitationLink} />}
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <Field label={t('Name')}>{({ id }) => <Input id={id} value={name} onChange={(e) => setName(e.target.value)} />}</Field>
            <Field label={t('Description')}>{({ id }) => <Textarea id={id} value={description} onChange={(e) => setDescription(e.target.value)} />}</Field>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

/** Imports an exported channel index (JSON) with live progress. */
export function ImportIndexDialog({ channelId, open, onOpenChange }: { channelId: string; open: boolean; onOpenChange(open: boolean): void }) {
  const qc = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [importId, setImportId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const progress = useImportProgress(importId, (e) => {
    if (e.error) return;
    void qc.invalidateQueries({ queryKey: fileManagerQueryKey(channelProviderId(channelId)) });
    void qc.invalidateQueries({ queryKey: queryKeys.channel(channelId) });
  });

  const start = async () => {
    if (!file) return;
    setBusy(true);
    try {
      const res = await api.files.import(channelId, file);
      setImportId(res?.importId ?? 'unknown');
    } catch (error) {
      toast.error(t('Could not import the index'), { description: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  const close = (o: boolean) => {
    onOpenChange(o);
    if (!o && (!importId || progress?.done)) {
      setImportId(null);
      setFile(null);
    }
  };

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent
        title={t('Import index')}
        description={t('Rebuilds the index from a file exported by another server. The files are read from Telegram.')}
        footer={
          <>
            <Button variant="secondary" onClick={() => close(false)}>
              {t('Close')}
            </Button>
            {!importId && (
              <Button icon={<FileUp />} loading={busy} disabled={!file} onClick={() => void start()}>
                {t('Import')}
              </Button>
            )}
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {!importId && (
            <Field label={t('Index file (.json)')}>
              {({ id }) => (
                <input
                  id={id}
                  type="file"
                  accept=".json,application/json"
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                  className="text-sm text-fg-muted file:mr-3 file:rounded-lg file:border-0 file:bg-surface-3 file:px-3 file:py-1.5 file:text-sm file:text-fg"
                />
              )}
            </Field>
          )}
          <ImportProgressView progress={progress} started={!!importId} />
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** The WebDAV address of the channel. */
export function WebDavDialog({ channelId, open, onOpenChange }: { channelId: string; open: boolean; onOpenChange(open: boolean): void }) {
  const url = `${window.location.origin}/webdav/${channelId}/`;
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={t('WebDAV address')} description={t('Mount the channel as a network drive from any WebDAV client.')}>
        <div className="flex flex-col gap-3 pb-2">
          <CopyField label={t('Address')} value={url} />
          <p className="text-xs text-fg-muted">{t('The user and password are the WebDAV credentials of the settings (Basic auth).')}</p>
        </div>
      </DialogContent>
    </Dialog>
  );
}
