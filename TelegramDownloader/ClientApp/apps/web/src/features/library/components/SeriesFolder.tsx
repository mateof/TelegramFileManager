import { useMutation, useQuery } from '@tanstack/react-query';
import { Badge, Button, Checkbox, Select, toast } from '@tfm/ui';
import { FolderTree, Unlink } from 'lucide-react';
import { useId } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { folderChoices } from '../libraryModel';
import { libraryKeys, useFollowScan } from '../queries';

export interface SeriesFolderProps {
  channelId: number | string;
  /** Folder it was opened from: it and its parents can be chosen. */
  folderPath: string;
  path: string;
  onPathChange: (path: string) => void;
  includeLocked: boolean;
  onIncludeLockedChange: (value: boolean) => void;
}

/** Which folder is assigned to the series, what it has now and the exceptions. */
export function SeriesFolder({ channelId, folderPath, path, onPathChange, includeLocked, onIncludeLockedChange }: SeriesFolderProps) {
  const id = useId();
  const followScan = useFollowScan();
  const folder = useQuery({
    queryKey: libraryKeys.folder(channelId, path),
    queryFn: ({ signal }) => api.library.folder(channelId, path, signal)
  });
  const remove = useMutation({
    mutationFn: (rulePath: string) => api.library.unassignFolder(channelId, rulePath),
    onSuccess: () => {
      toast.success(t('The folder is no longer assigned'), { description: t('Its files are being identified again, each by its name.') });
      void folder.refetch();
      followScan();
    },
    onError: (error) => toast.error(t('The change could not be saved'), { description: errorMessage(error) })
  });
  const rule = folder.data?.effectiveRule;
  const assigned = rule?.providerId ? rule : null;

  return (
    <div className="flex flex-col gap-2.5 rounded-xl border border-border bg-surface-2/50 p-3 text-sm">
      <div className="flex flex-col gap-1.5 sm:flex-row sm:items-center sm:gap-3">
        <label htmlFor={id} className="flex shrink-0 items-center gap-2 text-fg-muted">
          <FolderTree className="size-4" /> {t('Folder')}
        </label>
        <Select
          id={id}
          className="w-full min-w-0 sm:w-auto sm:flex-1"
          value={path}
          onValueChange={onPathChange}
          options={folderChoices(folderPath).map((value) => ({ value, label: value === '/' ? t('/ (whole channel)') : value }))}
        />
      </div>
      {folder.isError ? (
        <p className="text-xs text-danger">{errorMessage(folder.error)}</p>
      ) : (
        <p className="text-xs text-fg-muted">
          {folder.data && <span className="font-medium text-fg">{folder.data.videos === 1 ? t('1 video.') : t('{count} videos.', { count: folder.data.videos })} </span>}
          {t('All of them become episodes of the series you choose, with the season and episode taken from the subfolders and file names. New files too.')}
        </p>
      )}
      {assigned && (
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone="accent">
            {t('Now: {title}', { title: assigned.title ?? assigned.providerId ?? '' })}
            {assigned.year ? ` (${assigned.year})` : ''}
          </Badge>
          {assigned.path === path ? (
            <Button size="sm" variant="ghost" icon={<Unlink />} loading={remove.isPending} onClick={() => remove.mutate(assigned.path)}>
              {t('Remove')}
            </Button>
          ) : (
            <span className="font-mono text-xs text-fg-subtle">{assigned.path}</span>
          )}
        </div>
      )}
      {!!folder.data?.locked && (
        <Checkbox
          checked={includeLocked}
          onCheckedChange={(value) => onIncludeLockedChange(value === true)}
          label={folder.data.locked === 1 ? t('Also the file identified by hand') : t('Also the {count} files identified by hand', { count: folder.data.locked })}
        />
      )}
    </div>
  );
}
