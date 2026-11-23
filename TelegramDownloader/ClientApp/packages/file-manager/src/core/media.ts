import type { FileEntry, MediaKind } from '../types';
import { extensionOf } from './paths';

const KINDS: Record<Exclude<MediaKind, 'other'>, string[]> = {
  audio: ['mp3', 'flac', 'wav', 'ogg', 'oga', 'opus', 'm4a', 'aac', 'wma', 'alac', 'aiff', 'ape', 'mka'],
  video: ['mp4', 'mkv', 'webm', 'avi', 'mov', 'wmv', 'flv', 'm4v', 'mpg', 'mpeg', 'ts', 'm2ts', '3gp'],
  image: ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'avif', 'heic', 'tif', 'tiff', 'ico'],
  document: ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'odt', 'ods', 'odp', 'epub', 'rtf'],
  archive: ['zip', 'rar', '7z', 'tar', 'gz', 'tgz', 'bz2', 'xz', 'iso', 'dmg'],
  text: ['txt', 'md', 'json', 'xml', 'csv', 'log', 'yml', 'yaml', 'ini', 'srt', 'vtt', 'nfo', 'html', 'css', 'js', 'ts']
};

const BY_EXTENSION = new Map<string, MediaKind>();
for (const [kind, exts] of Object.entries(KINDS)) for (const e of exts) BY_EXTENSION.set(e, kind as MediaKind);

/** Media kind of a file name or extension (`mp3`, `.mp3` or `song.mp3`). */
export function mediaKindOf(nameOrExtension: string | undefined): MediaKind {
  if (!nameOrExtension) return 'other';
  const raw = nameOrExtension.startsWith('.') ? nameOrExtension.slice(1) : nameOrExtension;
  const ext = raw.includes('.') ? extensionOf(raw) : raw.toLowerCase();
  return (ext && BY_EXTENSION.get(ext)) || 'other';
}

/** Media kind of an entry: its own `mediaKind`, else from the extension/name. */
export function entryMediaKind(entry: FileEntry): MediaKind {
  if (entry.kind === 'folder') return 'other';
  return entry.mediaKind ?? mediaKindOf(entry.extension ?? entry.name);
}
