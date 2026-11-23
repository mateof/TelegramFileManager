import {
  ApiErrorCodes,
  isApiError,
  type ApiLibraryEpisodeDto,
  type ApiLibraryFileDto,
  type ApiLibraryGenreDto,
  type ApiLibraryItemDto,
  type ApiLibraryScanStateDto,
  type ApiWatchStateDto,
  type LibraryItemsSortBy,
  type LibraryKind
} from '@tfm/api-client';

/** True for the `503 library_disabled` answer of the catalogue endpoints. */
export function isLibraryDisabled(error: unknown): boolean {
  return isApiError(error) && error.code === ApiErrorCodes.LibraryDisabled;
}

/** 0-100, clamped; 0 when nothing was played. */
export function progressPercent(watch: Pick<ApiWatchStateDto, 'progress' | 'positionMs' | 'durationMs'> | null | undefined): number {
  if (!watch) return 0;
  let p = Number.isFinite(watch.progress) ? watch.progress : 0;
  if (!p && watch.durationMs > 0) p = watch.positionMs / watch.durationMs;
  return Math.round(Math.min(1, Math.max(0, p)) * 1000) / 10;
}

/** Position to resume from, in seconds; 0 when finished or barely started. */
export function resumeSeconds(watch: ApiWatchStateDto | null | undefined): number {
  if (!watch || watch.completed) return 0;
  const s = Math.floor(watch.positionMs / 1000);
  return s >= 10 ? s : 0;
}

/** 132 → "2 h 12 min", 45 → "45 min". */
export function formatRuntime(minutes: number | null | undefined): string {
  if (!minutes || minutes <= 0) return '';
  const h = Math.floor(minutes / 60);
  const m = Math.round(minutes % 60);
  if (!h) return `${m} min`;
  return m ? `${h} h ${m} min` : `${h} h`;
}

/** 3725000 → "1:02:05", 65000 → "1:05". */
export function formatClock(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return h ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

/** Minutes left of a watch state, rounded up (0 when unknown). */
export function minutesLeft(watch: Pick<ApiWatchStateDto, 'positionMs' | 'durationMs'> | null | undefined): number {
  if (!watch || watch.durationMs <= 0) return 0;
  return Math.max(0, Math.ceil((watch.durationMs - watch.positionMs) / 60000));
}

/** "S01E02" (or "S01" / "E02" when only one is known). */
export function episodeCode(season: number | null | undefined, episode: number | null | undefined): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const s = season != null ? `S${pad(season)}` : '';
  const e = episode != null ? `E${pad(episode)}` : '';
  return s + e;
}

/** Replaces (or adds) the `size` query parameter of a library image URL. */
export function imageAtSize(url: string | null | undefined, size: string): string | null {
  if (!url) return null;
  const [base, query = ''] = url.split('?');
  const params = new URLSearchParams(query);
  params.set('size', size);
  return `${base}?${params.toString()}`;
}

const QUALITY = /\b(2160p|4k|1080p|720p|576p|480p|hdr10\+?|hdr|dv|remux|bluray|bdrip|web-?dl|webrip|hdtv|x265|hevc|x264|h\.?264|av1)\b/gi;
const LANGS = /\b(castellano|español|espanol|spanish|latino|english|ingl[eé]s|dual|multi|vose|vos|subs?)\b/gi;

/** Short description of a file version: "1080p · BluRay · x265 · Castellano · 4.2 GB". */
export function versionLabel(file: Pick<ApiLibraryFileDto, 'fileName' | 'sizeText'>): string {
  const tags = new Map<string, string>();
  for (const re of [QUALITY, LANGS]) for (const m of file.fileName.matchAll(re)) tags.set(m[0].toLowerCase(), m[0]);
  return [...tags.values(), file.sizeText].filter(Boolean).join(' · ');
}

/**
 * The item shown big on the home page: something in progress with a backdrop
 * first, then the best rated recent items with a backdrop. Rotates daily among
 * the top candidates so the home page is not always the same.
 */
export function pickFeatured(items: ApiLibraryItemDto[], now = new Date()): ApiLibraryItemDto | null {
  const withArt = items.filter((i) => i.backdropUrl);
  if (!withArt.length) return items[0] ?? null;
  const inProgress = withArt.filter((i) => i.watch?.inProgress);
  if (inProgress.length) return inProgress[0];
  const ranked = [...withArt].sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0)).slice(0, 5);
  const day = Math.floor(now.getTime() / 86_400_000);
  return ranked[day % ranked.length];
}

