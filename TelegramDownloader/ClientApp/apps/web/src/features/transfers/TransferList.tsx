import type { TransferDto } from '@tfm/api-client';
import { Badge, cn, IconButton, Progress, Sheet, SheetContent, type BadgeTone } from '@tfm/ui';
import { useVirtualizer } from '@tanstack/react-virtual';
import {
  AlertCircle,
  ArrowDown,
  ArrowUp,
  CheckCircle2,
  Clock,
  FolderSync,
  Pause,
  Play,
  RotateCcw,
  Trash2,
  X,
  type LucideIcon
} from 'lucide-react';
import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { getLanguage, t } from '@/i18n';
import { formatBytes, formatRelative } from '@/lib/format';
import { actionLabel, runAction, stateLabel } from './actions';
import { availableActions, formatEta, stateGroup, type StateGroup, type TransferAction } from './model';
import type { ItemMetrics } from './useTransfersLive';

const TONES: Record<StateGroup, BadgeTone> = { active: 'accent', queued: 'neutral', paused: 'warning', done: 'success', failed: 'danger', canceled: 'neutral' };
const PROGRESS_TONES = { active: 'accent', queued: 'accent', paused: 'warning', done: 'success', failed: 'danger', canceled: 'warning' } as const;
const ACTION_ICONS: Record<TransferAction, LucideIcon> = { pause: Pause, resume: Play, cancel: X, retry: RotateCcw, remove: Trash2 };

function KindIcon({ item }: { item: TransferDto }) {
  const g = stateGroup(item);
  const Icon = g === 'done' ? CheckCircle2 : g === 'failed' ? AlertCircle : g === 'queued' ? Clock : item.kind === 'task' ? FolderSync : item.kind === 'upload' ? ArrowUp : ArrowDown;
  return (
    <span
      className={cn(
        'flex size-10 shrink-0 items-center justify-center rounded-xl border [&_svg]:size-5',
        g === 'active' && 'border-accent-500/30 bg-primary-soft text-accent-300',
        g === 'done' && 'border-success/30 bg-success-soft text-success',
        g === 'failed' && 'border-danger/30 bg-danger-soft text-danger',
        g === 'paused' && 'border-warning/30 bg-warning-soft text-warning',
        (g === 'queued' || g === 'canceled') && 'border-border bg-surface-2 text-fg-muted'
      )}
    >
      <Icon />
    </span>
  );
}

function progressText(item: TransferDto, m?: ItemMetrics): string {
  const parts: string[] = [];
  if (item.kind === 'task' && item.totalItems) parts.push(t('{done} of {total} files', { done: item.executedItems ?? 0, total: item.totalItems }));
  if (item.size > 0) parts.push(`${item.transmittedText || formatBytes(item.transmitted)} / ${item.sizeText || formatBytes(item.size)}`);
  if (m && m.speed > 0 && stateGroup(item) === 'active') parts.push(`${formatBytes(m.speed)}/s`);
  const eta = formatEta(m?.eta ?? null);
  if (eta) parts.push(t('{time} left', { time: eta }));
  return parts.join(' · ');
}

function percent(item: TransferDto): number {
  if (item.kind === 'task' && item.totalItems) return ((item.executedItems ?? 0) / item.totalItems) * 100;
  return item.progress;
}

function ActionButtons({ item, size = 'sm' }: { item: TransferDto; size?: 'sm' | 'xs' }) {
  return (
    <div className="flex shrink-0 items-center gap-0.5">
      {availableActions(item).map((a) => {
        const Icon = ACTION_ICONS[a];
        return (
          <IconButton
            key={a}
            label={actionLabel(a)}
            size={size}
            onClick={(e) => {
              e.stopPropagation();
              void runAction(item, a);
            }}
            className={cn(a === 'cancel' || a === 'remove' ? 'hover:text-danger' : '')}
          >
            <Icon />
          </IconButton>
        );
      })}
    </div>
  );
}

function Row({ item, metrics, onOpen }: { item: TransferDto; metrics?: ItemMetrics; onOpen: () => void }) {
  const g = stateGroup(item);
  const showProgress = g !== 'done' || item.kind === 'task';
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onOpen();
        }
      }}
      className="group flex items-center gap-3 rounded-2xl border border-transparent px-3 py-2.5 transition-colors hover:border-border hover:bg-surface-2/60 focus-visible:outline-2 focus-visible:outline-ring"
    >
      <KindIcon item={item} />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate text-sm font-medium text-fg">{item.name}</span>
          {item.action && item.action !== 'Download' && item.action !== 'Upload' && <Badge className="max-sm:hidden">{item.action}</Badge>}
        </div>
        {showProgress && g !== 'queued' && <Progress value={percent(item)} size="xs" tone={PROGRESS_TONES[g]} aria-label={t('Progress')} />}
        <div className="flex min-w-0 items-center gap-2 text-xs text-fg-muted">
          <span className="truncate tabular-nums">{progressText(item, metrics)}</span>
          {(item.channelName || item.path) && <span className="hidden truncate text-fg-subtle md:inline">· {item.channelName ?? item.path}</span>}
        </div>
      </div>
      <Badge tone={TONES[g]} dot={g === 'active'} className="max-sm:hidden">
        {g === 'active' ? `${Math.round(percent(item))}%` : stateLabel(g)}
      </Badge>
      <ActionButtons item={item} />
    </div>
  );
}

