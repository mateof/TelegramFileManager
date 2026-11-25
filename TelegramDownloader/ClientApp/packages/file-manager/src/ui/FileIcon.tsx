import { cn } from '@tfm/ui';
import { File, FileArchive, FileAudio, FileCode, FileImage, FileText, FileVideo, Folder } from 'lucide-react';
import { useState } from 'react';
import { entryMediaKind } from '../core/media';
import type { FileEntry, MediaKind } from '../types';

const KIND_STYLE: Record<MediaKind | 'folder', { icon: typeof File; tone: string }> = {
  folder: { icon: Folder, tone: 'text-accent-300 bg-primary-soft' },
  audio: { icon: FileAudio, tone: 'text-fuchsia-300 bg-fuchsia-500/15 light:text-fuchsia-600' },
  video: { icon: FileVideo, tone: 'text-rose-300 bg-rose-500/15 light:text-rose-600' },
  image: { icon: FileImage, tone: 'text-emerald-300 bg-emerald-500/15 light:text-emerald-600' },
  document: { icon: FileText, tone: 'text-sky-300 bg-sky-500/15 light:text-sky-600' },
  archive: { icon: FileArchive, tone: 'text-amber-300 bg-amber-500/15 light:text-amber-600' },
  text: { icon: FileCode, tone: 'text-teal-300 bg-teal-500/15 light:text-teal-600' },
  other: { icon: File, tone: 'text-fg-muted bg-surface-3/60' }
};

export function kindStyle(entry: FileEntry) {
  return KIND_STYLE[entry.kind === 'folder' ? 'folder' : entryMediaKind(entry)];
}

export interface FileIconProps {
  entry: FileEntry;
  /** Pixel size of the tile. */
  size?: 'sm' | 'md' | 'lg' | 'xl';
  className?: string;
}

const TILE = { sm: 'size-7 rounded-lg [&_svg]:size-4', md: 'size-9 rounded-xl [&_svg]:size-5', lg: 'size-14 rounded-2xl [&_svg]:size-7', xl: 'size-20 rounded-3xl [&_svg]:size-10' };

/** Tinted tile with the icon of the entry's kind. */
export function FileIcon({ entry, size = 'md', className }: FileIconProps) {
  const { icon: Icon, tone } = kindStyle(entry);
  return (
    <span aria-hidden className={cn('flex shrink-0 items-center justify-center', TILE[size], tone, className)}>
      <Icon strokeWidth={1.75} />
    </span>
  );
}

export interface EntryThumbProps extends FileIconProps {
  url?: string;
}

/** Thumbnail when the provider has one (falls back to the icon on error). */
export function EntryThumb({ entry, url, size = 'md', className }: EntryThumbProps) {
  const [failed, setFailed] = useState<string | null>(null);
  if (!url || failed === url) return <FileIcon entry={entry} size={size} className={className} />;
  return (
    <span className={cn('relative flex shrink-0 overflow-hidden bg-surface-3/60', TILE[size], className)}>
      <img src={url} alt="" loading="lazy" decoding="async" draggable={false} onError={() => setFailed(url)} className="size-full object-cover" />
    </span>
  );
}
