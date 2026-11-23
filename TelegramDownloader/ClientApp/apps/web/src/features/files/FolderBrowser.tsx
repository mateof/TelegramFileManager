import { useQuery } from '@tanstack/react-query';
import { pathSegments } from '@tfm/file-manager';
import { Breadcrumb, Button, cn, EmptyState, Input, Skeleton } from '@tfm/ui';
import { ChevronRight, Folder, FolderPlus, HardDrive } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';

export interface FolderItem {
  name: string;
  path: string;
}

export interface FolderBrowserProps {
  /** Cache key prefix (e.g. `['local']`, `['channel', 123]`). */
  queryKey: readonly unknown[];
  rootName: string;
  rootIcon?: ReactNode;
  path: string;
  onPathChange(path: string): void;
  listFolders(path: string, signal: AbortSignal): Promise<FolderItem[]>;
  /** Enables "New folder" inside the picker. */
  createFolder?(parent: string, name: string): Promise<void>;
  className?: string;
}

/** Compact folder navigator used by the "pick a destination" dialogs. */
export function FolderBrowser({ queryKey, rootName, rootIcon, path, onPathChange, listFolders, createFolder, className }: FolderBrowserProps) {
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: [...queryKey, 'folder-browser', path],
    queryFn: ({ signal }) => listFolders(path, signal)
  });
  const crumbs = [{ name: rootName, path: '/' }, ...pathSegments(path)].map((c, i) => ({
    label: c.name,
    icon: i === 0 ? rootIcon ?? <HardDrive /> : undefined,
    onSelect: () => onPathChange(c.path)
  }));

  const submit = async () => {
    if (!createFolder || !name.trim()) return;
    setBusy(true);
    try {
      await createFolder(path, name.trim());
      setName('');
      setCreating(false);
      await refetch();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={cn('flex min-h-0 flex-col overflow-hidden rounded-xl border border-border bg-surface-2/40', className)}>
      <div className="flex items-center gap-2 border-b border-border px-2 py-1.5">
        <Breadcrumb items={crumbs} maxItems={4} className="flex-1" />
        {createFolder && (
          <Button size="xs" variant="ghost" icon={<FolderPlus />} onClick={() => setCreating((v) => !v)}>
            {t('New folder')}
          </Button>
        )}
      </div>
      {creating && (
        <form
          className="flex gap-2 border-b border-border p-2"
          onSubmit={(e) => {
            e.preventDefault();
            void submit();
          }}
        >
          <Input inputSize="sm" autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder={t('Folder name')} aria-label={t('Folder name')} />
          <Button size="sm" type="submit" loading={busy} disabled={!name.trim()}>
            {t('Create')}
          </Button>
        </form>
      )}
      <ul className="min-h-40 flex-1 overflow-y-auto p-1">
        {isLoading &&
          Array.from({ length: 4 }, (_, i) => (
            <li key={i} className="flex h-9 items-center gap-2 px-2">
              <Skeleton className="size-5 rounded" />
              <Skeleton className="h-3.5 w-1/2" />
            </li>
          ))}
        {error && <EmptyState title={t('Could not load this folder')} description={errorMessage(error)} className="py-6" />}
        {data?.length === 0 && <li className="px-3 py-6 text-center text-sm text-fg-subtle">{t('No sub-folders')}</li>}
        {data?.map((f) => (
          <li key={f.path}>
            <button
              type="button"
              onClick={() => onPathChange(f.path)}
              className="flex h-9 w-full items-center gap-2.5 rounded-lg px-2 text-left text-sm text-fg transition-colors hover:bg-surface-2"
            >
              <Folder className="size-4 shrink-0 text-accent-300" />
              <span className="min-w-0 flex-1 truncate">{f.name}</span>
              <ChevronRight className="size-4 text-fg-subtle" />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
