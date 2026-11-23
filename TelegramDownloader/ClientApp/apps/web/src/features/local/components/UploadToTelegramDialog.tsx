import type { UploadPreviewResult } from '@tfm/api-client';
import { useQuery } from '@tanstack/react-query';
import type { FileEntry } from '@tfm/file-manager';
import { Badge, Button, Checkbox, Dialog, DialogContent, Field, Skeleton, toast } from '@tfm/ui';
import { AlertTriangle, CloudUpload, Copy, FolderTree } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { toApiFolder, toEntry } from '@/features/files/entries';
import { FolderBrowser } from '@/features/files/FolderBrowser';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { ChannelCombobox } from '@/shell/ChannelCombobox';
import { useChannels } from '@/shell/useChannels';

async function listChannelFolders(channelId: string, path: string, signal: AbortSignal) {
  const res = await api.files.list(channelId, { path: toApiFolder(path), page: 1, pageSize: 1000, sortBy: 'name' }, signal);
  return res.contents.items.filter((i) => !i.isFile).map((dto) => {
    const e = toEntry(dto);
    return { name: e.name, path: e.path };
  });
}

const ACTION_TONE: Record<string, 'success' | 'warning' | 'neutral' | 'info'> = {
  new: 'success',
  replace: 'warning',
  'skip-empty': 'neutral',
  'skip-newer': 'neutral',
  'create-folder': 'info',
  'existing-folder': 'neutral'
};

function actionLabel(action: string): string {
  switch (action) {
    case 'new':
      return t('New');
    case 'replace':
      return t('Replace');
    case 'skip-empty':
      return t('Empty, skipped');
    case 'skip-newer':
      return t('Newer in the channel, skipped');
    case 'create-folder':
      return t('New folder');
    case 'existing-folder':
      return t('Existing folder');
    default:
      return action;
  }
}

function Num({ label, value, hint, tone }: { label: string; value: string | number; hint?: string; tone?: string }) {
  return (
    <div className="rounded-xl border border-border bg-surface-2/50 px-3 py-2">
      <p className="text-[11px] font-medium uppercase tracking-wider text-fg-subtle">{label}</p>
      <p className={`text-base font-semibold tabular-nums ${tone ?? 'text-fg'}`}>{value}</p>
      {hint && <p className="text-[11px] text-fg-muted">{hint}</p>}
    </div>
  );
}

function PreviewSummary({ preview }: { preview: UploadPreviewResult }) {
  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Num label={t('To upload')} value={preview.filesToUpload} hint={formatBytes(preview.bytesToUpload)} tone="text-accent-300" />
        <Num label={t('New')} value={preview.newFiles} hint={formatBytes(preview.newBytes)} />
        <Num label={t('Replaced')} value={preview.replacedFiles} hint={formatBytes(preview.replacedBytes)} />
        <Num label={t('Skipped')} value={preview.skippedFiles} hint={t('{empty} empty, {newer} newer in the channel', { empty: preview.skippedEmpty, newer: preview.skippedNewerInChannel })} />
        <Num label={t('Folders to create')} value={preview.foldersToCreate} hint={t('{count} existing', { count: preview.existingFolders })} />
        {preview.duplicateCheck && <Num label={t('Duplicates')} value={preview.duplicates.length} hint={t('{count} files hashed', { count: preview.hashedFiles })} />}
      </div>
      {!preview.targetFolderExists && (
        <p className="flex items-start gap-2 rounded-xl border border-warning/30 bg-warning-soft px-3 py-2 text-xs text-warning">
          <AlertTriangle className="mt-0.5 size-3.5 shrink-0" />
          {t('The destination folder is not indexed: the files will go under the channel root.')}
        </p>
      )}
      {preview.missingLocalPaths.length > 0 && (
        <p className="rounded-xl border border-danger/30 bg-danger-soft px-3 py-2 text-xs text-danger">
          {t('Not found on the server: {paths}', { paths: preview.missingLocalPaths.join(', ') })}
        </p>
      )}
      {preview.duplicates.length > 0 && (
        <div className="rounded-xl border border-warning/30 bg-warning-soft/40 p-2">
          <p className="mb-1 flex items-center gap-1.5 px-1 text-xs font-semibold text-warning">
            <Copy className="size-3.5" /> {t('Already in the channel at another path')}
          </p>
          <ul className="max-h-32 overflow-y-auto text-xs">
            {preview.duplicates.map((d) => (
              <li key={d.localPath} className="truncate px-1 py-0.5 text-fg-muted">
                <span className="text-fg">{d.localPath}</span> → {d.matches.map((m) => m.filePath).join(', ')}
              </li>
            ))}
          </ul>
        </div>
      )}
      {preview.items.length > 0 && (
        <ul className="max-h-48 divide-y divide-border/60 overflow-y-auto rounded-xl border border-border text-xs">
          {preview.items.map((i) => (
            <li key={i.localPath} className="flex items-center gap-2 px-2.5 py-1.5">
              <span className="min-w-0 flex-1 truncate text-fg" title={i.targetPath}>
                {i.localPath}
              </span>
              {i.isFile && <span className="tabular-nums text-fg-subtle">{formatBytes(i.size)}</span>}
              <Badge tone={ACTION_TONE[i.action] ?? 'neutral'}>{actionLabel(i.action)}</Badge>
            </li>
          ))}
          {preview.itemsTruncated && <li className="px-2.5 py-1.5 text-fg-subtle">{t('Only the first items are listed.')}</li>}
        </ul>
      )}
    </div>
  );
}