const ROW_ESTIMATE = 76;

/** Virtualized against the page scroll container (#tfm-main). */
export function TransferList({ items, metrics, empty }: { items: TransferDto[]; metrics: Record<string, ItemMetrics>; empty: ReactNode }) {
  const listRef = useRef<HTMLDivElement>(null);
  const [scrollMargin, setScrollMargin] = useState(0);
  const [openId, setOpenId] = useState<string | null>(null);
  const getScrollElement = () => document.getElementById('tfm-main');

  useLayoutEffect(() => {
    const main = getScrollElement();
    const el = listRef.current;
    if (main && el) setScrollMargin(el.getBoundingClientRect().top - main.getBoundingClientRect().top + main.scrollTop);
  }, [items.length]);

  const virtualizer = useVirtualizer({
    count: items.length,
    getScrollElement,
    estimateSize: () => ROW_ESTIMATE,
    overscan: 6,
    scrollMargin,
    getItemKey: (i) => items[i]?.id ?? i
  });

  const open = openId ? items.find((i) => i.id === openId) : undefined;

  if (items.length === 0) return <>{empty}</>;

  return (
    <>
      <div ref={listRef} className="relative" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((v) => {
          const item = items[v.index];
          return (
            <div key={v.key} data-index={v.index} ref={virtualizer.measureElement} className="absolute inset-x-0 top-0 pb-1" style={{ transform: `translateY(${v.start - scrollMargin}px)` }}>
              <Row item={item} metrics={metrics[item.id]} onOpen={() => setOpenId(item.id)} />
            </div>
          );
        })}
      </div>
      <Sheet open={!!open} onOpenChange={(o) => !o && setOpenId(null)}>
        <SheetContent side="right" title={open?.name ?? ''} description={open ? stateLabel(stateGroup(open)) : undefined}>
          {open && <TransferDetails item={open} metrics={metrics[open.id]} />}
        </SheetContent>
      </Sheet>
    </>
  );
}

function Detail({ label, value }: { label: string; value: ReactNode }) {
  if (value === null || value === undefined || value === '') return null;
  return (
    <div className="flex flex-col gap-0.5 py-2">
      <dt className="text-xs text-fg-subtle">{label}</dt>
      <dd className="break-words text-sm text-fg">{value}</dd>
    </div>
  );
}

function TransferDetails({ item, metrics }: { item: TransferDto; metrics?: ItemMetrics }) {
  const lang = getLanguage();
  const g = stateGroup(item);
  const date = (iso: string | null) => (iso ? `${new Date(iso).toLocaleString(lang)} (${formatRelative(iso, lang)})` : null);
  const kind = item.kind === 'download' ? t('Download') : item.kind === 'upload' ? t('Upload') : t('Folder task');
  return (
    <div className="flex flex-col gap-4 px-5 pb-6">
      <div className="flex flex-col gap-2 rounded-2xl border border-border bg-surface-2/50 p-4">
        <div className="flex items-center justify-between text-sm">
          <span className="font-semibold tabular-nums">{Math.round(percent(item))}%</span>
          <Badge tone={TONES[g]}>{stateLabel(g)}</Badge>
        </div>
        <Progress value={percent(item)} tone={PROGRESS_TONES[g]} aria-label={t('Progress')} />
        <p className="text-xs tabular-nums text-fg-muted">{progressText(item, metrics)}</p>
        <div className="pt-1">
          <ActionButtons item={item} size="sm" />
        </div>
      </div>
      <dl className="divide-y divide-border">
        <Detail label={t('Type')} value={`${kind}${item.action ? ` · ${item.action}` : ''}`} />
        <Detail label={t('Channel')} value={item.channelName ?? item.channelId} />
        <Detail label={t('Path')} value={item.path} />
        <Detail label={t('From')} value={item.fromPath} />
        <Detail label={t('To')} value={item.toPath} />
        <Detail label={t('Size')} value={item.size ? `${item.sizeText || formatBytes(item.size)} (${item.size.toLocaleString(lang)} B)` : null} />
        <Detail label={t('Transferred')} value={item.transmitted ? item.transmittedText || formatBytes(item.transmitted) : null} />
        {item.kind === 'task' && <Detail label={t('Files')} value={item.totalItems ? t('{done} of {total} files', { done: item.executedItems ?? 0, total: item.totalItems }) : null} />}
        <Detail label={t('Queued')} value={item.isQueued ? t('Yes') : null} />
        <Detail label={t('Created')} value={date(item.createdAt)} />
        <Detail label={t('Started')} value={date(item.startedAt)} />
        <Detail label={t('Finished')} value={date(item.endedAt)} />
        <Detail label={t('Id')} value={<code className="text-xs">{item.id}</code>} />
      </dl>
    </div>
  );
}
