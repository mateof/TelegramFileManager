import type { ApiChannelDto } from '@tfm/api-client';

export type ChannelComboTab = 'mine' | 'favourites' | 'all';

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

/** Own channels first, then favourites, then the rest; natural order inside each group. */
export function sortChannels(list: ApiChannelDto[]): ApiChannelDto[] {
  const rank = (c: ApiChannelDto) => (c.isOwner ? 0 : c.isFavorite ? 1 : 2);
  return [...list].sort((a, b) => rank(a) - rank(b) || collator.compare(a.name, b.name));
}

function normalize(s: string) {
  return s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();
}

/** Channels of `tab` whose name (accent-insensitive) or id contains `query`. */
export function filterChannels(list: ApiChannelDto[], tab: ChannelComboTab, query: string): ApiChannelDto[] {
  const q = normalize(query.trim());
  return list.filter((c) => {
    if (tab === 'mine' && !c.isOwner) return false;
    if (tab === 'favourites' && !c.isFavorite) return false;
    return !q || normalize(c.name).includes(q) || String(c.id).includes(q);
  });
}

/** Tab to open with: own channels when there are any, else favourites, else all. */
export function initialTab(list: ApiChannelDto[]): ChannelComboTab {
  if (list.some((c) => c.isOwner)) return 'mine';
  if (list.some((c) => c.isFavorite)) return 'favourites';
  return 'all';
}
