import type { ApiChatMessageDto } from '@tfm/api-client';
import { describe, expect, it } from 'vitest';
import { dayLabel, fileTypeLabel, groupByDay, hasOlderPage, isVisualMedia, localDay, mergePages, messageMedia, telegramMessageLink } from './chatModel';

function msg(id: number, date: Date, extra: Partial<ApiChatMessageDto> = {}): ApiChatMessageDto {
  return { id, date: date.toISOString(), text: null, hasMedia: false, mediaType: null, fileName: null, fileSize: 0, mimeType: null, from: null, ...extra };
}

// Local dates, so the tests do not depend on the machine time zone
const d = (y: number, m: number, day: number, h = 12) => new Date(y, m - 1, day, h);

describe('mergePages', () => {
  it('merges newest-first pages into one chronological list without duplicates', () => {
    const page1 = [msg(5, d(2026, 3, 2)), msg(4, d(2026, 3, 2))];
    const page2 = [msg(4, d(2026, 3, 2)), msg(3, d(2026, 3, 1)), msg(1, d(2026, 2, 28))];
    expect(mergePages([page1, page2]).map((m) => m.id)).toEqual([1, 3, 4, 5]);
    expect(mergePages([])).toEqual([]);
  });
});

describe('groupByDay', () => {
  it('starts a new group on every local calendar day, keeping the order', () => {
    const list = [msg(1, d(2026, 2, 28, 23)), msg(2, d(2026, 3, 1, 0)), msg(3, d(2026, 3, 1, 18)), msg(4, d(2026, 3, 3))];
    const groups = groupByDay(list);
    expect(groups.map((g) => g.day)).toEqual(['2026-02-28', '2026-03-01', '2026-03-03']);
    expect(groups[1].messages.map((m) => m.id)).toEqual([2, 3]);
  });

  it('computes the local day of an ISO date', () => {
    expect(localDay(d(2026, 12, 31, 23).toISOString())).toBe('2026-12-31');
  });
});

describe('dayLabel', () => {
  const labels = { today: 'Today', yesterday: 'Yesterday' };
  const now = d(2026, 11, 23, 10);
  it('says today / yesterday', () => {
    expect(dayLabel('2026-11-23', 'en', labels, now)).toBe('Today');
    expect(dayLabel('2026-11-22', 'en', labels, now)).toBe('Yesterday');
  });
  it('omits the year for this year and shows it otherwise', () => {
    expect(dayLabel('2026-11-02', 'en', labels, now)).not.toMatch(/2026/);
    expect(dayLabel('2025-11-02', 'en', labels, now)).toMatch(/2025/);
  });
});

describe('message helpers', () => {
  it('detects the end of the history', () => {
    expect(hasOlderPage([msg(1, d(2026, 1, 1))])).toBe(true);
    expect(hasOlderPage([])).toBe(false);
    expect(hasOlderPage(undefined)).toBe(false);
  });

  it('keeps photos and videos for the grid', () => {
    expect(isVisualMedia(msg(1, d(2026, 1, 1), { hasMedia: true, mediaType: 'photo' }))).toBe(true);
    expect(isVisualMedia(msg(1, d(2026, 1, 1), { hasMedia: true, mediaType: 'audio' }))).toBe(false);
  });

  it('builds t.me links for channels', () => {
    expect(telegramMessageLink('1290586824', 88)).toBe('https://t.me/c/1290586824/88');
    expect(telegramMessageLink('-1001290586824', 88)).toBe('https://t.me/c/1290586824/88');
    expect(telegramMessageLink('abc', 1)).toBeNull();
  });

  it('labels the file type', () => {
    expect(fileTypeLabel({ fileName: 'a.Mp3', mimeType: null })).toBe('MP3');
    expect(fileTypeLabel({ fileName: null, mimeType: 'application/pdf' })).toBe('PDF');
    expect(fileTypeLabel({ fileName: 'noext', mimeType: null })).toBe('');
  });
});

describe('messageMedia', () => {
  const url = (id: number, disposition?: string) => `/api/v1/channels/5/messages/${id}/content${disposition ? `?disposition=${disposition}` : ''}`;
  const at = new Date(2024, 0, 1);

  it('streams videos, photos and PDFs of a message without the index', () => {
    const video = messageMedia('5', msg(7, at, { hasMedia: true, mediaType: 'video', fileName: null, fileSize: 900, mimeType: 'video/mp4' }), url, 'Video');
    expect(video).toEqual({
      kind: 'video',
      id: '5:msg:7',
      name: 'Video',
      url: '/api/v1/channels/5/messages/7/content',
      downloadUrl: '/api/v1/channels/5/messages/7/content?disposition=attachment',
      mimeType: 'video/mp4',
      size: 900
    });
    expect(messageMedia('5', msg(8, at, { hasMedia: true, mediaType: 'photo' }), url, 'Photo')).toMatchObject({ kind: 'image', url: '/api/v1/channels/5/messages/8/content' });
    expect(messageMedia('5', msg(9, at, { hasMedia: true, mediaType: 'document', fileName: 'Manual.PDF' }), url, 'x')).toMatchObject({ kind: 'pdf', name: 'Manual.PDF' });
  });

  it('gives audio as a player track', () => {
    expect(messageMedia('5', msg(10, at, { hasMedia: true, mediaType: 'audio', fileName: '01 - Song.flac', fileSize: 42 }), url, 'Audio')).toEqual({
      kind: 'audio',
      id: '5:msg:10',
      title: '01 - Song',
      url: '/api/v1/channels/5/messages/10/content',
      size: 42
    });
  });

  it('leaves other files and text messages alone', () => {
    expect(messageMedia('5', msg(11, at, { hasMedia: true, mediaType: 'document', fileName: 'backup.zip' }), url, 'x')).toBeNull();
    expect(messageMedia('5', msg(12, at), url, 'x')).toBeNull();
  });
});