/** Genres worth a home row: the most populated ones (at least `min` items), up to `max`. */
export function genreRows(genres: ApiLibraryGenreDto[], max = 4, min = 3): ApiLibraryGenreDto[] {
  return [...genres]
    .filter((g) => g.count >= min)
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name))
    .slice(0, max);
}

/** A "continue watching" entry: what to show for a file in progress. */
export interface ContinueEntry {
  key: string;
  file: ApiLibraryFileDto;
  title: string;
  subtitle: string;
  imageUrl: string | null;
  percent: number;
  itemId: string | null;
}

/**
 * Builds the "continue watching" row: one entry per item (its most recent
 * file, the endpoint already sorts by last played), finished files dropped.
 * Unidentified files keep their own entry (title = parsed title or file name).
 */
export function continueEntries(files: ApiLibraryFileDto[]): ContinueEntry[] {
  const seen = new Set<string>();
  const out: ContinueEntry[] = [];
  for (const f of files) {
    if (f.watch?.completed) continue;
    const key = f.item?.id ?? `${f.channelId}:${f.fileId}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const isEpisode = f.season != null || f.episode != null;
    out.push({
      key,
      file: f,
      title: f.item?.title ?? f.parsed?.title ?? f.fileName,
      subtitle: isEpisode ? episodeCode(f.season, f.episode) : (f.item?.year ? String(f.item.year) : ''),
      imageUrl: f.item?.backdropUrl ?? f.item?.posterUrl ?? null,
      percent: progressPercent(f.watch),
      itemId: f.item?.id ?? f.itemId
    });
  }
  return out;
}

/** The file to play for an episode: the one in progress, else the first. */
export function episodeFile(ep: ApiLibraryEpisodeDto): ApiLibraryFileDto | null {
  if (!ep.files.length) return null;
  return ep.files.find((f) => f.watch && !f.watch.completed && f.watch.positionMs > 0) ?? ep.files[0];
}

/** 0-100 of a running scan (channels done / total). */
export function scanPercent(state: ApiLibraryScanStateDto | null | undefined): number {
  if (!state || state.channelsTotal <= 0) return state?.running ? 0 : 100;
  return Math.min(100, Math.round((state.channelsScanned / state.channelsTotal) * 100));
}

/** Browse filters as kept in the URL. */
export interface BrowseFilters {
  kind: LibraryKind | 'all';
  genre: string | null;
  /** First year of a decade (1990 = the 1990s). */
  decade: number | null;
  sortBy: LibraryItemsSortBy;
  desc: boolean;
  search: string;
}

const SORTS: LibraryItemsSortBy[] = ['title', 'year', 'added', 'rating', 'lastPlayed'];

export function parseBrowseFilters(params: URLSearchParams, kind: LibraryKind | 'all'): BrowseFilters {
  const sort = params.get('sort') as LibraryItemsSortBy | null;
  const decade = Number(params.get('decade'));
  const sortBy = sort && SORTS.includes(sort) ? sort : 'added';
  const dir = params.get('dir');
  return {
    kind,
    genre: params.get('genre') || null,
    decade: Number.isInteger(decade) && decade > 1800 && decade % 10 === 0 ? decade : null,
    sortBy,
    // Titles read A→Z by default; dates, ratings and "last played" newest/best first
    desc: dir === 'asc' ? false : dir === 'desc' ? true : sortBy !== 'title',
    search: params.get('q') ?? ''
  };
}

/** The items endpoint has no year filter: the decade is applied on the loaded pages. */
export function matchesDecade(item: Pick<ApiLibraryItemDto, 'year'>, decade: number | null): boolean {
  if (decade == null) return true;
  return item.year != null && item.year >= decade && item.year < decade + 10;
}

/** Decades offered by the filter, newest first, from `from` to the current one. */
export function decades(now = new Date(), from = 1920): number[] {
  const out: number[] = [];
  for (let d = Math.floor(now.getFullYear() / 10) * 10; d >= from; d -= 10) out.push(d);
  return out;
}
