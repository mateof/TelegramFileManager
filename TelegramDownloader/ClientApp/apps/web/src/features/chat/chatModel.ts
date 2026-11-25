import type { ApiChatMessageDto, ApiChatWindowDto, ChatMessageKind } from '@tfm/api-client';

/** How long the search in the chat waits after the last key (Enter does not wait). */
export const CHAT_SEARCH_DELAY_MS = 1200;

/** Messages requested per page (the endpoint accepts 1-100). */
export const PAGE_SIZE = 50;

export interface DayGroup {
  /** Local calendar day, `YYYY-MM-DD`. */
  day: string;
  /** Oldest first. */
  messages: ApiChatMessageDto[];
}

/** Local `YYYY-MM-DD` of an ISO date. */
export function localDay(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * Merges pages of messages into one chronological list (oldest first), without
 * duplicates.
 */
export function mergePages(pages: ApiChatMessageDto[][]): ApiChatMessageDto[] {
  const byId = new Map<number, ApiChatMessageDto>();
  for (const page of pages) for (const m of page) if (!byId.has(m.id)) byId.set(m.id, m);
  return [...byId.values()].sort((a, b) => a.id - b.id);
}

/** Splits a chronological list into calendar days (local time), keeping the order. */
export function groupByDay(messages: ApiChatMessageDto[]): DayGroup[] {
  const groups: DayGroup[] = [];
  for (const m of messages) {
    const day = localDay(m.date);
    const last = groups[groups.length - 1];
    if (last && last.day === day) last.messages.push(m);
    else groups.push({ day, messages: [m] });
  }
  return groups;
}

/** "Today", "Yesterday" or a long date in `lang`. `labels` carries the translated words. */
export function dayLabel(day: string, lang: string, labels: { today: string; yesterday: string }, now = new Date()): string {
  const today = localDay(now.toISOString());
  const y = new Date(now);
  y.setDate(y.getDate() - 1);
  if (day === today) return labels.today;
  if (day === localDay(y.toISOString())) return labels.yesterday;
  const [yy, mm, dd] = day.split('-').map(Number);
  const date = new Date(yy, mm - 1, dd);
  return new Intl.DateTimeFormat(lang, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric'
  }).format(date);
}

/** Which part of the history a page asks for. */
export type WindowParam = { around?: number; before?: number; after?: number };

/** The page before (older than) the oldest one loaded. */
export function olderParam(last: ApiChatWindowDto): WindowParam | undefined {
  return last.hasOlder && last.oldestId !== null ? { before: last.oldestId } : undefined;
}

/** The page after (newer than) the newest one loaded. */
export function newerParam(first: ApiChatWindowDto): WindowParam | undefined {
  return first.hasNewer && first.newestId !== null ? { after: first.newestId } : undefined;
}

/** The message to show for `id`: itself, or the nearest one when it was deleted. `messages` is oldest first. */
export function nearestMessage(messages: ApiChatMessageDto[], id: number): ApiChatMessageDto | undefined {
  let best: ApiChatMessageDto | undefined;
  for (const m of messages) if (!best || Math.abs(m.id - id) < Math.abs(best.id - id)) best = m;
  return best;
}

/** Ids from `from` to `to` in the order of `ids`, both included, whatever comes first. */
export function rangeOf(ids: number[], from: number, to: number): number[] {
  const a = ids.indexOf(from);
  const b = ids.indexOf(to);
  if (a < 0 || b < 0) return [to];
  return ids.slice(Math.min(a, b), Math.max(a, b) + 1);
}

/** Splits `text` at the words of `query`, to highlight them. */
export function highlightParts(text: string, query: string): { text: string; match: boolean }[] {
  const words = query.split(/\s+/).filter(Boolean);
  if (words.length === 0) return [{ text, match: false }];
  const pattern = words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|');
  return text
    .split(new RegExp(`(${pattern})`, 'gi'))
    .map((part, i) => ({ text: part, match: i % 2 === 1 }))
    .filter((p) => p.text !== '');
}

/** Web links of a text, without the punctuation that follows them. */
export function linksIn(text: string | null): string[] {
  const found = (text ?? '').match(/\b(?:https?:\/\/|www\.)[^\s<>"']+/gi) ?? [];
  return [...new Set(found.map((l) => l.replace(/[.,;:!?)\]}]+$/, '')))];
}

/** Absolute URL of a link written without the scheme. */
export function linkHref(link: string): string {
  return /^https?:/i.test(link) ? link : `https://${link}`;
}

