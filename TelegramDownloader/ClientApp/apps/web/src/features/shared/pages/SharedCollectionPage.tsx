import { useQuery } from '@tanstack/react-query';
import { FileManager } from '@tfm/file-manager';
import { Button, EmptyState, Skeleton } from '@tfm/ui';
import { AlertTriangle, ArrowLeft, Share2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { channelActions, countLabel, type ChannelDialog } from '@/features/channels/lib/channelActions';
import { createChannelProvider } from '@/features/channels/lib/channelProvider';
import { DownloadToServerDialog } from '@/features/files/DownloadToServerDialog';
import { fmLocale, reportError } from '@/features/files/fileManagerOptions';
import { openMedia } from '@/features/files/media';
import { fileManagerStrings } from '@/features/files/strings';
import { useRoutePath } from '@/features/files/useRoutePath';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';

const strings = fileManagerStrings();

/** `/shared/:id/*`: read-only browsing of an imported shared collection. */
export default function SharedCollectionPage() {
  const id = useParams()['id'] ?? '';
  const [path, setPath] = useRoutePath(`/shared/${id}`);
  const { data: collection, isLoading, error } = useQuery({
    queryKey: ['shared', 'detail', id],
    queryFn: ({ signal }) => api.shares.get(id, signal),
    enabled: !!id
  });
  const [dialog, setDialog] = useState<ChannelDialog | null>(null);
  const name = collection?.name || collection?.collectionId || t('Shared collection');
  const channelId = collection?.channelId ?? '';
  const collectionId = collection?.collectionId ?? undefined;

  useEffect(() => {
    document.title = `${name} · TFM`;
  }, [name]);

  const provider = useMemo(
    () => (channelId && collectionId ? createChannelProvider({ channelId, collectionId, rootName: name }) : null),
    [channelId, collectionId, name]
  );
  const actions = useMemo(() => channelActions({ channelId, channelName: name, collectionId, openDialog: setDialog }), [channelId, name, collectionId]);

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex shrink-0 items-center gap-3 border-b border-border px-3 py-2.5 sm:px-4">
        <Button size="sm" variant="ghost" icon={<ArrowLeft />} asChild>
          <Link to="/shared" aria-label={t('Shared collections')}>
            <span className="hidden sm:inline">{t('Shared collections')}</span>
          </Link>
        </Button>
        <span className="flex size-9 items-center justify-center rounded-xl bg-primary-soft text-accent-300">
          <Share2 className="size-4" />
        </span>
        <div className="min-w-0 flex-1">
          {isLoading ? <Skeleton className="h-5 w-40" /> : <h1 className="truncate text-base font-semibold text-fg">{name}</h1>}
          {collection?.description && <p className="truncate text-xs text-fg-muted">{collection.description}</p>}
        </div>
      </div>
      {error ? (
        <EmptyState icon={<AlertTriangle />} title={t('Could not open the collection')} description={errorMessage(error)} />
      ) : provider ? (
        <FileManager
          key={provider.id}
          provider={provider}
          path={path}
          onPathChange={setPath}
          actions={actions}
          strings={strings}
          locale={fmLocale(getLanguage())}
          rootIcon={<Share2 />}
          onError={reportError}
          onOpen={(entry, fm) => openMedia(entry, fm.entries, { channelId, sourceName: name })}
          className="flex-1"
        />
      ) : null}
      <DownloadToServerDialog
        request={
          dialog?.type === 'download' && channelId
            ? { channelId, collectionId, fileIds: dialog.entries.map((e) => e.id), label: countLabel(dialog.entries) }
            : null
        }
        onClose={() => setDialog(null)}
      />
    </div>
  );
}
