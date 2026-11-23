import { Button, Dialog, DialogContent, RadioGroup, toast } from '@tfm/ui';
import { HardDriveDownload } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { FolderBrowser } from './FolderBrowser';
import { createLocalFolder, listLocalFolders, localRel } from './local';

export interface DownloadToServerRequest {
  channelId: string;
  fileIds: string[];
  /** Imported shared collection the files come from. */
  collectionId?: string;
  /** Text of the dialog description (e.g. the file name or "3 items"). */
  label: string;
}

/** Pick a local folder and queue `POST /api/v1/transfers/downloads`. */
export function DownloadToServerDialog({ request, onClose }: { request: DownloadToServerRequest | null; onClose(): void }) {
  const navigate = useNavigate();
  const [mode, setMode] = useState<'structure' | 'folder'>('folder');
  const [path, setPath] = useState('/');
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    if (!request) return;
    setBusy(true);
    try {
      const res = await api.transfers.startDownload({
        channelId: request.channelId,
        fileIds: request.fileIds,
        targetPath: mode === 'structure' ? null : localRel(path),
        sharedCollectionId: request.collectionId ?? null
      });
      toast.success(t('{count} downloads queued', { count: res.accepted }), {
        description: res.skipped.length ? t('{count} could not be resolved', { count: res.skipped.length }) : undefined,
        action: { label: t('Transfers'), onClick: () => navigate('/transfers') }
      });
      onClose();
    } catch (error) {
      toast.error(t('Could not start the download'), { description: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!request} onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        size="md"
        title={t('Download to the server')}
        description={request?.label}
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              {t('Cancel')}
            </Button>
            <Button icon={<HardDriveDownload />} loading={busy} onClick={() => void submit()}>
              {t('Download')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <RadioGroup
            value={mode}
            onValueChange={(v) => setMode(v as 'structure' | 'folder')}
            options={[
              { value: 'folder', label: t('Into a folder of the local files') },
              { value: 'structure', label: t('Keep the folders of the channel') }
            ]}
          />
          {mode === 'folder' && (
            <FolderBrowser
              queryKey={['local']}
              rootName={t('Local files')}
              path={path}
              onPathChange={setPath}
              listFolders={listLocalFolders}
              createFolder={createLocalFolder}
              className="h-72"
            />
          )}
          {mode === 'folder' && (
            <p className="text-xs text-fg-muted">
              {t('Destination')}: <span className="font-medium text-fg">/{localRel(path)}</span>
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
