import { useEffect } from 'react';
import { create } from 'zustand';
import type * as MusicMetadata from 'music-metadata';
import type { AudioTrack } from './public';

/**
 * Tags and cover art read from the audio file itself (ID3, Vorbis comments,
 * FLAC pictures...). music-metadata is big, so it is loaded on first use, and
 * only the first 512 KB of a file are fetched (where the tags live).
 */
export interface TrackMeta {
  title?: string;
  artist?: string;
  album?: string;
  /** data: URL of the embedded picture. */
  cover?: string;
}

const HEAD_BYTES = 512 * 1024;

interface MetaState {
  byId: Record<string, TrackMeta | null>;
}

const useMetaStore = create<MetaState>()(() => ({ byId: {} }));
const inflight = new Set<string>();

let mmPromise: Promise<typeof MusicMetadata> | null = null;
function loadMusicMetadata() {
  mmPromise ??= import('music-metadata').catch((e: unknown) => {
    mmPromise = null;
    throw e;
  });
  return mmPromise;
}

function blobToDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error ?? new Error('read failed'));
    reader.readAsDataURL(blob);
  });
}

async function readMeta(track: AudioTrack): Promise<TrackMeta | null> {
  const response = await fetch(track.url, { headers: { Range: `bytes=0-${HEAD_BYTES - 1}` }, credentials: 'same-origin' });
  if (!response.ok) return null;
  let bytes: Uint8Array;
  if (response.status === 206) {
    bytes = new Uint8Array(await response.arrayBuffer());
  } else {
    // No range support: read only the head of the stream
    const reader = response.body?.getReader();
    if (!reader) return null;
    const chunks: Uint8Array[] = [];
    let total = 0;
    while (total < HEAD_BYTES) {
      const { done, value } = await reader.read();
      if (done || !value) break;
      chunks.push(value);
      total += value.length;
    }
    void reader.cancel().catch(() => undefined);
    bytes = new Uint8Array(Math.min(total, HEAD_BYTES));
    let offset = 0;
    for (const c of chunks) {
      const part = c.subarray(0, Math.min(c.length, bytes.length - offset));
      bytes.set(part, offset);
      offset += part.length;
      if (offset >= bytes.length) break;
    }
  }
  const range = response.headers.get('content-range')?.match(/\/(\d+)/);
  const size = range ? Number(range[1]) : track.size;
  const mm = await loadMusicMetadata();
  const parsed = await mm.parseBuffer(bytes, { mimeType: response.headers.get('content-type') ?? undefined, size }, { duration: false, skipPostHeaders: true });
  const { common } = parsed;
  const picture = common.picture?.[0];
  const cover = picture ? await blobToDataUrl(new Blob([picture.data.slice()], { type: picture.format || 'image/jpeg' })) : undefined;
  return { title: common.title, artist: common.artist, album: common.album, cover };
}

/** Tags of `track` (null while unknown). Starts reading them the first time it is asked. */
export function useTrackMeta(track: AudioTrack | null): TrackMeta | null {
  const id = track?.id;
  const meta = useMetaStore((s) => (id ? (s.byId[id] ?? null) : null));
  useEffect(() => {
    if (!track || inflight.has(track.id) || track.id in useMetaStore.getState().byId) return;
    // Never parse in the background: the CPU spike after a track change can get the page frozen
    if (typeof document !== 'undefined' && document.hidden) return;
    inflight.add(track.id);
    readMeta(track)
      .catch(() => null)
      .then((m) => useMetaStore.setState((s) => ({ byId: { ...s.byId, [track.id]: m } })))
      .finally(() => inflight.delete(track.id));
  }, [track]);
  return meta;
}

/** Stable gradient for tracks without a cover (hue from the title). */
export function fallbackGradient(seed: string): string {
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) >>> 0;
  const a = h % 360;
  const b = (a + 50 + (h % 70)) % 360;
  return `linear-gradient(135deg, oklch(0.55 0.17 ${a}), oklch(0.35 0.14 ${b}))`;
}
