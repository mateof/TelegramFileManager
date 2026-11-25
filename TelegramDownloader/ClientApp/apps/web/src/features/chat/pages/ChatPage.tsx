import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { Avatar, Button, cn, EmptyState, IconButton, SegmentedControl, Skeleton } from '@tfm/ui';
import { CheckSquare, LayoutGrid, MessagesSquare, RefreshCw, Search, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useChannels } from '@/shell/useChannels';
import { mergePages, newerParam, olderParam, PAGE_SIZE, type WindowParam } from '../chatModel';
import { ChatSearch } from '../components/ChatSearch';
import { MediaBrowser } from '../components/MediaBrowser';
import { MessageList } from '../components/MessageList';
import { SelectionBar } from '../components/SelectionBar';
import { useSelection } from '../useSelection';

type View = 'messages' | 'media';

/** `/chat/:id`: `?message=` opens at a message, `?view=media&kind=` the media by kind. */
export default function ChatPage() {
  const { id = '' } = useParams();
  return <Chat key={id} id={id} />;
}

function Chat({ id }: { id: string }) {
  const channels = useChannels();
  const channel = channels.data?.find((c) => String(c.id) === id);
  const name = channel?.name ?? id;
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const view: View = params.get('view') === 'media' ? 'media' : 'messages';
  const wanted = Number(params.get('message')) || null;
  const [searching, setSearching] = useState(false);
  const [query, setQuery] = useState('');
  const [selecting, setSelecting] = useState(false);
  const selection = useSelection();

  useEffect(() => {
    document.title = `${name} · TFM`;
  }, [name]);

  // Where the loaded part of the history starts from: a message, or the end
  const [anchor, setAnchor] = useState<number | null>(wanted);
  const history = useInfiniteQuery({
    queryKey: ['chat', 'history', id, anchor ?? 'latest'],
    queryFn: ({ pageParam, signal }) => api.channels.messagesWindow(id, { ...pageParam, limit: PAGE_SIZE }, signal),
    initialPageParam: (anchor === null ? {} : { around: anchor }) as WindowParam,
    getNextPageParam: olderParam,
    getPreviousPageParam: newerParam,
    enabled: !!id && view === 'messages',
    staleTime: Infinity,
    // Every page would be asked again: it starts over instead, with one
    gcTime: 0
  });
  const messages = useMemo(() => mergePages(history.data?.pages.map((p) => p.items) ?? []), [history.data]);
  const withFiles = useMemo(() => messages.filter((m) => m.hasMedia), [messages]);

  // Taking to a message: in the loaded part, or loading the part around it
  const [jump, setJump] = useState<{ id: number; nonce: number } | null>(wanted === null ? null : { id: wanted, nonce: 1 });
  const goTo = (messageId: number, keepInHistory: boolean) => {
    if (!messages.some((m) => m.id === messageId)) setAnchor(messageId);
    setJump((j) => ({ id: messageId, nonce: (j?.nonce ?? 0) + 1 }));
    setParams({ message: String(messageId) }, { replace: !keepInHistory });
  };
  // Back and forward, or another link to this chat
  const [seen, setSeen] = useState(wanted);
  if (wanted !== seen) {
    setSeen(wanted);
    if (wanted !== null && jump?.id !== wanted) {
      if (!messages.some((m) => m.id === wanted)) setAnchor(wanted);
      setJump({ id: wanted, nonce: (jump?.nonce ?? 0) + 1 });
    }
  }
  const toLatest = () => {
    setAnchor(null);
    setJump(null);
    setParams({}, { replace: true });
  };
  const reload = () => {
    if (view === 'messages') {
      toLatest();
      void qc.resetQueries({ queryKey: ['chat', 'history', id] });
      return;
    }
    void qc.resetQueries({ queryKey: ['chat', 'media', id] });
    void qc.resetQueries({ queryKey: ['chat', 'counts', id] });
  };
  const setView = (v: View) => setParams(v === 'media' ? { view: 'media' } : {});
  const choosing = selecting || selection.ids.length > 0;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="glass z-10 flex flex-wrap items-center gap-3 border-b border-border px-4 py-3 sm:px-6">
        <Avatar src={api.channels.imageUrl(id)} name={name} size="md" square />
        <div className="min-w-40 flex-1">
          <h1 className="truncate text-lg font-semibold tracking-tight text-fg">{name}</h1>
          <p className="truncate text-xs text-fg-subtle">
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
        <div className="flex items-center gap-1">
          {view === 'messages' && (
            <>
              <IconButton
                label={t('Search in this chat')}
                aria-pressed={searching}
                onClick={() => {
                  setSearching(!searching);
                  setQuery('');
                }}
              >
                <Search />
              </IconButton>
              <IconButton
                label={t('Select files')}
                aria-pressed={choosing}
                onClick={() => {
                  if (choosing) selection.clear();
                  setSelecting(!choosing);
                }}
              >
                <CheckSquare />
              </IconButton>
            </>
          )}
          <IconButton label={t('Reload')} onClick={reload} disabled={history.isFetching}>
            <RefreshCw className={cn(history.isFetching && 'animate-spin')} />
          </IconButton>
        </div>
      </header>

      {view === 'media' ? (
        <MediaBrowser
          channelId={id}
          channelName={name}
          section={params.get('kind')}
          onSectionChange={(kind) => setParams({ view: 'media', kind }, { replace: true })}
          onGoTo={(messageId) => goTo(messageId, true)}
        />
      ) : (
        <>
          {searching && (
            <ChatSearch
              channelId={id}
              onHit={(messageId) => goTo(messageId, false)}
              onQueryChange={setQuery}
              onClose={() => {
                setSearching(false);
                setQuery('');
              }}
            />
          )}
          {choosing && (
            <SelectionBar
              channelId={id}
              selection={selection}
              available={withFiles}
              hint={t('Choose the messages with files to download')}
              onClose={() => setSelecting(false)}
            />
          )}
          {history.isPending ? (
            <ChatSkeleton />
          ) : history.isError && !history.data ? (
            <EmptyState
              icon={<TriangleAlert />}
              title={t('Could not read the messages')}
              description={errorMessage(history.error)}
              action={<Button onClick={() => void history.refetch()}>{t('Retry')}</Button>}
            />
          ) : messages.length === 0 && !history.hasNextPage ? (
            <EmptyState icon={<MessagesSquare />} title={t('No messages')} description={t('This chat has no messages yet.')} />
          ) : (
            <MessageList
              key={anchor ?? 'latest'}
              channelId={id}
              channelName={name}
              messages={messages}
              hasOlder={history.hasNextPage}
              loadingOlder={history.isFetchingNextPage}
              loadOlder={() => void history.fetchNextPage()}
              hasNewer={history.hasPreviousPage}
              loadingNewer={history.isFetchingPreviousPage}
              loadNewer={() => void history.fetchPreviousPage()}
              jump={jump}
              onLatest={toLatest}
              query={query}
              selecting={choosing}
              selection={selection}
            />
          )}
        </>
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
