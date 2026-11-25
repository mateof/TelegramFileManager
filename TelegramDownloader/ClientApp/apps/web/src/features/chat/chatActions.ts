import type { ApiChatMessageDto, ApiFileDto } from '@tfm/api-client';
import { toast } from '@tfm/ui';
import { downloadUrls } from '@/features/files/browser';
import { openViewer, playAudio, type AudioTrack, type ViewerItem } from '@/features/player/public';
import { toAudioTrack, toViewerItem, type MediaFileDto } from '@/features/search/fileMedia';
import { t } from '@/i18n';
import { messageMedia, type MessageMedia } from './chatModel';
import { api, errorMessage } from '@/lib/api';

/** URL of the JPEG preview of a photo/video message (404 when it has none). */
export function messageThumbnailUrl(channelId: string, messageId: number): string {
  return api.http.url(`/api/v1/channels/${encodeURIComponent(channelId)}/messages/${messageId}/thumbnail`);
}

/**
 * The indexed entry of a message, when there is one (it brings transcoding,
 * library progress and playlists). Looks the file up by name and keeps the
 * entry backed by this message.
 */
async function findIndexedFile(channelId: string, m: ApiChatMessageDto): Promise<ApiFileDto | null> {
  if (!m.fileName) return null;
  try {
    const res = await api.files.search(channelId, { q: m.fileName, filesOnly: true, pageSize: 50 });
    return res.items.find((f) => f.messageId === m.id) ?? res.items.find((f) => f.name === m.fileName && f.size === m.fileSize) ?? null;
  } catch {
    return null;
  }
}

/** Queues the attachments in the server downloads; `targetPath` is a local folder (`/` is the root). True when it was queued. */
export async function downloadMessages(channelId: string, messageIds: number[], targetPath?: string): Promise<boolean> {
  if (!messageIds.length) return false;
  try {
    const res = await api.transfers.downloadMessages({ chatId: Number(channelId), messageIds, targetPath });
    toast.success(t('Download queued'), {
      description: [
        t('{count} files sent to the server downloads', { count: res.accepted }),
        res.skipped.length ? t('{count} could not be resolved', { count: res.skipped.length }) : ''
      ]
        .filter(Boolean)
        .join('. ')
    });
    return true;
  } catch (error) {
    toast.error(t('Could not start the download'), { description: errorMessage(error) });
    return false;
  }
}

/** Saves the attachments with the browser, one after another. */
export function saveMessages(channelId: string, messages: ApiChatMessageDto[]) {
  void downloadUrls(messages.map((m) => ({ url: api.channels.messageContentUrl(channelId, m.id, 'attachment'), name: m.fileName ?? '' })));
}

function notIndexed(channelId: string, m: ApiChatMessageDto) {
  toast(t('This file is not in the channel index'), {
    description: t('Download it to the server, or refresh the channel index to play it here.'),
    action: { label: t('Download to server'), onClick: () => void downloadMessages(channelId, [m.id]) }
  });
}

/** Plays (audio) or opens in the viewer (photo, video, document) the media of a message. */
export async function openMessageMedia(channelId: string, channelName: string, m: ApiChatMessageDto, goTo?: ViewerItem['goTo']) {
  const file = (await findIndexedFile(channelId, m)) as MediaFileDto | null;
  if (m.mediaType === 'audio') {
    const track = file && toAudioTrack(file, channelId, channelName);
    if (track) {
      playAudio([track], 0);
      return;
    }
  } else {
    const item = file && toViewerItem(file, channelId);
    if (item) {
      openViewer([{ ...item, thumbnailUrl: item.thumbnailUrl ?? messageThumbnailUrl(channelId, m.id), goTo }], 0);
      return;
    }
  }

  // Not indexed: stream the attachment of the message itself
  const fallbackName = m.mediaType === 'video' ? t('Video') : m.mediaType === 'audio' ? t('Audio') : t('Photo');
  const media = messageMedia(channelId, m, (id, disposition) => api.channels.messageContentUrl(channelId, id, disposition), fallbackName);
  if (!media) {
    notIndexed(channelId, m);
    return;
  }
  if (media.kind === 'audio') {
    playAudio([toTrack(media, channelName)], 0);
    return;
  }
  const thumbnailUrl = media.kind === 'image' ? media.url : m.mediaType === 'video' ? messageThumbnailUrl(channelId, m.id) : undefined;
  openViewer([{ ...media, thumbnailUrl, goTo }], 0);
}

function toTrack(media: Extract<MessageMedia, { kind: 'audio' }>, source: string): AudioTrack {
  return { id: media.id, title: media.title, artist: media.artist, source, url: media.url, size: media.size, durationSeconds: media.durationSeconds };
}

/** Opens `m` among `list`: the audios become the queue and the photos a gallery; anything else opens alone. */
export function openAmong(
  channelId: string,
  channelName: string,
  list: ApiChatMessageDto[],
  m: ApiChatMessageDto,
  goTo: (message: ApiChatMessageDto) => ViewerItem['goTo']
) {
  const url = (id: number, disposition?: 'inline' | 'attachment') => api.channels.messageContentUrl(channelId, id, disposition);
  const own = messageMedia(channelId, m, url, t('Photo'));
  if (own?.kind === 'audio') {
    const tracks = list.flatMap((x) => {
      const media = messageMedia(channelId, x, url, t('Audio'));
      return media?.kind === 'audio' ? [toTrack(media, channelName)] : [];
    });
    playAudio(tracks, Math.max(0, tracks.findIndex((x) => x.id === own.id)));
    return;
  }
  if (own?.kind === 'image') {
    const items = list.flatMap((x): ViewerItem[] => {
      const media = messageMedia(channelId, x, url, t('Photo'));
      return media?.kind === 'image' ? [{ ...media, thumbnailUrl: messageThumbnailUrl(channelId, x.id), goTo: goTo(x) }] : [];
    });
    openViewer(items, Math.max(0, items.findIndex((x) => x.id === own.id)));
    return;
  }
  void openMessageMedia(channelId, channelName, m, goTo(m));
}