/** Sections of the media view, in order. */
export const MEDIA_KINDS = ['photos', 'videos', 'documents', 'audio', 'voice', 'gifs', 'links'] as const satisfies readonly ChatMessageKind[];
export type MediaSection = (typeof MEDIA_KINDS)[number];

/** Sections shown as a grid of thumbnails. */
export function isGridSection(kind: MediaSection): boolean {
  return kind === 'photos' || kind === 'videos' || kind === 'gifs';
}

/** Sections with something in them; all while the counts are unknown. The open one always stays. */
export function visibleSections(counts: Partial<Record<string, number>> | undefined, current: MediaSection): MediaSection[] {
  return MEDIA_KINDS.filter((k) => k === current || !counts || (counts[k] ?? 0) > 0);
}

/** The section to open: the one asked for, or the first with something. */
export function initialSection(asked: string | null, counts: Partial<Record<string, number>> | undefined): MediaSection {
  const known = MEDIA_KINDS.find((k) => k === asked);
  if (known) return known;
  return MEDIA_KINDS.find((k) => (counts?.[k] ?? 0) > 0) ?? 'photos';
}

/** Next position of a search result: older is +1 (results come newest first). Null when there is none that way. */
export function stepIndex(index: number, delta: 1 | -1, total: number): number | null {
  const next = index + delta;
  return next < 0 || next >= total ? null : next;
}

/** Photos and videos only (for the media grid). */
export function isVisualMedia(m: ApiChatMessageDto): boolean {
  return m.hasMedia && (m.mediaType === 'photo' || m.mediaType === 'video');
}

/**
 * `t.me` link of a message. Channel ids come as the bare id (`1290586824`) or
 * with the `-100` prefix of the Bot API; private chats have no public link.
 */
export function telegramMessageLink(channelId: string, messageId: number): string | null {
  const bare = channelId.replace(/^-100/, '').replace(/^-/, '');
  if (!/^\d+$/.test(bare)) return null;
  return `https://t.me/c/${bare}/${messageId}`;
}

/** Extension of a file name in lower case with the dot, or ''. */
export function extensionOf(name: string | null | undefined): string {
  if (!name) return '';
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot).toLowerCase() : '';
}

/** Short type label for a file card: the extension ("MP3") or the MIME subtype. */
export function fileTypeLabel(m: Pick<ApiChatMessageDto, 'fileName' | 'mimeType'>): string {
  const ext = extensionOf(m.fileName);
  if (ext) return ext.slice(1).toUpperCase();
  const sub = m.mimeType?.split('/')[1];
  return sub ? sub.toUpperCase() : '';
}

/** What the player/viewer can do with the file of a message that is not in the channel index. */
export type MessageMedia =
  | { kind: 'audio'; id: string; title: string; artist?: string; url: string; size: number; durationSeconds?: number }
  | { kind: 'video' | 'image' | 'pdf'; id: string; name: string; url: string; downloadUrl: string; mimeType?: string; size: number };

const IMAGE_EXT = new Set(['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.avif']);

/**
 * Plays a message attachment straight from Telegram
 * (`GET /api/v1/channels/{id}/messages/{messageId}/content`), without
 * indexing it first: audio for the player, video/photo/PDF for the viewer.
 * Null for other files (they still go through "Download to server").
 */
export function messageMedia(
  channelId: string,
  m: ApiChatMessageDto,
  contentUrl: (messageId: number, disposition?: 'inline' | 'attachment') => string,
  fallbackName: string
): MessageMedia | null {
  if (!m.hasMedia) return null;
  const id = `${channelId}:msg:${m.id}`;
  const name = m.fileName || fallbackName;
  const ext = extensionOf(m.fileName);
  const url = contentUrl(m.id);
  if (m.mediaType === 'audio') {
    const dot = name.lastIndexOf('.');
    const title = m.title || (dot > 0 ? name.slice(0, dot) : name);
    return { kind: 'audio', id, title, artist: m.performer ?? undefined, url, size: m.fileSize, durationSeconds: m.duration ?? undefined };
  }
  const kind =
    m.mediaType === 'video' ? 'video' : m.mediaType === 'photo' || IMAGE_EXT.has(ext) ? 'image' : ext === '.pdf' || m.mimeType === 'application/pdf' ? 'pdf' : null;
  if (!kind) return null;
  return { kind, id, name, url, downloadUrl: contentUrl(m.id, 'attachment'), mimeType: m.mimeType ?? undefined, size: m.fileSize };
}
