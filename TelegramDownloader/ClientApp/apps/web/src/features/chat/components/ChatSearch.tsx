import { useInfiniteQuery } from '@tanstack/react-query';
import { IconButton, Input, Spinner } from '@tfm/ui';
import { ChevronDown, ChevronUp, Search, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useSearchText } from '@/lib/useSearchText';
import { CHAT_SEARCH_DELAY_MS, PAGE_SIZE, stepIndex } from '../chatModel';

export interface ChatSearchProps {
  channelId: string;
  /** Takes to a result. */
  onHit: (messageId: number) => void;
  onQueryChange: (query: string) => void;
  onClose: () => void;
}

/** Search in the whole chat: the arrows (and Enter) go from result to result, the newest first. */
export function ChatSearch({ channelId, onHit, onQueryChange, onClose }: ChatSearchProps) {
  const text = useSearchText('', CHAT_SEARCH_DELAY_MS);
  const q = text.query;
  const [position, setPosition] = useState({ q: '', index: 0 });
  const index = position.q === q ? position.index : 0;

  const results = useInfiniteQuery({
    queryKey: ['chat', 'search', channelId, q],
    queryFn: ({ pageParam, signal }) => api.channels.searchMessages(channelId, { q, offsetId: pageParam, limit: PAGE_SIZE }, signal),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffsetId ?? undefined,
    enabled: q.length > 0,
    staleTime: 60_000,
    refetchOnWindowFocus: false
  });
  const hits = useMemo(() => {
    const seen = new Set<number>();
    return (results.data?.pages ?? []).flatMap((p) => p.items).filter((m) => !seen.has(m.id) && seen.add(m.id));
  }, [results.data]);
  const total = Math.max(results.data?.pages[0]?.total ?? 0, hits.length);
  const hitId = hits[index]?.id;

  const callbacks = useRef({ onHit, onQueryChange });
  useEffect(() => {
    callbacks.current = { onHit, onQueryChange };
  });
  useEffect(() => callbacks.current.onQueryChange(q), [q]);
  // Also the first result, as soon as it arrives
  useEffect(() => {
    if (hitId !== undefined) callbacks.current.onHit(hitId);
  }, [hitId]);

  const step = async (delta: 1 | -1) => {
    // Telegram may announce more results than it gives: the end is where the pages end
    const next = stepIndex(index, delta, results.hasNextPage ? hits.length + 1 : hits.length);
    if (next === null || results.isFetchingNextPage) return;
    if (next >= hits.length) {
      const page = await results.fetchNextPage();
      const known = new Set(hits.map((m) => m.id));
      if (!page.data?.pages.at(-1)?.items.some((m) => !known.has(m.id))) return;
    }
    setPosition({ q, index: next });
  };
  const busy = results.isLoading || results.isFetchingNextPage;

  return (
    <form
      role="search"
      className="flex items-center gap-1 border-b border-border bg-surface px-3 py-2 sm:px-6"
      onSubmit={(e) => {
        e.preventDefault();
        if (text.pending) text.commit();
        else void step(1);
      }}
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
        else if (e.key === 'Enter' && e.shiftKey && !text.pending) {
          e.preventDefault();
          void step(-1);
        }
      }}
    >
      <Input
        autoFocus
        className="min-w-0 flex-1"
        value={text.value}
        onChange={(e) => text.setValue(e.target.value)}
        placeholder={t('Search in this chat')}
        aria-label={t('Search in this chat')}
        enterKeyHint="search"
        leading={<Search />}
      />
      <span className="min-w-16 shrink-0 text-center text-xs tabular-nums text-fg-muted" role="status" aria-live="polite">
        {busy ? (
          <Spinner size="sm" label={t('Searching')} />
        ) : results.isError ? (
          <span className="text-danger" title={errorMessage(results.error)}>
            {t('Error')}
          </span>
        ) : !q || text.pending ? (
          ''
        ) : total === 0 ? (
          t('No results')
        ) : (
          `${index + 1} / ${total}`
        )}
      </span>
      <IconButton type="button" size="sm" label={t('Previous result (older)')} disabled={!hits.length || busy || (index >= hits.length - 1 && !results.hasNextPage)} onClick={() => void step(1)}>
        <ChevronUp />
      </IconButton>
      <IconButton type="button" size="sm" label={t('Next result (newer)')} disabled={!hits.length || busy || index === 0} onClick={() => void step(-1)}>
        <ChevronDown />
      </IconButton>
      <IconButton type="button" size="sm" label={t('Close the search')} onClick={onClose}>
        <X />
      </IconButton>
    </form>
  );
}
