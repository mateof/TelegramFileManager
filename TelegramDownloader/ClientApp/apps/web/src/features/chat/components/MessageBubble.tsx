import type { ApiChatMessageDto } from '@tfm/api-client';
import { Checkbox, cn } from '@tfm/ui';
import { t } from '@/i18n';
import { formatBytes } from '@/lib/format';
import { fileTypeLabel, highlightParts, isVisualMedia } from '../chatModel';
import { mediaIcon } from './mediaIcon';
import { MessageActions } from './MessageActions';
import { Thumbnail } from './Thumbnail';

function Highlighted({ text, query }: { text: string; query: string }) {
  if (!query) return text;
  return highlightParts(text, query).map((p, i) =>
    p.match ? (
      <mark key={i} className="rounded bg-warning/40 text-inherit">
        {p.text}
      </mark>
    ) : (
      p.text
    )
  );
}

export interface MessageBubbleProps {
  m: ApiChatMessageDto;
  time: string;
  channelId: string;
  /** Words to highlight. */
  query: string;
  /** The message someone was taken to. */
  flash: boolean;
  /** Choosing messages: a click chooses instead of opening. */
  selecting: boolean;
  selected: boolean;
  onToggle: (range: boolean) => void;
  onOpen: () => void;
}

export function MessageBubble({ m, time, channelId, query, flash, selecting, selected, onToggle, onOpen }: MessageBubbleProps) {
  const visual = isVisualMedia(m);
  const choosable = selecting && m.hasMedia;
  return (
    <article
      data-message={m.id}
      className={cn('group flex animate-fade-in scroll-my-24 items-start gap-2', choosable && 'cursor-pointer', selecting && !m.hasMedia && 'opacity-50')}
      onClickCapture={
        choosable
          ? (e) => {
              e.preventDefault();
              e.stopPropagation();
              onToggle(e.shiftKey);
            }
          : undefined
      }
    >
      {choosable && <Checkbox checked={selected} aria-label={t('Select')} className="mt-3 shrink-0" />}
      <div
        className={cn(
          'min-w-0 max-w-[min(34rem,88%)] overflow-hidden rounded-2xl rounded-tl-md border border-border bg-surface shadow-soft transition-shadow duration-500',
          (selected || flash) && 'ring-2 ring-accent-400'
        )}
      >
        {visual && (
          <button type="button" onClick={onOpen} className="block w-full focus-visible:outline-2 focus-visible:outline-ring" aria-label={m.mediaType === 'video' ? t('Play video') : t('View photo')}>
            <Thumbnail channelId={channelId} m={m} className="aspect-video w-[min(28rem,76vw)]" />
          </button>
        )}
        {m.hasMedia && !visual && (
          <button type="button" onClick={onOpen} className="flex w-full items-center gap-3 px-3 pt-3 text-left focus-visible:outline-2 focus-visible:outline-ring">
            <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-accent-300 [&_svg]:size-5">{mediaIcon(m.mediaType)}</span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-medium text-fg">
                <Highlighted text={m.fileName ?? t('Attachment')} query={query} />
              </span>
              <span className="block text-xs text-fg-subtle">{[formatBytes(m.fileSize), fileTypeLabel(m)].filter(Boolean).join(' · ')}</span>
            </span>
          </button>
        )}
        <div className="px-3 pb-2 pt-2">
          {m.from && <p className="text-xs font-semibold text-accent-300">{m.from}</p>}
          {visual && m.fileName && (
            <p className="truncate text-xs text-fg-subtle">
              <Highlighted text={m.fileName} query={query} /> · {formatBytes(m.fileSize)}
            </p>
          )}
          {m.text && (
            <p className="whitespace-pre-wrap break-words text-sm text-fg">
              <Highlighted text={m.text} query={query} />
            </p>
          )}
          <div className="mt-0.5 flex items-center justify-end gap-1">
            <time dateTime={m.date} className="text-[11px] text-fg-subtle">
              {time}
            </time>
          </div>
        </div>
      </div>
      {!selecting && <MessageActions m={m} channelId={channelId} onOpen={onOpen} onSelect={() => onToggle(false)} />}
    </article>
  );
}
