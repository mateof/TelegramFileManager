import { baseName, FileManager, parentPath, type FileAction, type FileEntry } from '@tfm/file-manager';
import { CloudUpload, HardDrive } from 'lucide-react';
import { useMemo, useState } from 'react';
import { dtoOf } from '@/features/files/entries';
import { confirmDelete, fmLocale, reportError, reportSuccess } from '@/features/files/fileManagerOptions';
import { mediaActions, openMedia, type MediaSource } from '@/features/files/media';
import { localRel } from '@/features/files/local';
import { fileManagerStrings } from '@/features/files/strings';
import { useRoutePath } from '@/features/files/useRoutePath';
import { getLanguage, t } from '@/i18n';
import { api } from '@/lib/api';
import { PageHeader } from '@/shell/PageHeader';
import { useUi } from '@/stores/ui';
import { UploadToTelegramDialog } from '../components/UploadToTelegramDialog';
import { createLocalProvider } from '../lib/localProvider';

const strings = fileManagerStrings();

/** `/local/*`: the server local files (download target, upload source). */
export default function LocalFilesPage() {
  const [path, setPath] = useRoutePath('/local');
  const singleClickFolders = useUi((s) => s.singleClickFolders);
  const [uploading, setUploading] = useState<FileEntry[] | null>(null);
  const provider = useMemo(() => createLocalProvider(t('Local files')), []);

  const source: MediaSource = useMemo(
    () => ({
      local: true,
      sourceName: t('Local files'),
      inlineUrl: (e) => dtoOf(e).contentUrl ?? api.local.inlineUrl(e.id),
      downloadUrl: (e) => api.local.downloadUrl(e.id)
    }),
    []
  );

  const actions = useMemo<FileAction[]>(
    () => [
      {
        id: 'tfm:upload-telegram',
        label: t('Upload to Telegram'),
        icon: <CloudUpload />,
        group: 'a-open',
        order: 12,
        run: (entries) => setUploading(entries)
      },
      {
        id: 'tfm:upload-telegram-folder',
        label: t('Upload this folder to Telegram'),
        icon: <CloudUpload />,
        selection: 'none',
        placement: ['toolbar', 'context'],
        when: (_e, ctx) => ctx.currentPath !== '/',
        run: (_e, ctx) =>
          setUploading([
            { id: localRel(ctx.currentPath), name: baseName(ctx.currentPath), kind: 'folder', path: ctx.currentPath, parentPath: parentPath(ctx.currentPath) }
          ])
      },
      ...mediaActions(source)
    ],
    [source]
  );

  return (
    <div className="flex h-full min-h-0 flex-col">
      <PageHeader title={t('Local files')} description={t('Files stored on the server.')} icon={<HardDrive />} className="px-3 pt-3 sm:px-4 [&_p]:hidden sm:[&_p]:block" />
      <FileManager
        singleClickFolders={singleClickFolders}
        provider={provider}
        path={path}
        onPathChange={setPath}
        actions={actions}
        strings={strings}
        locale={fmLocale(getLanguage())}
        rootIcon={<HardDrive />}
        confirmDelete={(entries) => confirmDelete(entries)}
        onError={reportError}
        onSuccess={reportSuccess}
        onOpen={(entry, fm) => openMedia(entry, fm.entries, source)}
        className="mt-2 flex-1"
      />
      <UploadToTelegramDialog entries={uploading} onClose={() => setUploading(null)} />
    </div>
  );
}
