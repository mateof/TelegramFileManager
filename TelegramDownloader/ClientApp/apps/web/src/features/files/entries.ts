import type { ApiFileDto, BrowseFilter, BrowseSortBy } from '@tfm/api-client';
import { entryMediaKind, extensionOf, joinPath, mediaKindOf, normalizePath, type FileEntry, type ListFilter, type MediaKind, type SortSpec } from '@tfm/file-manager';
import type { AudioTrack, ViewerItem } from '@/features/player/public';

const CATEGORY_KIND: Record<string, MediaKind> = {
  Audio: 'audio',
  Video: 'video',
  Photo: 'image',
  Document: 'document',
  Archive: 'archive'
};

/** API folder path (`/music/rock/`, root `/`) from a file manager path (`/music/rock`). */
export function toApiFolder(path: string): string {
  const p = normalizePath(path);
  return p === '/' ? '/' : `${p}/`;
}

/** Maps an `ApiFileDto` (channel, collection or local entry) to a file manager entry. */
export function toEntry(dto: ApiFileDto): FileEntry {
  const parentPath = normalizePath(dto.path);
  const isFile = dto.isFile;
  const ext = isFile ? (dto.type?.startsWith('.') ? dto.type.slice(1).toLowerCase() : extensionOf(dto.name)) : undefined;
  const byExt = isFile ? mediaKindOf(ext ?? dto.name) : undefined;
  const byCategory = CATEGORY_KIND[dto.category];
  return {
    id: dto.id,
    name: dto.name,
    kind: isFile ? 'file' : 'folder',
    path: joinPath(parentPath, dto.name),
    parentPath,
    size: isFile || dto.size ? dto.size : undefined,
    modifiedAt: dto.dateModified,
    createdAt: dto.dateCreated,
    extension: ext,
    // The extension is more precise for text/document files; the category wins for media
    mediaKind: isFile ? (byCategory && byCategory !== 'document' ? byCategory : byExt !== 'other' ? byExt : byCategory ?? 'other') : undefined,
    hasChildren: dto.hasChildren,
    data: dto
  };
}

/** The DTO behind an entry built by {@link toEntry}. */
export function dtoOf(entry: FileEntry): ApiFileDto {
  return entry.data as ApiFileDto;
}

/** `sortBy` / `sortDescending` of the browse endpoints. */
export function apiSort(sort: SortSpec | undefined): { sortBy?: BrowseSortBy; sortDescending?: boolean } {
  if (!sort) return {};
  const map: Partial<Record<SortSpec['field'], BrowseSortBy>> = { name: 'name', modifiedAt: 'date', size: 'size', type: 'type' };
  return { sortBy: map[sort.field] ?? 'name', sortDescending: sort.direction === 'desc' };
}

/** `filter` of the browse endpoints: only one category can be sent; several are filtered client side. */
export function apiFilter(filter: ListFilter | undefined): BrowseFilter | undefined {
  const kinds = filter?.mediaKinds;
  if (!kinds || kinds.length !== 1) return undefined;
  const map: Partial<Record<MediaKind, BrowseFilter>> = { audio: 'audio', video: 'video', image: 'photo', document: 'document', archive: 'archive' };
  return map[kinds[0]];
}

function stripExtension(name: string) {
  const i = name.lastIndexOf('.');
  return i > 0 ? name.slice(0, i) : name;
}

/** An audio entry as a player track. */
export function toTrack(entry: FileEntry, source: { channelId?: string; sourceName?: string; local?: boolean }): AudioTrack {
  const dto = dtoOf(entry);
  return {
    id: source.local ? `local:${dto.id}` : dto.id,
    title: stripExtension(entry.name),
    source: source.sourceName,
    url: dto.playbackUrl ?? dto.streamUrl ?? dto.contentUrl ?? '',
    channelId: source.local ? undefined : source.channelId,
    fileId: source.local ? undefined : dto.id,
    localPath: source.local ? dto.id : undefined,
    size: dto.size
  };
}

export type ViewerKind = ViewerItem['kind'];

/** Viewer kind of an entry, or null when the viewer cannot show it. */
export function viewerKind(entry: FileEntry): ViewerKind | null {
  const kind = entryMediaKind(entry);
  if (kind === 'image') return 'image';
  if (kind === 'video') return 'video';
  if (kind === 'audio') return 'audio';
  if (entry.extension === 'pdf') return 'pdf';
  if (kind === 'text') return 'text';
  return null;
}

/** An entry as a viewer item (inline URL, optional transcoding URL). */
export function toViewerItem(entry: FileEntry, inlineUrl?: string, downloadUrl?: string): ViewerItem {
  const dto = dtoOf(entry);
  return {
    id: dto.id,
    name: entry.name,
    kind: viewerKind(entry) ?? 'other',
    url: inlineUrl ?? dto.contentUrl ?? dto.streamUrl ?? '',
    playbackUrl: dto.playbackUrl ?? undefined,
    downloadUrl: downloadUrl ?? dto.contentDownloadUrl ?? dto.downloadUrl ?? undefined,
    thumbnailUrl: dto.thumbnailUrl ?? undefined,
    size: dto.size
  };
}
