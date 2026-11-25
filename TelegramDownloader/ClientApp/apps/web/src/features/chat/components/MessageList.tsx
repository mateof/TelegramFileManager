import type { ApiChatMessageDto } from '@tfm/api-client';
import { IconButton, Spinner } from '@tfm/ui';
import { ChevronsDown } from 'lucide-react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { getLanguage, t } from '@/i18n';
import { openMessageMedia } from '../chatActions';
import { dayLabel, groupByDay, nearestMessage } from '../chatModel';
import type { MessageSelection } from '../useSelection';
import { useSentinel } from '../useSentinel';
import { MessageBubble } from './MessageBubble';

const FLASH_MS = 2500;

/** Position of a message in the content of the scroller. */
function topOf(scroller: HTMLElement, id: number | null): number {
  const element = id === null ? null : scroller.querySelector(`[data-message="${id}"]`);
  return element ? element.getBoundingClientRect().top - scroller.getBoundingClientRect().top + scroller.scrollTop : 0;
}

export interface MessageListProps {
  channelId: string;
  channelName: string;
  /** Oldest first. */
  messages: ApiChatMessageDto[];
  hasOlder: boolean;
  loadingOlder: boolean;
  loadOlder: () => void;
  hasNewer: boolean;
  loadingNewer: boolean;
  loadNewer: () => void;
  /** Message to show; a new `nonce` takes there again. */
  jump: { id: number; nonce: number } | null;
  onLatest: () => void;
  query: string;
  selecting: boolean;
  selection: MessageSelection;
}

/** The conversation: loads older messages at the top and newer ones at the bottom, without moving what is being read. */
export function MessageList({
  channelId,
  channelName,
  messages,
  hasOlder,
  loadingOlder,
  loadOlder,
  hasNewer,
  loadingNewer,
  loadNewer,
  jump,
  onLatest,
  query,
  selecting,
  selection
}: MessageListProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const top = useRef<HTMLDivElement>(null);
  const bottom = useRef<HTMLDivElement>(null);
  // The oldest message and where it was: the reference when others are put above it
  const view = useRef<{ first: number | null; top: number }>({ first: null, top: 0 });
  const [faded, setFaded] = useState(0);
  const [away, setAway] = useState(false);
  const lang = getLanguage();
  const days = useMemo(() => groupByDay(messages), [messages]);
  const withFiles = useMemo(() => messages.filter((m) => m.hasMedia), [messages]);
  const labels = { today: t('Today'), yesterday: t('Yesterday') };
  const timeFmt = useMemo(() => new Intl.DateTimeFormat(lang, { hour: '2-digit', minute: '2-digit' }), [lang]);

  const first = messages[0]?.id ?? null;
  const last = messages[messages.length - 1]?.id ?? null;
  // Older messages go above what is on screen: the view moves with it. The first time, start at the end.
  useLayoutEffect(() => {
    const el = scroller.current;
    if (!el) return;
    const before = view.current;
    if (before.first === null) el.scrollTop = el.scrollHeight;
    else if (first !== null && first < before.first) el.scrollTop += topOf(el, before.first) - before.top;
    view.current = { first, top: topOf(el, first) };
  }, [first, last]);

  const target = jump ? (nearestMessage(messages, jump.id)?.id ?? null) : null;
  const nonce = jump?.nonce ?? 0;
  const flash = nonce === faded ? null : target;
  useLayoutEffect(() => {
    if (target === null) return;
    scroller.current?.querySelector(`[data-message="${target}"]`)?.scrollIntoView({ block: 'center' });
    const timer = setTimeout(() => setFaded(nonce), FLASH_MS);
    return () => clearTimeout(timer);
    // `target` is left out: it can only change with the messages around, and loading more must not take the view back
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nonce]);

  // The scroll position is kept by hand
  useEffect(() => {
    const el = scroller.current;
    if (!el) return;
    const onScroll = () => {
      view.current.top = topOf(el, view.current.first);
      setAway(el.scrollHeight - el.scrollTop - el.clientHeight > el.clientHeight);
    };
    el.addEventListener('scroll', onScroll, { passive: true });
    return () => el.removeEventListener('scroll', onScroll);
  }, []);

  useSentinel(scroller, top, hasOlder && !loadingOlder && !loadingNewer, loadOlder);
  useSentinel(scroller, bottom, hasNewer && !loadingNewer && !loadingOlder, loadNewer);

  return (
    <div className="relative min-h-0 flex-1">
      <div ref={scroller} className="h-full overflow-y-auto overscroll-contain [overflow-anchor:none]">
        <div className="mx-auto flex w-full max-w-3xl flex-col gap-2 px-3 py-4 sm:px-6">
          <div ref={top} className="flex h-10 items-center justify-center">
            {loadingOlder ? <Spinner size="sm" label={t('Loading older messages')} /> : !hasOlder && <span className="text-xs text-fg-subtle">{t('Start of the chat')}</span>}
          </div>
          {days.map((d) => (
            <section key={d.day} aria-label={dayLabel(d.day, lang, labels)} className="flex flex-col gap-2">
              <div className="pointer-events-none sticky top-2 z-[1] flex justify-center py-1">
                <span className="glass rounded-full border border-border px-3 py-1 text-xs font-medium capitalize text-fg-muted shadow-soft">{dayLabel(d.day, lang, labels)}</span>
              </div>
              {d.messages.map((m) => (
                <MessageBubble
                  key={m.id}
                  m={m}
                  time={timeFmt.format(new Date(m.date))}
                  channelId={channelId}
                  query={query}
                  flash={flash === m.id}
                  selecting={selecting}
                  selected={selection.has(m.id)}
                  onToggle={(range) => selection.toggle(m, withFiles, range)}
                  onOpen={() => void openMessageMedia(channelId, channelName, m)}
                />
              ))}
            </section>
          ))}
          <div ref={bottom} className="flex min-h-2 items-center justify-center">
            {loadingNewer && <Spinner size="sm" label={t('Loading newer messages')} />}
          </div>
        </div>
      </div>
      {(away || hasNewer) && (
        <IconButton
          label={t('Go to the last message')}
          variant="secondary"
          className="absolute bottom-4 right-4 z-[2] rounded-full shadow-elevated"
          onClick={() => {
            if (hasNewer) onLatest();
            else if (scroller.current) scroller.current.scrollTop = scroller.current.scrollHeight;
          }}
        >
          <ChevronsDown />
        </IconButton>
      )}
    </div>
  );
}
