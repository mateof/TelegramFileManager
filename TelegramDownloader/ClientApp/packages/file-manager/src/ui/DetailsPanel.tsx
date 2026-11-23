import { useQuery } from '@tanstack/react-query';
import { Button, cn, EmptyState, IconButton } from '@tfm/ui';
import { MousePointerClick, X } from 'lucide-react';
import type { ReactNode } from 'react';
import { entryMediaKind } from '../core/media';
import type { FileEntry } from '../types';
import { useResolvedActions } from './ActionMenu';
import { useFileManagerUi } from './context';
import { EntryThumb, FileIcon } from './FileIcon';
import { formatBytes, formatDate } from './format';
import { fmt } from './strings';

function Row({ label, children }: { label: string; children: ReactNode }) {
  if (children === '' || children === undefined || children === null) return null;
  return (
    <div className="grid grid-cols-[6.5rem_1fr] gap-2 py-1.5 text-sm">
      <dt className="text-fg-subtle">{label}</dt>
      <dd className="min-w-0 break-words text-fg">{children}</dd>
    </div>
  );
}

function Preview({ entry }: { entry: FileEntry }) {
  const { fm } = useFileManagerUi();
  const kind = entryMediaKind(entry);
  const thumb = fm.provider.thumbnailUrl?.(entry);
  const preview = fm.provider.capabilities.preview ? fm.provider.previewUrl?.(entry) : undefined;
  const image = kind === 'image' ? preview ?? thumb : thumb;
  return (
    <div className="flex aspect-[4/3] w-full items-center justify-center overflow-hidden rounded-2xl border border-border bg-surface-2/60">
      {image ? <EntryThumb entry={entry} url={image} size="xl" className="size-full rounded-none [&_img]:object-contain" /> : <FileIcon entry={entry} size="xl" />}
    </div>
  );
}

/** Side panel with the preview, metadata and actions of the selection. */
export function DetailsPanel({ onClose, className, children }: { onClose?: () => void; className?: string; children?: ReactNode }) {
  const { fm, strings, locale } = useFileManagerUi();
  const selected = fm.selection.entries;
  const single = selected.length === 1 ? selected[0] : null;
  const actions = useResolvedActions('details', selected);
  const stat = fm.provider.stat;
  const { data: detailed } = useQuery({
    queryKey: ['tfm-file-manager-stat', fm.provider.id, single?.id],
    queryFn: ({ signal }) => stat!.call(fm.provider, single!, signal),
    enabled: !!single && !!stat,
    staleTime: 60_000
  });
  const entry = single ? (detailed && detailed.id === single.id ? detailed : single) : null;
  const totalBytes = selected.reduce((s, e) => s + (e.size ?? 0), 0);

  return (
    <aside className={cn('flex min-h-0 flex-col', className)} aria-label={strings.details}>
      <div className="flex h-11 shrink-0 items-center justify-between border-b border-border px-3">
        <span className="text-sm font-semibold text-fg">{strings.details}</span>
        {onClose && (
          <IconButton label={strings.cancel} size="xs" onClick={onClose}>
            <X />
          </IconButton>
        )}
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto p-3">
        {selected.length === 0 ? (
          <EmptyState icon={<MousePointerClick />} title={strings.noSelection} description={strings.noSelectionHint} className="py-10" />
        ) : entry ? (
          <div className="flex flex-col gap-4">
            <Preview entry={entry} />
            <div>
              <h3 className="break-words text-base font-semibold text-fg">{entry.name}</h3>
              <p className="text-xs text-fg-subtle">{entry.kind === 'folder' ? strings.folder : (entry.extension ?? '').toUpperCase() || strings.file}</p>
            </div>
            <dl className="divide-y divide-border/60">
              <Row label={strings.size}>{entry.kind === 'file' || entry.size ? formatBytes(entry.size) : ''}</Row>
              <Row label={strings.contents}>
                {entry.kind === 'folder' && entry.childCount !== undefined ? entry.childCount : ''}
              </Row>
              <Row label={strings.modified}>{formatDate(entry.modifiedAt, locale)}</Row>
              <Row label={strings.created}>{formatDate(entry.createdAt, locale)}</Row>
              <Row label={strings.location}>{entry.parentPath}</Row>
            </dl>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="flex -space-x-3">
              {selected.slice(0, 4).map((e) => (
                <FileIcon key={e.id} entry={e} size="lg" className="ring-2 ring-surface" />
              ))}
            </div>
            <h3 className="text-base font-semibold text-fg">{fmt(strings.selected, { count: selected.length })}</h3>
            <dl>
              <Row label={strings.totalSize}>{formatBytes(totalBytes)}</Row>
            </dl>
          </div>
        )}
        {actions.length > 0 && (
          <div className="mt-4 flex flex-col gap-1.5">
            {actions.map(({ action, label, enabled }) => (
              <Button
                key={action.id}
                variant={action.destructive ? 'ghost' : 'secondary'}
                size="sm"
                icon={action.icon}
                disabled={!enabled}
                className={cn('justify-start', action.destructive && 'text-danger hover:bg-danger-soft hover:text-danger')}
                onClick={() => void action.run(selected, fm.actionContext)}
              >
                {label}
              </Button>
            ))}
          </div>
        )}
        {children}
      </div>
    </aside>
  );
}
