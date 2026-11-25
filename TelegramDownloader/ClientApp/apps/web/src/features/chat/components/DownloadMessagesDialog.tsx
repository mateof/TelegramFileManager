import { Button, Dialog, DialogContent } from '@tfm/ui';
import { HardDriveDownload } from 'lucide-react';
import { useState } from 'react';
import { FolderBrowser } from '@/features/files/FolderBrowser';
import { createLocalFolder, listLocalFolders, localRel } from '@/features/files/local';
import { t } from '@/i18n';
import { downloadMessages } from '../chatActions';

/** Pick a local folder for the files of some messages and queue them. */
export function DownloadMessagesDialog({ channelId, ids, onClose, onQueued }: { channelId: string; ids: number[] | null; onClose: () => void; onQueued: () => void }) {
  const [path, setPath] = useState('/');
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    if (!ids) return;
    setBusy(true);
    const queued = await downloadMessages(channelId, ids, `/${localRel(path)}`);
    setBusy(false);
    if (!queued) return;
    onClose();
    onQueued();
  };
  return (
    <Dialog open={!!ids} onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        size="md"
        title={t('Download to the server')}
        description={ids ? t('{count} files', { count: ids.length }) : undefined}
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
          <FolderBrowser queryKey={['local']} rootName={t('Local files')} path={path} onPathChange={setPath} listFolders={listLocalFolders} createFolder={createLocalFolder} className="h-72" />
          <p className="text-xs text-fg-muted">
            {t('Destination')}: <span className="font-medium text-fg">/{localRel(path)}</span>
          </p>
        </div>
      </DialogContent>
    </Dialog>
  );
}
