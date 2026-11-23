import type { SharedCollectionDto } from '@tfm/api-client';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Card, confirm, EmptyState, IconButton, Input, Skeleton, toast } from '@tfm/ui';
import { FileUp, FolderOpen, Search, Share2, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import { Link } from 'react-router';
import { ImportProgressView } from '@/features/files/ImportProgress';
import { useImportProgress } from '@/features/files/useImportProgress';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatRelative } from '@/lib/format';
import { PageHeader } from '@/shell/PageHeader';

const SHARES_KEY = ['shared', 'list'] as const;

async function loadAll(signal: AbortSignal): Promise<SharedCollectionDto[]> {
  const all: SharedCollectionDto[] = [];
  for (let page = 1; page <= 20; page++) {
    const res = await api.shares.list({ page, pageSize: 200 }, signal);
    all.push(...res.items);
    if (!res.page.hasNext) break;
  }
  return all;
}

/** Sends a `.tfm` (zip from the classic UI) or a `.json` share file (from this app) to the import endpoint. */
async function importShareFile(file: File) {
  if (file.name.toLowerCase().endsWith('.json')) {
    const parsed = JSON.parse(await file.text()) as { share?: unknown };
    const share = (parsed.share ?? parsed) as Parameters<typeof api.shares.import>[0]['share'];
    return api.shares.import({ share });
  }
  return api.shares.importFile(file);
}

/** `/shared`: shared collections imported on this server, and the import of share files. */
export default function SharedIndexPage() {
  const qc = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: SHARES_KEY, queryFn: ({ signal }) => loadAll(signal) });
  const [filter, setFilter] = useState('');
  const [importId, setImportId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const progress = useImportProgress(importId, (e) => {
    if (!e.error) toast.success(t('Share imported'));
    void qc.invalidateQueries({ queryKey: SHARES_KEY });
  });

  const onFile = async (file: File | undefined) => {
    if (!file) return;
    setBusy(true);
    try {
      const res = await importShareFile(file);
      setImportId(res?.importId ?? null);
    } catch (err) {
      toast.error(t('Could not import the share'), { description: errorMessage(err) });
    } finally {
      setBusy(false);
    }
  };

  const remove = async (c: SharedCollectionDto) => {
    const ok = await confirm({
      title: t('Remove "{name}"?', { name: c.name ?? c.id }),
      description: t('The collection is removed from this server. The files stay on Telegram.'),
      confirmLabel: t('Remove'),
      destructive: true
    });
    if (!ok) return;
    try {
      await api.shares.delete(c.id);
      void qc.invalidateQueries({ queryKey: SHARES_KEY });
    } catch (err) {
      toast.error(t('Could not remove the collection'), { description: errorMessage(err) });
    }
  };

  const q = filter.trim().toLocaleLowerCase();
  const list = (data ?? []).filter((c) => !q || (c.name ?? '').toLocaleLowerCase().includes(q) || (c.description ?? '').toLocaleLowerCase().includes(q));

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={t('Shared collections')}
        description={t('Collections other people shared with you.')}
        icon={<Share2 />}
        actions={
          <Button icon={<FileUp />} loading={busy} onClick={() => input.current?.click()}>
            {t('Import share')}
          </Button>
        }
      />
      <input
        ref={input}
        type="file"
        accept=".tfm,.json,application/json,application/zip"
        hidden
        onChange={(e) => {
          void onFile(e.target.files?.[0]);
          e.target.value = '';
        }}
      />
      <ImportProgressView progress={progress} started={!!importId} />

      <Input leading={<Search />} value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t('Filter collections...')} aria-label={t('Filter collections...')} className="max-w-sm" />

      {isLoading ? (
        <div className="flex flex-col gap-2">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-16 rounded-2xl" />
          ))}
        </div>
      ) : error ? (
        <Card glass>
          <EmptyState title={t('Could not load the collections')} description={errorMessage(error)} />
        </Card>
      ) : list.length === 0 ? (
        <Card glass>
          <EmptyState
            icon={<Share2 />}
            title={t('No shared collections')}
            description={t('Import a share file (.tfm or .json) that another TFM server exported.')}
          />
        </Card>
      ) : (
        <ul className="flex flex-col gap-2">
          {list.map((c) => (
            <li key={c.id}>
              <Card className="flex items-center gap-3 p-3.5">
                <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-accent-300">
                  <Share2 className="size-5" />
                </span>
                <Link to={`/shared/${c.id}`} className="min-w-0 flex-1 rounded focus-visible:outline-2 focus-visible:outline-ring">
                  <p className="truncate text-sm font-semibold text-fg">{c.name || c.collectionId || c.id}</p>
                  <p className="truncate text-xs text-fg-muted">
                    {[c.description, formatRelative(c.dateModified || c.dateCreated, getLanguage())].filter(Boolean).join(' · ')}
                  </p>
                </Link>
                <Button size="sm" variant="secondary" icon={<FolderOpen />} asChild>
                  <Link to={`/shared/${c.id}`}>{t('Open')}</Link>
                </Button>
                <IconButton label={t('Remove')} size="sm" className="text-danger hover:bg-danger-soft hover:text-danger" onClick={() => void remove(c)}>
                  <Trash2 />
                </IconButton>
              </Card>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
