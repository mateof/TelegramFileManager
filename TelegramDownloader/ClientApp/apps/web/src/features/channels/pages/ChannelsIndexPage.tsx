import { Avatar, Badge, Card, EmptyState, Input, SegmentedControl, Skeleton } from '@tfm/ui';
import { Database, FolderTree, Search, Star } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { t } from '@/i18n';
import { api } from '@/lib/api';
import { PageHeader } from '@/shell/PageHeader';
import { useChannels } from '@/shell/useChannels';

/** `/channels`: pick a channel (indexed ones first). */
export default function ChannelsIndexPage() {
  const { data, isLoading } = useChannels();
  const [scope, setScope] = useState<'indexed' | 'all'>('indexed');
  const [query, setQuery] = useState('');
  const q = query.trim().toLocaleLowerCase();
  const channels = (data ?? [])
    .filter((c) => (scope === 'indexed' ? c.hasDatabase : true))
    .filter((c) => !q || c.name.toLocaleLowerCase().includes(q))
    .sort((a, b) => Number(b.isFavorite) - Number(a.isFavorite) || Number(b.hasDatabase) - Number(a.hasDatabase) || a.name.localeCompare(b.name));

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader title={t('Channels')} description={t('Pick a channel to browse its files.')} icon={<FolderTree />} />
      <div className="flex flex-wrap items-center gap-3">
        <Input leading={<Search />} value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('Filter channels...')} aria-label={t('Filter channels...')} className="w-full max-w-sm" />
        <SegmentedControl
          value={scope}
          onValueChange={setScope}
          aria-label={t('Channels')}
          options={[
            { value: 'indexed', label: t('Indexed') },
            { value: 'all', label: t('All') }
          ]}
        />
      </div>
      {isLoading ? (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-20 rounded-2xl" />
          ))}
        </div>
      ) : channels.length === 0 ? (
        <Card glass>
          <EmptyState icon={<FolderTree />} title={t('No channels')} description={scope === 'indexed' ? t('No channel is indexed yet: show all of them and open one to index it.') : undefined} />
        </Card>
      ) : (
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {channels.map((c) => (
            <li key={c.id}>
              <Link to={`/channels/${c.id}`} className="block rounded-2xl focus-visible:outline-2 focus-visible:outline-ring">
                <Card interactive className="flex items-center gap-3 p-3.5">
                  <Avatar src={api.channels.imageUrl(c.id)} name={c.name} square />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold text-fg">{c.name}</p>
                    <div className="mt-1 flex flex-wrap gap-1.5">
                      {c.hasDatabase ? (
                        <Badge tone="success" dot>
                          {t('Indexed')}
                        </Badge>
                      ) : (
                        <Badge>{t('Not indexed')}</Badge>
                      )}
                      {c.isOwner && <Badge tone="accent">{t('Owner')}</Badge>}
                    </div>
                  </div>
                  {c.isFavorite ? <Star className="size-4 fill-amber-400 text-amber-400" /> : c.hasDatabase ? <Database className="size-4 text-fg-subtle" /> : null}
                </Card>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
