import type { ApiChatMessageDto } from '@tfm/api-client';
import { useInfiniteQuery } from '@tanstack/react-query';
import {
  Avatar,
  Button,
  Checkbox,
  cn,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  EmptyState,
  IconButton,
  SegmentedControl,
  Skeleton,
  Spinner
} from '@tfm/ui';
import {
  CloudDownload,
  ExternalLink,
  Eye,
  File as FileIcon,
  FileText,
  Film,
  Image as ImageIcon,
  LayoutGrid,
  MessagesSquare,
  MoreHorizontal,
  Music,
  Play,
  RefreshCw,
  TriangleAlert
} from 'lucide-react';
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from 'react';
import { Link, useParams } from 'react-router';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { useChannels } from '@/shell/useChannels';
import { downloadMessages, messageThumbnailUrl, openMessageMedia } from '../chatActions';
import { dayLabel, fileTypeLabel, groupByDay, hasOlderPage, isVisualMedia, mergePages, PAGE_SIZE, telegramMessageLink } from '../chatModel';

type View = 'messages' | 'media';

export default function ChatPage() {
  const { id = '' } = useParams();
  const channels = useChannels();
  const channel = channels.data?.find((c) => String(c.id) === id);
  const name = channel?.name ?? id;
  const [view, setView] = useState<View>('messages');

  useEffect(() => {
    document.title = `${name} · TFM`;
  }, [name]);

  const history = useInfiniteQuery({
    queryKey: ['chat', 'messages', id],
    queryFn: ({ pageParam, signal }) => api.channels.messages(id, { limit: PAGE_SIZE, offset: pageParam }, signal),
    initialPageParam: 0,
    getNextPageParam: (last, all) => (hasOlderPage(last) ? all.length * PAGE_SIZE : undefined),
    enabled: !!id,
    staleTime: 60_000
  });

  const messages = useMemo(() => mergePages(history.data?.pages ?? []), [history.data]);

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="glass z-10 flex flex-wrap items-center gap-3 border-b border-border px-4 py-3 sm:px-6">
        <Avatar src={api.channels.imageUrl(id)} name={name} size="md" square />
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-lg font-semibold tracking-tight text-fg">{name}</h1>
          <p className="text-xs text-fg-subtle">
            {channel ? (
              <Link to={`/channels/${encodeURIComponent(id)}`} className="hover:text-fg-muted hover:underline">
                {t('Open the file manager')}
              </Link>
            ) : (
              t('Messages and media of this chat.')
            )}
          </p>
        </div>
        <SegmentedControl<View>
          size="sm"
          aria-label={t('View')}
          value={view}
          onValueChange={setView}
          options={[
            { value: 'messages', label: t('Messages'), icon: <MessagesSquare /> },
            { value: 'media', label: t('Media'), icon: <LayoutGrid /> }
          ]}
        />
        <IconButton label={t('Reload')} onClick={() => void history.refetch()} disabled={history.isFetching}>
          <RefreshCw className={cn(history.isRefetching && 'animate-spin')} />
        </IconButton>
      </header>

      {history.isPending ? (
        <ChatSkeleton />
      ) : history.isError ? (
        <EmptyState
          icon={<TriangleAlert />}
          title={t('Could not read the messages')}
          description={errorMessage(history.error)}
          action={<Button onClick={() => void history.refetch()}>{t('Retry')}</Button>}
        />
      ) : messages.length === 0 ? (
        <EmptyState icon={<MessagesSquare />} title={t('No messages')} description={t('This chat has no messages yet.')} />
      ) : view === 'messages' ? (
        <MessageList
          channelId={id}
          channelName={name}
          messages={messages}
          hasOlder={history.hasNextPage}
          loadingOlder={history.isFetchingNextPage}
          loadOlder={() => void history.fetchNextPage()}
        />
      ) : (
        <MediaGrid
          channelId={id}
          channelName={name}
          messages={messages}
          hasOlder={history.hasNextPage}
          loadingOlder={history.isFetchingNextPage}
          loadOlder={() => void history.fetchNextPage()}
        />
      )}
    </div>
  );
}

