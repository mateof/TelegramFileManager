import type { ApiFileDto } from '@tfm/api-client';
import type { AudioTrack, ViewerItem } from '@/features/player/public';
import type { PlaylistCandidate } from '@/features/playlists/public';

/**
 * Helpers that turn an `ApiFileDto` (channel or local entry) into what the
 * player / viewer / playlist contracts expect. Shared by search, library and chat.
 */

/**
 * The C# `ApiFileDto` also carries the v1 media URLs; the generated typings do
 * not declare them yet, so they are read through this optional extension.
 */
export type MediaFileDto = ApiFileDto & {
  contentUrl?: string | null;
  contentDownloadUrl?: string | null;
  playbackUrl?: string | null;
  thumbnailUrl?: string | null;
};

export type MediaKind = ViewerItem['kind'];

const IMAGE = new Set(['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.avif', '.svg', '.heic']);
const TEXT = new Set(['.txt', '.md', '.log', '.json', '.xml', '.csv', '.srt', '.vtt', '.ini', '.yml', '.yaml', '.nfo']);

/** What the viewer would do with this file. */
export function mediaKind(file: Pick<ApiFileDto, 'category' | 'type'>): MediaKind {
  const ext = (file.type ?? '').toLowerCase();
  switch (file.category) {
    case 'Audio':
      return 'audio';
    case 'Video':
      return 'video';
    case 'Photo':
      return 'image';
  }
  if (IMAGE.has(ext)) return 'image';
  if (ext === '.pdf') return 'pdf';
  if (TEXT.has(ext)) return 'text';
  return 'other';
}

/** Title without the extension ("01 - Song.mp3" → "01 - Song"). */
export function stripExtension(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(0, dot) : name;
}

/** Inline URL of a file (v1 content URL first, legacy stream URL otherwise). */
export function inlineUrl(file: MediaFileDto): string | null {
  return file.contentUrl ?? file.playbackUrl ?? file.streamUrl ?? null;
}

/** `channelId` is `'local'` (or null) for the server local storage. */
export function isLocal(channelId: string | null | undefined): boolean {
  return !channelId || channelId === 'local';
}

export function toAudioTrack(file: MediaFileDto, channelId: string | null, source?: string): AudioTrack | null {
  const url = file.playbackUrl ?? inlineUrl(file);
  if (!url) return null;
  const local = isLocal(channelId);
  return {
    id: local ? file.id : `${channelId}:${file.id}`,
    title: stripExtension(file.name),
    source,
    url,
    channelId: local ? undefined : (channelId ?? undefined),
    fileId: local ? undefined : file.id,
    localPath: local ? file.id : undefined,
    size: file.size
  };
}

export function toViewerItem(file: MediaFileDto, channelId: string | null): ViewerItem | null {
  const url = inlineUrl(file);
  if (!url) return null;
  return {
    id: isLocal(channelId) ? file.id : `${channelId}:${file.id}`,
    name: file.name,
    kind: mediaKind(file),
    url,
    playbackUrl: file.playbackUrl ?? undefined,
    downloadUrl: file.contentDownloadUrl ?? file.downloadUrl ?? undefined,
    thumbnailUrl: file.thumbnailUrl ?? undefined,
    size: file.size
  };
}

export function toPlaylistCandidate(file: ApiFileDto, channelId: string | null): PlaylistCandidate {
  const local = isLocal(channelId);
  return {
    channelId: local ? 'local' : String(channelId),
    fileId: file.id,
    fileName: file.name,
    fileSize: file.size,
    filePath: local ? file.id : undefined,
    isLocalFile: local || undefined
  };
}

/** Route (relative to /app) of the folder that contains a file. */
export function containingFolderRoute(channelId: string | null, folderPath: string): string {
  const segments = folderPath
    .split('/')
    .filter(Boolean)
    .map((s) => encodeURIComponent(s));
  const tail = segments.length ? `/${segments.join('/')}` : '';
  return isLocal(channelId) ? `/local${tail}` : `/channels/${encodeURIComponent(String(channelId))}${tail}`;
}
