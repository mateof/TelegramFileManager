import type { ApiChatMessageDto } from '@tfm/api-client';

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
 * Merges the pages of the history (each newest first, older pages after) into
 * one chronological list (oldest first), without duplicates: when new messages
 * arrive between two page loads the offsets shift and a message can come twice.
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

/** True when an older page may exist (an empty page means the start of the chat). */
export function hasOlderPage(lastPage: ApiChatMessageDto[] | undefined): boolean {
  return !!lastPage && lastPage.length > 0;
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
  | { kind: 'audio'; id: string; title: string; url: string; size: number }
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
    return { kind: 'audio', id, title: dot > 0 ? name.slice(0, dot) : name, url, size: m.fileSize };
  }
  const kind =
    m.mediaType === 'video' ? 'video' : m.mediaType === 'photo' || IMAGE_EXT.has(ext) ? 'image' : ext === '.pdf' || m.mimeType === 'application/pdf' ? 'pdf' : null;
  if (!kind) return null;
  return { kind, id, name, url, downloadUrl: contentUrl(m.id, 'attachment'), mimeType: m.mimeType ?? undefined, size: m.fileSize };
}
