import type { ApiLibraryFileDto, ApiLibraryItemDetailDto, ApiLibraryItemDto } from '@tfm/api-client';
import { toast } from '@tfm/ui';
import { openViewer } from '@/features/player/public';
import { toViewerItem, type MediaFileDto } from '@/features/search/fileMedia';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { episodeCode, episodeFile, imageAtSize, resumeSeconds } from './libraryModel';

/**
 * Plays a library file in the video viewer with progress reporting (the
 * player sends `PUT /library/watch/...` for `watch`). `fromStart` ignores the
 * saved position.
 */
export function playLibraryFile(file: ApiLibraryFileDto, item?: Pick<ApiLibraryItemDto, 'title' | 'backdropUrl' | 'posterUrl'> | null, fromStart = false) {
  const base = toViewerItem(file.file as MediaFileDto, String(file.channelId));
  if (!base) {
    toast.error(t('This file cannot be played'));
    return;
  }
  const code = episodeCode(file.season, file.episode);
  const title = item?.title ?? file.item?.title;
  openViewer(
    [
      {
        ...base,
        kind: 'video',
        name: title ? (code ? `${title} · ${code}` : title) : file.fileName,
        thumbnailUrl: imageAtSize(item?.backdropUrl ?? item?.posterUrl ?? file.item?.backdropUrl, 'w780') ?? base.thumbnailUrl,
        watch: {
          channelId: String(file.channelId),
          fileId: file.fileId,
          startAtSeconds: fromStart ? 0 : resumeSeconds(file.watch)
        }
      }
    ],
    0
  );
}

/** What the main "Play" button of an item starts: the file in progress, the next episode, or the first version. */
export function primaryFile(item: ApiLibraryItemDetailDto): ApiLibraryFileDto | null {
  if (item.resume) return item.resume;
  if (item.nextUp) return episodeFile(item.nextUp);
  if (item.files.length) return item.files[0];
  const first = item.seasons.flatMap((s) => s.episodes)[0];
  return first ? episodeFile(first) : null;
}

/** Loads an item and plays its primary file (home hero). */
export async function playItem(id: string) {
  try {
    const item = await api.library.item(id);
    const file = primaryFile(item);
    if (file) playLibraryFile(file, item);
    else toast.error(t('This title has no playable file'));
  } catch (error) {
    toast.error(t('Could not load the title'), { description: errorMessage(error) });
  }
}
