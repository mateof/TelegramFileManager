import { Button, cn, IconButton, Progress } from '@tfm/ui';
import { CheckCircle2, ChevronDown, ChevronUp, RotateCcw, Upload, X, XCircle } from 'lucide-react';
import { useState } from 'react';
import type { UploadItem } from '../core/uploadQueue';
import { useFileManagerUi } from './context';
import { formatBytes } from './format';

function statusText(item: UploadItem, s: ReturnType<typeof useFileManagerUi>['strings']) {
  switch (item.status) {
    case 'queued':
      return s.uploadQueued;
    case 'uploading':
      return `${formatBytes(item.loaded) || '0 B'} / ${formatBytes(item.total)}`;
    case 'done':
      return s.uploadDone;
    case 'error':
      return item.error ? `${s.uploadError}: ${item.error}` : s.uploadError;
    case 'canceled':
      return s.uploadCanceled;
  }
}

/** Floating card with the upload queue (progress, cancel, retry). */
export function UploadPanel({ className }: { className?: string }) {
  const { fm, strings } = useFileManagerUi();
  const [collapsed, setCollapsed] = useState(false);
  const items = fm.uploads.items;
  if (items.length === 0) return null;
  const active = items.filter((i) => i.status === 'uploading' || i.status === 'queued');
  const finished = items.length - active.length;
  const loaded = items.reduce((s, i) => s + (i.status === 'done' ? i.total : i.loaded), 0);
  const total = items.reduce((s, i) => s + (i.status === 'canceled' ? 0 : i.total), 0);
  const percent = total ? Math.round((loaded / total) * 100) : 0;

  return (
    <section
      aria-label={strings.uploads}
      className={cn('glass-strong flex w-[min(24rem,calc(100%-1rem))] flex-col overflow-hidden rounded-2xl border border-border shadow-elevated animate-slide-in-bottom', className)}
    >
      <header className="flex items-center gap-2 px-3 py-2.5">
        <span className="flex size-8 items-center justify-center rounded-xl bg-primary-soft text-accent-300">
          <Upload className="size-4" />
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-fg">
            {strings.uploads} · {items.length - active.length}/{items.length}
          </p>
          {active.length > 0 && <Progress value={percent} size="xs" className="mt-1" aria-label={strings.uploads} />}
        </div>
        {active.length > 0 && (
          <Button variant="ghost" size="xs" onClick={fm.uploads.cancelAll}>
            {strings.cancelAll}
          </Button>
        )}
        {finished > 0 && active.length === 0 && (
          <Button variant="ghost" size="xs" onClick={fm.uploads.clearFinished}>
            {strings.clearFinished}
          </Button>
        )}
        <IconButton label={collapsed ? strings.expand : strings.collapse} size="xs" onClick={() => setCollapsed((c) => !c)}>
          {collapsed ? <ChevronUp /> : <ChevronDown />}
        </IconButton>
      </header>
      {!collapsed && (
        <ul className="max-h-64 overflow-y-auto border-t border-border px-1.5 py-1">
          {items.map((item) => (
            <li key={item.id} className="flex items-center gap-2 rounded-lg px-1.5 py-1.5">
              <div className="min-w-0 flex-1">
                <p className="truncate text-xs font-medium text-fg" title={item.file.name}>
                  {item.file.name}
                </p>
                {item.status === 'uploading' ? (
                  <Progress value={item.total ? (item.loaded / item.total) * 100 : undefined} size="xs" className="mt-1" aria-label={item.file.name} />
                ) : null}
                <p className={cn('truncate text-[11px]', item.status === 'error' ? 'text-danger' : 'text-fg-subtle')}>{statusText(item, strings)}</p>
              </div>
              {item.status === 'done' && <CheckCircle2 className="size-4 shrink-0 text-success" />}
              {item.status === 'error' && <XCircle className="size-4 shrink-0 text-danger" />}
              {(item.status === 'error' || item.status === 'canceled') && (
                <IconButton label={strings.retry} size="xs" onClick={() => fm.uploads.retry(item.id)}>
                  <RotateCcw />
                </IconButton>
              )}
              <IconButton
                label={item.status === 'uploading' || item.status === 'queued' ? strings.cancel : strings.remove}
                size="xs"
                onClick={() => (item.status === 'uploading' || item.status === 'queued' ? fm.uploads.cancel(item.id) : fm.uploads.remove(item.id))}
              >
                <X />
              </IconButton>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