function ChatSkeleton() {
  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4 p-6">
      {[0, 1, 2, 3, 4].map((i) => (
        <Skeleton key={i} className={cn('h-16 rounded-2xl', i % 2 ? 'w-2/3' : 'w-1/2')} />
      ))}
    </div>
  );
}

interface ListProps {
  channelId: string;
  channelName: string;
  messages: ApiChatMessageDto[];
  hasOlder: boolean;
  loadingOlder: boolean;
  loadOlder: () => void;
}

/** Calls `onVisible` while `target` is on screen inside `root`. */
function useSentinel(root: RefObject<HTMLElement | null>, target: RefObject<HTMLElement | null>, enabled: boolean, onVisible: () => void) {
  const cb = useRef(onVisible);
  useEffect(() => {
    cb.current = onVisible;
  });
  useEffect(() => {
    if (!enabled || !target.current || typeof IntersectionObserver === 'undefined') return;
    const io = new IntersectionObserver((entries) => entries.some((e) => e.isIntersecting) && cb.current(), {
      root: root.current,
      rootMargin: '400px 0px'
    });
    io.observe(target.current);
    return () => io.disconnect();
  }, [root, target, enabled]);
}

function MessageList({ channelId, channelName, messages, hasOlder, loadingOlder, loadOlder }: ListProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const top = useRef<HTMLDivElement>(null);
  const fromBottom = useRef<number | null>(null);
  const lang = getLanguage();
  const days = useMemo(() => groupByDay(messages), [messages]);
  const labels = { today: t('Today'), yesterday: t('Yesterday') };
  const timeFmt = useMemo(() => new Intl.DateTimeFormat(lang, { hour: '2-digit', minute: '2-digit' }), [lang]);

  // Older messages are prepended: keep the distance to the bottom so the view does not jump
  useLayoutEffect(() => {
    const el = scroller.current;
    if (!el) return;
    el.scrollTop = fromBottom.current === null ? el.scrollHeight : el.scrollHeight - fromBottom.current;
  }, [messages.length]);

  useSentinel(scroller, top, hasOlder && !loadingOlder, () => {
    const el = scroller.current;
    if (el) fromBottom.current = el.scrollHeight - el.scrollTop;
    loadOlder();
  });

  return (
    <div ref={scroller} className="min-h-0 flex-1 overflow-y-auto overscroll-contain">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-2 px-3 py-4 sm:px-6">
        <div ref={top} className="flex h-10 items-center justify-center">
          {loadingOlder ? <Spinner size="sm" label={t('Loading older messages')} /> : !hasOlder && <span className="text-xs text-fg-subtle">{t('Start of the chat')}</span>}
        </div>
        {days.map((d) => (
          <section key={d.day} aria-label={dayLabel(d.day, lang, labels)} className="flex flex-col gap-2">
            <div className="sticky top-2 z-[1] flex justify-center py-1">
              <span className="glass rounded-full border border-border px-3 py-1 text-xs font-medium capitalize text-fg-muted shadow-soft">
                {dayLabel(d.day, lang, labels)}
              </span>
            </div>
            {d.messages.map((m) => (
              <MessageBubble key={m.id} m={m} time={timeFmt.format(new Date(m.date))} channelId={channelId} channelName={channelName} />
            ))}
          </section>
        ))}
      </div>
    </div>
  );
}

function mediaIcon(type: string | null): ReactNode {
  switch (type) {
    case 'audio':
      return <Music />;
    case 'video':
      return <Film />;
    case 'photo':
      return <ImageIcon />;
    case 'document':
      return <FileText />;
    default:
      return <FileIcon />;
  }
}

