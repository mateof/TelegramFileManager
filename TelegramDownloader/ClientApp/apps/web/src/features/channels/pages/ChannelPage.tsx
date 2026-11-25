import { useQuery } from '@tanstack/react-query';
import { FileManager } from '@tfm/file-manager';
import { EmptyState } from '@tfm/ui';
import { AlertTriangle, FolderTree } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router';
import { DownloadToServerDialog } from '@/features/files/DownloadToServerDialog';
import { confirmDelete, fmLocale, reportError, reportSuccess } from '@/features/files/fileManagerOptions';
import { openMedia } from '@/features/files/media';
import { fileManagerStrings } from '@/features/files/strings';
import { useRoutePath } from '@/features/files/useRoutePath';
import { IdentifyDialog } from '@/features/library/components/IdentifyDialog';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { useUi } from '@/stores/ui';
import { ShareDialog, StrmDialog } from '../components/ChannelDialogs';
import { ChannelHeader, NotIndexed } from '../components/ChannelHeader';
import { channelActions, countLabel, type ChannelDialog } from '../lib/channelActions';
import { createChannelProvider } from '../lib/channelProvider';

const strings = fileManagerStrings();

/** `/channels/:id/*`: the file manager of a Telegram channel (the splat is the folder path, deep-linkable). */
export default function ChannelPage() {
  const channelId = useParams()['id'] ?? '';
  const [path, setPath] = useRoutePath(`/channels/${channelId}`);
  const singleClickFolders = useUi((s) => s.singleClickFolders);
  const detail = useQuery({
    queryKey: queryKeys.channel(channelId),
    queryFn: ({ signal }) => api.channels.get(channelId, signal),
    enabled: !!channelId
  });
  const name = detail.data?.name ?? t('Channel');
  const [dialog, setDialog] = useState<ChannelDialog | null>(null);

  useEffect(() => {
    if (detail.data?.name) document.title = `${detail.data.name} · TFM`;
  }, [detail.data?.name]);

  const provider = useMemo(() => createChannelProvider({ channelId, rootName: name }), [channelId, name]);
  const actions = useMemo(() => channelActions({ channelId, channelName: name, openDialog: setDialog }), [channelId, name]);

  let body = null;
  if (detail.error) {
    body = <EmptyState icon={<AlertTriangle />} title={t('Could not open the channel')} description={errorMessage(detail.error)} />;
  } else if (detail.data && !detail.data.hasDatabase) {
    body = <NotIndexed channelId={channelId} name={name} />;
  } else if (detail.data) {
    body = (
      <FileManager
        singleClickFolders={singleClickFolders}
        key={channelId}
        provider={provider}
        path={path}
        onPathChange={setPath}
        actions={actions}
        strings={strings}
        locale={fmLocale(getLanguage())}
        rootIcon={<FolderTree />}
        confirmDelete={(entries) => confirmDelete(entries, t('The Telegram messages are deleted too.'))}
        onError={reportError}
        onSuccess={reportSuccess}
        onOpen={(entry, fm) => openMedia(entry, fm.entries, { channelId, sourceName: name })}
        className="flex-1"
      />
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <ChannelHeader channelId={channelId} detail={detail.data} loading={detail.isLoading} />
      {body}
      <DownloadToServerDialog
        request={dialog?.type === 'download' ? { channelId, fileIds: dialog.entries.map((e) => e.id), label: countLabel(dialog.entries) } : null}
        onClose={() => setDialog(null)}
      />
      <StrmDialog channelId={channelId} request={dialog?.type === 'strm' ? dialog : null} onClose={() => setDialog(null)} />
      <IdentifyDialog
        target={dialog?.type === 'series' ? { type: 'folder', channelId, path: dialog.path, name: dialog.name } : null}
        onClose={() => setDialog(null)}
      />
      <ShareDialog channelId={channelId} request={dialog?.type === 'share' ? dialog : null} onClose={() => setDialog(null)} />
    </div>
  );
}
