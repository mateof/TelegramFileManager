import type { ApiChatMessageDto } from '@tfm/api-client';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { Button, Checkbox, cn, EmptyState, IconButton, Skeleton, Spinner, Tabs, TabsList, TabsTrigger } from '@tfm/ui';
import { File as FileIcon, FileText, Film, Image as ImageIcon, Link2, MessageSquare, Mic, Music, Sticker, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, type ReactNode } from 'react';
import { formatTime } from '@/features/player/format';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatBytes } from '@/lib/format';
import { openAmong } from '../chatActions';
import { fileTypeLabel, initialSection, isGridSection, linkHref, linksIn, PAGE_SIZE, visibleSections, type MediaSection } from '../chatModel';
import { useSelection, type MessageSelection } from '../useSelection';
import { useSentinel } from '../useSentinel';
import { MessageActions } from './MessageActions';
import { SelectionBar } from './SelectionBar';
import { mediaIcon } from './mediaIcon';
import { Thumbnail } from './Thumbnail';

const SECTIONS: Record<MediaSection, { label: () => string; icon: ReactNode }> = {
  photos: { label: () => t('Photos'), icon: <ImageIcon /> },
  videos: { label: () => t('Videos'), icon: <Film /> },
  documents: { label: () => t('Documents'), icon: <FileText /> },
  audio: { label: () => t('Audio'), icon: <Music /> },
  voice: { label: () => t('Voice'), icon: <Mic /> },
  gifs: { label: () => t('GIFs'), icon: <Sticker /> },
  links: { label: () => t('Links'), icon: <Link2 /> }
};

export interface MediaBrowserProps {
  channelId: string;
  channelName: string;
  /** Section asked for (from the URL). */
  section: string | null;
  onSectionChange: (section: MediaSection) => void;
  /** Takes to a message in the conversation. */
  onGoTo: (messageId: number) => void;
}

