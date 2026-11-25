import type { ViewerItem } from '../public';

/** Containers browsers do not play natively (they go to the transcoding URL when there is one). */
const NON_NATIVE = new Set(['mkv', 'avi', 'wmv', 'flv', 'mpg', 'mpeg', 'ts', 'm2ts', 'vob', '3gp', 'rm', 'rmvb', 'divx', 'asf', 'f4v']);

export function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot + 1).toLowerCase() : '';
}

/**
 * First source to try for a video: the direct URL when the browser can play
 * the container, the transcoding `playbackUrl` otherwise.
 */
export function pickVideoSource(item: ViewerItem, canPlayType: (mime: string) => string): string {
  const alt = item.playbackUrl;
  if (!alt || alt === item.url) return item.url;
  if (NON_NATIVE.has(extensionOf(item.name))) return alt;
  if (item.mimeType && item.mimeType.startsWith('video/') && canPlayType(item.mimeType) === '') return alt;
  return item.url;
}

/**
 * Source to fall back to after `current` failed, or null when there is nothing
 * else to try. Each URL is tried once, in both directions: a direct stream the
 * browser cannot decode goes to the transcoder, and a transcoder that is off
 * (403), missing FFmpeg or failing goes to the direct stream (Chrome and
 * Firefox play many MKVs, AVIs... with H.264/AAC inside).
 */
export function fallbackVideoSource(item: ViewerItem, current: string, tried: readonly string[] = []): string | null {
  const done = new Set([...tried, current]);
  for (const candidate of [item.playbackUrl, item.url]) {
    if (candidate && !done.has(candidate)) return candidate;
  }
  return null;
}

/** Interval between watch-progress reports while a library item plays. */
export const WATCH_REPORT_MS = 15_000;

/** Body of `PUT /api/v1/library/watch/{channelId}/{fileId}` for a position, or null when it is not worth sending. */
export function watchReport(position: number, duration: number, ended = false) {
  if (!Number.isFinite(duration) || duration <= 0 || !Number.isFinite(position)) return null;
  if (!ended && position < 5) return null; // just opened: nothing to remember
  return { positionMs: Math.round(position * 1000), durationMs: Math.round(duration * 1000), completed: ended ? true : null };
}