function MessageActions({ m, channelId, channelName }: { m: ApiChatMessageDto; channelId: string; channelName: string }) {
  const link = telegramMessageLink(channelId, m.id);
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <IconButton size="xs" label={t('Message actions')} className="opacity-60 group-hover:opacity-100 focus-visible:opacity-100">
          <MoreHorizontal />
        </IconButton>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {m.hasMedia && (
          <>
            <DropdownMenuItem onSelect={() => void openMessageMedia(channelId, channelName, m)}>
              {m.mediaType === 'audio' ? <Play /> : <Eye />} {m.mediaType === 'audio' ? t('Play') : t('View')}
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => void downloadMessages(channelId, [m.id])}>
              <CloudDownload /> {t('Download to server')}
            </DropdownMenuItem>
          </>
        )}
        {link && (
          <DropdownMenuItem asChild>
            <a href={link} target="_blank" rel="noreferrer">
              <ExternalLink /> {t('Open in Telegram')}
            </a>
          </DropdownMenuItem>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function Thumbnail({ channelId, m, className }: { channelId: string; m: ApiChatMessageDto; className?: string }) {
  const [state, setState] = useState<'loading' | 'ok' | 'error'>('loading');
  return (
    <span className={cn('relative block overflow-hidden bg-surface-3', className)}>
      {state === 'loading' && <span className="absolute inset-0 animate-shimmer bg-gradient-to-r from-surface-3 via-surface-2 to-surface-3 bg-[length:200%_100%]" />}
      {state === 'error' ? (
        <span className="absolute inset-0 flex items-center justify-center text-fg-subtle [&_svg]:size-8">{mediaIcon(m.mediaType)}</span>
      ) : (
        <img
          src={messageThumbnailUrl(channelId, m.id)}
          alt={m.fileName ?? ''}
          loading="lazy"
          decoding="async"
          onLoad={() => setState('ok')}
          onError={() => setState('error')}
          className={cn('size-full object-cover transition-[opacity,filter,transform] duration-500', state === 'ok' ? 'opacity-100 blur-0' : 'opacity-0 blur-md')}
        />
      )}
      {m.mediaType === 'video' && (
        <span className="absolute inset-0 flex items-center justify-center">
          <span className="flex size-11 items-center justify-center rounded-full bg-black/55 text-white shadow-elevated backdrop-blur-sm [&_svg]:size-5">
            <Play className="translate-x-px" />
          </span>
        </span>
      )}
    </span>
  );
}

function MessageBubble({ m, time, channelId, channelName }: { m: ApiChatMessageDto; time: string; channelId: string; channelName: string }) {
  const visual = isVisualMedia(m);
  const open = () => void openMessageMedia(channelId, channelName, m);
  return (
    <article className="group flex animate-fade-in items-start gap-2">
      <div className="min-w-0 max-w-[min(34rem,88%)] overflow-hidden rounded-2xl rounded-tl-md border border-border bg-surface shadow-soft">
        {visual && (
          <button type="button" onClick={open} className="block w-full focus-visible:outline-2 focus-visible:outline-ring" aria-label={m.mediaType === 'video' ? t('Play video') : t('View photo')}>
            <Thumbnail channelId={channelId} m={m} className="aspect-video max-h-80 w-full sm:w-[28rem]" />
          </button>
        )}
        {m.hasMedia && !visual && (
          <button
            type="button"
            onClick={open}
            className="flex w-full items-center gap-3 px-3 pt-3 text-left focus-visible:outline-2 focus-visible:outline-ring"
          >
            <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-accent-300 [&_svg]:size-5">
              {mediaIcon(m.mediaType)}
            </span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-medium text-fg">{m.fileName ?? t('Attachment')}</span>
              <span className="block text-xs text-fg-subtle">
                {[formatBytes(m.fileSize), fileTypeLabel(m)].filter(Boolean).join(' · ')}
              </span>
            </span>
          </button>
        )}
        <div className="px-3 pb-2 pt-2">
          {m.from && <p className="text-xs font-semibold text-accent-300">{m.from}</p>}
          {visual && m.fileName && <p className="truncate text-xs text-fg-subtle">{m.fileName} · {formatBytes(m.fileSize)}</p>}
          {m.text && <p className="whitespace-pre-wrap break-words text-sm text-fg">{m.text}</p>}
          <div className="mt-0.5 flex items-center justify-end gap-1">
            <time dateTime={m.date} className="text-[11px] text-fg-subtle">
              {time}
            </time>
          </div>
        </div>
      </div>
      <MessageActions m={m} channelId={channelId} channelName={channelName} />
    </article>
  );
}

function MediaGrid({ channelId, channelName, messages, hasOlder, loadingOlder, loadOlder }: ListProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const media = useMemo(() => messages.filter(isVisualMedia).reverse(), [messages]);
  const [selected, setSelected] = useState<Set<number>>(() => new Set());

  useSentinel(scroller, bottom, hasOlder && !loadingOlder, loadOlder);

  const toggle = (msgId: number) =>
    setSelected((s) => {
      const next = new Set(s);
      if (next.has(msgId)) next.delete(msgId);
      else next.add(msgId);
      return next;
    });
  const allSelected = media.length > 0 && media.every((m) => selected.has(m.id));

  return (
    <div ref={scroller} className="min-h-0 flex-1 overflow-y-auto overscroll-contain">
      <div className="glass sticky top-0 z-10 flex flex-wrap items-center gap-3 border-b border-border px-4 py-2 sm:px-6">
        <Checkbox
          checked={allSelected ? true : selected.size > 0 ? 'indeterminate' : false}
          onCheckedChange={(v) => setSelected(v === true ? new Set(media.map((m) => m.id)) : new Set())}
          aria-label={t('Select all')}
        />
        <span className="flex-1 text-sm text-fg-muted">
          {selected.size > 0 ? t('{count} selected', { count: selected.size }) : t('{count} photos and videos', { count: media.length })}
        </span>
        <Button
          size="sm"
          icon={<CloudDownload />}
          disabled={selected.size === 0}
          onClick={() => {
            void downloadMessages(channelId, [...selected]);
            setSelected(new Set());
          }}
        >
          {t('Download selected')}
        </Button>
      </div>
      {media.length === 0 && !hasOlder ? (
        <EmptyState icon={<ImageIcon />} title={t('No photos or videos')} description={t('The loaded messages carry no photos or videos.')} />
      ) : (
        <ul className="grid grid-cols-3 gap-1 p-1 sm:grid-cols-4 sm:gap-2 sm:p-4 md:grid-cols-5 xl:grid-cols-7">
          {media.map((m) => {
            const isSel = selected.has(m.id);
            return (
              <li key={m.id} className="group relative">
                <button
                  type="button"
                  onClick={() => (selected.size > 0 ? toggle(m.id) : void openMessageMedia(channelId, channelName, m))}
                  className={cn(
                    'block w-full overflow-hidden rounded-lg ring-offset-2 ring-offset-bg transition focus-visible:outline-2 focus-visible:outline-ring',
                    isSel && 'ring-2 ring-accent-400'
                  )}
                  aria-label={m.fileName ?? (m.mediaType === 'video' ? t('Play video') : t('View photo'))}
                >
                  <Thumbnail channelId={channelId} m={m} className={cn('aspect-square transition-transform duration-300 group-hover:scale-[1.03]', isSel && 'scale-95')} />
                </button>
                <span className={cn('absolute left-1.5 top-1.5 transition-opacity', isSel || selected.size > 0 ? 'opacity-100' : 'opacity-0 group-hover:opacity-100 group-focus-within:opacity-100')}>
                  <Checkbox checked={isSel} onCheckedChange={() => toggle(m.id)} aria-label={t('Select')} className="bg-surface/80 shadow-soft" />
                </span>
              </li>
            );
          })}
        </ul>
      )}
      <div ref={bottom} className="flex h-14 items-center justify-center">
        {loadingOlder ? (
          <Spinner size="sm" label={t('Loading older messages')} />
        ) : hasOlder ? (
          <Button size="sm" variant="ghost" onClick={loadOlder}>
            {t('Load older')}
          </Button>
        ) : null}
      </div>
    </div>
  );
}
