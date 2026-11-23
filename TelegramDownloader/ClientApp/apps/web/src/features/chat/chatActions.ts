import type { ApiChatMessageDto, ApiFileDto } from '@tfm/api-client';
import { toast } from '@tfm/ui';
import { openViewer, playAudio } from '@/features/player/public';
import { toAudioTrack, toViewerItem, type MediaFileDto } from '@/features/search/fileMedia';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';

/** URL of the JPEG preview of a photo/video message (404 when it has none). */
export function messageThumbnailUrl(channelId: string, messageId: number): string {
  return api.http.url(`/api/v1/channels/${encodeURIComponent(channelId)}/messages/${messageId}/thumbnail`);
}

/**
 * Raw messages have no stream URL: playing one goes through the channel index.
 * Looks the file up by name and keeps the entry backed by this message.
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

export async function downloadMessages(channelId: string, messageIds: number[]) {
  if (!messageIds.length) return;
  try {
    const res = await api.transfers.downloadMessages({ chatId: Number(channelId), messageIds });
    toast.success(t('Download queued'), { description: t('{count} files sent to the server downloads', { count: res.accepted }) });
  } catch (error) {
    toast.error(t('Could not start the download'), { description: errorMessage(error) });
  }
}

function notIndexed(channelId: string, m: ApiChatMessageDto) {
  toast(t('This file is not in the channel index'), {
    description: t('Download it to the server, or refresh the channel index to play it here.'),
    action: { label: t('Download to server'), onClick: () => void downloadMessages(channelId, [m.id]) }
  });
}

/** Plays (audio) or opens in the viewer (photo, video, document) the media of a message. */
export async function openMessageMedia(channelId: string, channelName: string, m: ApiChatMessageDto) {
  const file = (await findIndexedFile(channelId, m)) as MediaFileDto | null;
  if (m.mediaType === 'audio') {
    const track = file && toAudioTrack(file, channelId, channelName);
    if (track) playAudio([track], 0);
    else notIndexed(channelId, m);
    return;
  }
  const item = file && toViewerItem(file, channelId);
  if (item) {
    openViewer([{ ...item, thumbnailUrl: item.thumbnailUrl ?? messageThumbnailUrl(channelId, m.id) }], 0);
    return;
  }
  if (m.mediaType === 'photo') {
    // Not indexed: the preview is the best we can show
    const url = messageThumbnailUrl(channelId, m.id);
    openViewer([{ id: `${channelId}:msg:${m.id}`, name: m.fileName ?? t('Photo'), kind: 'image', url, thumbnailUrl: url }], 0);
    return;
  }
  notIndexed(channelId, m);
}