/** Upload local files/folders to a channel: pick channel + folder, review the dry run, confirm. */
export function UploadToTelegramDialog({ entries, onClose }: { entries: FileEntry[] | null; onClose(): void }) {
  const navigate = useNavigate();
  const channels = useChannels();
  const targets = (channels.data ?? []).filter((c) => c.hasDatabase);
  const [channelId, setChannelId] = useState('');
  const [target, setTarget] = useState('/');
  const [findDuplicates, setFindDuplicates] = useState(false);
  const [busy, setBusy] = useState(false);
  const localPaths = entries?.map((e) => e.id) ?? [];

  const preview = useQuery({
    queryKey: ['local', 'upload-preview', channelId, target, findDuplicates, localPaths],
    queryFn: ({ signal }) =>
      api.transfers.previewUpload({ channelId, localPaths, targetPath: toApiFolder(target), findDuplicates, maxItems: 300 }, signal),
    enabled: !!entries && !!channelId && localPaths.length > 0,
    staleTime: 0,
    gcTime: 0
  });

  const submit = async () => {
    setBusy(true);
    try {
      await api.transfers.startUpload({ channelId, localPaths, targetPath: toApiFolder(target) });
      toast.success(t('Upload to Telegram queued'), { action: { label: t('Transfers'), onClick: () => navigate('/transfers') } });
      onClose();
    } catch (error) {
      toast.error(t('Could not start the upload'), { description: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  const channelName = targets.find((c) => String(c.id) === channelId)?.name ?? '';
  return (
    <Dialog open={!!entries} onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        size="lg"
        title={t('Upload to Telegram')}
        description={entries?.length === 1 ? entries[0].name : t('{count} items', { count: entries?.length ?? 0 })}
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              {t('Cancel')}
            </Button>
            <Button
              icon={<CloudUpload />}
              loading={busy}
              disabled={!preview.data || preview.data.filesToUpload === 0 || preview.isFetching}
              onClick={() => void submit()}
            >
              {preview.data ? t('Upload {count} files', { count: preview.data.filesToUpload }) : t('Upload')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <Field label={t('Channel')}>
            {({ id }) => (
              <ChannelCombobox
                id={id}
                channels={targets}
                value={channelId}
                onChange={(v) => {
                  setChannelId(v);
                  setTarget('/');
                }}
                loading={channels.isLoading}
                placeholder={t('Choose a channel')}
              />
            )}
          </Field>
          {channelId && (
            <>
              <FolderBrowser
                queryKey={['channels', channelId]}
                rootName={channelName}
                rootIcon={<FolderTree />}
                path={target}
                onPathChange={setTarget}
                listFolders={(p, signal) => listChannelFolders(channelId, p, signal)}
                createFolder={async (parent, name) => {
                  await api.files.createFolder(channelId, { path: toApiFolder(parent), name });
                }}
                className="h-52"
              />
              <Checkbox
                label={t('Also look for files already in the channel at another path (reads and hashes the files: slower)')}
                checked={findDuplicates}
                onCheckedChange={(v) => setFindDuplicates(v === true)}
              />
              <div aria-live="polite">
                {preview.isFetching && (
                  <div className="flex flex-col gap-2">
                    <p className="text-xs text-fg-muted">{findDuplicates ? t('Checking the files and their content...') : t('Checking the files...')}</p>
                    <Skeleton className="h-16 w-full rounded-xl" />
                  </div>
                )}
                {preview.error && <p className="text-sm text-danger">{errorMessage(preview.error)}</p>}
                {preview.data && !preview.isFetching && <PreviewSummary preview={preview.data} />}
              </div>
            </>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
