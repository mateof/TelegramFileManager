import type { ApiFileDto, ApiGlobalSearchResultDto, BrowseFilter, GlobalSearchScope } from '@tfm/api-client';

/** Minimum query length accepted by `GET /api/v1/search`. */
export const MIN_QUERY_LENGTH = 2;

export const SEARCH_FILTERS: BrowseFilter[] = ['all', 'audio', 'video', 'photo', 'document', 'archive'];
export const SEARCH_SCOPES: GlobalSearchScope[] = ['all', 'channels', 'local'];

/** One block of results on the page: a channel or the local storage. */
export interface SearchGroup {
  /** `channel:<id>` or `local`. */
  key: string;
  kind: 'channel' | 'local';
  /** Channel id, or null for the local storage. */
  channelId: string | null;
  title: string;
  files: ApiFileDto[];
  /** Counts per category in this group (Audio, Video...). */
  categories: Record<string, number>;
}

export interface SearchSummary {
  groups: SearchGroup[];
  total: number;
  channelCount: number;
  truncated: boolean;
}

/** Trimmed query, or null when it is too short to be sent. */
export function normaliseQuery(raw: string | null | undefined): string | null {
  const q = (raw ?? '').trim();
  return q.length >= MIN_QUERY_LENGTH ? q : null;
}

export function parseFilter(raw: string | null): BrowseFilter {
  return SEARCH_FILTERS.includes(raw as BrowseFilter) ? (raw as BrowseFilter) : 'all';
}

export function parseScope(raw: string | null): GlobalSearchScope {
  return SEARCH_SCOPES.includes(raw as GlobalSearchScope) ? (raw as GlobalSearchScope) : 'all';
}

function countCategories(files: ApiFileDto[]): Record<string, number> {
  const out: Record<string, number> = {};
  for (const f of files) out[f.category] = (out[f.category] ?? 0) + 1;
  return out;
}

function byName(a: ApiFileDto, b: ApiFileDto) {
  return a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });
}

/**
 * Turns the server answer into the groups rendered by the page: one per
 * channel (server order: by channel name), then the local storage. Folders
 * are dropped (the endpoint only returns files, but be defensive), duplicate
 * ids inside a group are removed and files are sorted naturally by name.
 */
export function groupSearchResults(result: ApiGlobalSearchResultDto | null | undefined, localTitle: string): SearchSummary {
  if (!result) return { groups: [], total: 0, channelCount: 0, truncated: false };
  const clean = (files: ApiFileDto[]) => {
    const seen = new Set<string>();
    return files
      .filter((f) => f.isFile && !seen.has(f.id) && (seen.add(f.id), true))
      .sort(byName);
  };

  const groups: SearchGroup[] = [];
  for (const g of result.channels ?? []) {
    const files = clean(g.files ?? []);
    if (!files.length) continue;
    groups.push({
      key: `channel:${g.channelId}`,
      kind: 'channel',
      channelId: String(g.channelId),
      title: g.channelName || String(g.channelId),
      files,
      categories: countCategories(files)
    });
  }
  const channelCount = groups.length;
  const local = clean(result.local ?? []);
  if (local.length) {
    groups.push({ key: 'local', kind: 'local', channelId: null, title: localTitle, files: local, categories: countCategories(local) });
  }
  return {
    groups,
    total: groups.reduce((n, g) => n + g.files.length, 0),
    channelCount,
    truncated: !!result.truncated
  };
}

/** Splits `text` into parts, flagging the (case-insensitive) occurrences of `query` for highlighting. */
export function highlightParts(text: string, query: string): { text: string; match: boolean }[] {
  const q = query.trim().toLowerCase();
  if (!q) return [{ text, match: false }];
  const lower = text.toLowerCase();
  const parts: { text: string; match: boolean }[] = [];
  let from = 0;
  for (let at = lower.indexOf(q); at !== -1; at = lower.indexOf(q, from)) {
    if (at > from) parts.push({ text: text.slice(from, at), match: false });
    parts.push({ text: text.slice(at, at + q.length), match: true });
    from = at + q.length;
  }
  if (from < text.length) parts.push({ text: text.slice(from), match: false });
  return parts;
}