/** Everything a chat has, by kind: the whole chat, not only the messages loaded. */
export function MediaBrowser({ channelId, channelName, section: asked, onSectionChange, onGoTo }: MediaBrowserProps) {
  const counts = useQuery({
    queryKey: ['chat', 'counts', channelId],
    queryFn: ({ signal }) => api.channels.messageCounts(channelId, signal),
    staleTime: 5 * 60_000
  });
  const section = initialSection(asked, counts.data);
  const tabs = useRef<HTMLDivElement>(null);
  useEffect(() => {
    tabs.current?.querySelector('[data-state=active]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [section]);

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <Tabs value={section} onValueChange={(v) => onSectionChange(v as MediaSection)}>
        <div ref={tabs} className="overflow-x-auto border-b border-border px-4 [scrollbar-width:none] sm:px-6">
          <TabsList variant="underline" aria-label={t('Media')} className="w-max border-b-0">
            {visibleSections(counts.data, section).map((k) => (
              <TabsTrigger key={k} value={k}>
                {SECTIONS[k].icon}
                {SECTIONS[k].label()}
                {counts.data?.[k] !== undefined && <span className="text-xs tabular-nums text-fg-subtle">{counts.data[k]}</span>}
              </TabsTrigger>
            ))}
          </TabsList>
        </div>
      </Tabs>
      {/* Until the counts say which section has something, do not ask for an empty one */}
      {asked || !counts.isPending ? (
        <Section key={section} channelId={channelId} channelName={channelName} section={section} onGoTo={onGoTo} />
      ) : (
        <SectionSkeleton grid />
      )}
    </div>
  );
}

function SectionSkeleton({ grid }: { grid: boolean }) {
  return grid ? (
    <div className="grid grid-cols-3 gap-1 p-1 sm:grid-cols-4 sm:gap-2 sm:p-4 md:grid-cols-5 xl:grid-cols-7">
      {Array.from({ length: 14 }, (_, i) => (
        <Skeleton key={i} className="aspect-square rounded-lg" />
      ))}
    </div>
  ) : (
    <div className="mx-auto flex w-full max-w-4xl flex-col gap-2 p-4">
      {Array.from({ length: 8 }, (_, i) => (
        <Skeleton key={i} className="h-14 rounded-xl" />
      ))}
    </div>
  );
}

interface SectionProps {
  channelId: string;
  channelName: string;
  section: MediaSection;
  onGoTo: (messageId: number) => void;
}

function Section({ channelId, channelName, section, onGoTo }: SectionProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const selection = useSelection();
  const grid = isGridSection(section);
  const list = useInfiniteQuery({
    queryKey: ['chat', 'media', channelId, section],
    queryFn: ({ pageParam, signal }) => api.channels.searchMessages(channelId, { kind: section, offsetId: pageParam, limit: PAGE_SIZE }, signal),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffsetId ?? undefined,
    staleTime: 60_000,
    refetchOnWindowFocus: false
  });
  const messages = useMemo(() => {
    const seen = new Set<number>();
    return (list.data?.pages ?? []).flatMap((p) => p.items).filter((m) => !seen.has(m.id) && seen.add(m.id));
  }, [list.data]);
  const total = list.data?.pages[0]?.total ?? 0;
  useSentinel(scroller, bottom, list.hasNextPage && !list.isFetchingNextPage, () => void list.fetchNextPage());

  if (list.isPending) return <SectionSkeleton grid={grid} />;
  if (list.isError)
    return (
      <EmptyState
        icon={<TriangleAlert />}
        title={t('Could not read the messages')}
        description={errorMessage(list.error)}
        action={<Button onClick={() => void list.refetch()}>{t('Retry')}</Button>}
      />
    );
  if (messages.length === 0) return <EmptyState icon={SECTIONS[section].icon} title={t('Nothing here')} description={t('This chat has nothing of this kind.')} />;

  const goTo = (m: ApiChatMessageDto) => ({ label: t('Go to the message'), run: () => onGoTo(m.id) });
  const item = (m: ApiChatMessageDto): ItemProps => ({
    m,
    channelId,
    selection,
    list: messages,
    onOpen: () => openAmong(channelId, channelName, messages, m, goTo),
    onGoTo: () => onGoTo(m.id)
  });

  return (
    <div ref={scroller} className="min-h-0 flex-1 overflow-y-auto overscroll-contain">
      {section !== 'links' && (
        <SelectionBar channelId={channelId} selection={selection} available={messages} hint={t('{loaded} of {total} loaded', { loaded: messages.length, total: Math.max(total, messages.length) })} />
      )}
      {grid ? (
        <ul className="grid grid-cols-3 gap-1 p-1 sm:grid-cols-4 sm:gap-2 sm:p-4 md:grid-cols-5 xl:grid-cols-7">
          {messages.map((m) => (
            <GridItem key={m.id} {...item(m)} />
          ))}
        </ul>
      ) : (
        <ul className="mx-auto flex w-full max-w-4xl flex-col gap-1 p-2 sm:p-4">
          {messages.map((m) => (section === 'links' ? <LinkItem key={m.id} {...item(m)} /> : <RowItem key={m.id} {...item(m)} voice={section === 'voice'} />))}
        </ul>
      )}
      <div ref={bottom} className="flex h-14 items-center justify-center">
        {list.isFetchingNextPage ? (
          <Spinner size="sm" label={t('Loading older messages')} />
        ) : list.hasNextPage ? (
          <Button size="sm" variant="ghost" onClick={() => void list.fetchNextPage()}>
            {t('Load older')}
          </Button>
        ) : null}
      </div>
    </div>
  );
}

interface ItemProps {
  m: ApiChatMessageDto;
  channelId: string;
  selection: MessageSelection;
  list: ApiChatMessageDto[];
  onOpen: () => void;
  onGoTo: () => void;
}

function GoToButton({ onGoTo, className }: { onGoTo: () => void; className?: string }) {
  return (
    <IconButton size="xs" label={t('Go to the message')} className={className} onClick={onGoTo}>
      <MessageSquare />
    </IconButton>
  );
}

function GridItem({ m, channelId, selection, list, onOpen, onGoTo }: ItemProps) {
  const selected = selection.has(m.id);
  const choosing = selection.ids.length > 0;
  // Without a mouse there is no hover: the button stays visible
  const onHover = 'opacity-0 group-hover:opacity-100 group-focus-within:opacity-100 [@media(hover:none)]:opacity-100';
  return (
    <li className="group relative">
      <button
        type="button"
        onClick={(e) => (choosing ? selection.toggle(m, list, e.shiftKey) : onOpen())}
        className={cn(
          'block w-full overflow-hidden rounded-lg ring-offset-2 ring-offset-bg transition focus-visible:outline-2 focus-visible:outline-ring',
          selected && 'ring-2 ring-accent-400'
        )}
        aria-label={m.fileName ?? (m.mediaType === 'video' ? t('Play video') : t('View photo'))}
      >
        <Thumbnail channelId={channelId} m={m} className={cn('aspect-square transition-transform duration-300 group-hover:scale-[1.03]', selected && 'scale-95')} />
      </button>
      <span className={cn('absolute left-1.5 top-1.5 transition-opacity', !(selected || choosing) && onHover)}>
        <Checkbox checked={selected} onCheckedChange={() => selection.toggle(m, list)} aria-label={t('Select')} className="bg-surface/80 shadow-soft" />
      </span>
      <GoToButton onGoTo={onGoTo} className={cn('absolute bottom-1.5 right-1.5 bg-black/55 text-white transition-opacity hover:bg-black/75 hover:text-white', onHover)} />
    </li>
  );
}

function useDate() {
  const lang = getLanguage();
  return useMemo(() => {
    const fmt = new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short', year: 'numeric' });
    return (iso: string) => fmt.format(new Date(iso));
  }, [lang]);
}

function RowItem({ m, channelId, selection, list, onOpen, onGoTo, voice }: ItemProps & { voice: boolean }) {
  const date = useDate();
  const selected = selection.has(m.id);
  const choosing = selection.ids.length > 0;
  const name = m.title ? [m.performer, m.title].filter(Boolean).join(' – ') : (m.fileName ?? (voice ? t('Voice message') : t('Attachment')));
  return (
    <li className={cn('group flex items-center gap-2 rounded-xl px-2 py-1.5 hover:bg-surface-2', selected && 'bg-primary-soft')}>
      <Checkbox checked={selected} onCheckedChange={() => selection.toggle(m, list)} aria-label={t('Select')} />
      <button
        type="button"
        onClick={(e) => (choosing ? selection.toggle(m, list, e.shiftKey) : onOpen())}
        className="flex min-w-0 flex-1 items-center gap-3 rounded-lg text-left focus-visible:outline-2 focus-visible:outline-ring"
      >
        <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-accent-300 [&_svg]:size-5">
          {m.mediaType ? mediaIcon(voice && m.mediaType === 'audio' ? 'voice' : m.mediaType) : <FileIcon />}
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-medium text-fg">{name}</span>
          <span className="block truncate text-xs text-fg-subtle">
            {[m.duration ? formatTime(m.duration) : '', formatBytes(m.fileSize), voice ? '' : fileTypeLabel(m), date(m.date)].filter(Boolean).join(' · ')}
          </span>
        </span>
      </button>
      <GoToButton onGoTo={onGoTo} />
      <MessageActions m={m} channelId={channelId} onOpen={onOpen} onGoTo={onGoTo} />
    </li>
  );
}

function LinkItem({ m, channelId, onOpen, onGoTo }: ItemProps) {
  const date = useDate();
  const links = linksIn(m.text);
  return (
    <li className="group flex items-start gap-2 rounded-xl px-2 py-2 hover:bg-surface-2">
      <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-accent-300 [&_svg]:size-5">
        <Link2 />
      </span>
      <div className="min-w-0 flex-1">
        {links.map((l) => (
          <a key={l} href={linkHref(l)} target="_blank" rel="noreferrer noopener" className="block truncate text-sm font-medium text-accent-300 hover:underline">
            {l}
          </a>
        ))}
        {m.text && <p className="line-clamp-3 whitespace-pre-wrap break-words text-xs text-fg-muted">{m.text}</p>}
        <p className="text-xs text-fg-subtle">{date(m.date)}</p>
      </div>
      <GoToButton onGoTo={onGoTo} />
      <MessageActions m={m} channelId={channelId} onOpen={onOpen} onGoTo={onGoTo} />
    </li>
  );
}
