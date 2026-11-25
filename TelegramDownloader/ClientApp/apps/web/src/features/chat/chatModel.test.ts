import type { ApiChatMessageDto } from '@tfm/api-client';
import { describe, expect, it } from 'vitest';
import { dayLabel, fileTypeLabel, groupByDay, highlightParts, initialSection, isVisualMedia, linkHref, linksIn, localDay, mergePages, messageMedia, nearestMessage, newerParam, olderParam, rangeOf, stepIndex, telegramMessageLink, visibleSections } from './chatModel';

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
  it('asks for the page at each end of the history until it ends', () => {
    const window = { items: [], hasOlder: true, hasNewer: true, oldestId: 5, newestId: 9 };
    expect(olderParam(window)).toEqual({ before: 5 });
    expect(newerParam(window)).toEqual({ after: 9 });
    expect(olderParam({ ...window, hasOlder: false })).toBeUndefined();
    expect(newerParam({ ...window, newestId: null })).toBeUndefined();
  });

  it('goes to the nearest message when the one wanted was deleted', () => {
    const list = [3, 7, 20].map((id) => msg(id, d(2026, 1, 1)));
    expect(nearestMessage(list, 7)?.id).toBe(7);
    expect(nearestMessage(list, 9)?.id).toBe(7);
    expect(nearestMessage(list, 99)?.id).toBe(20);
    expect(nearestMessage([], 1)).toBeUndefined();
  });

  it('selects a range in either direction', () => {
    expect(rangeOf([1, 2, 3, 4], 2, 4)).toEqual([2, 3, 4]);
    expect(rangeOf([1, 2, 3, 4], 4, 2)).toEqual([2, 3, 4]);
    expect(rangeOf([1, 2], 9, 2)).toEqual([2]);
  });

  it('highlights the words of a search as plain text', () => {
    expect(highlightParts('Hola (mundo) hola', 'hola (')).toEqual([
      { text: 'Hola', match: true },
      { text: ' ', match: false },
      { text: '(', match: true },
      { text: 'mundo) ', match: false },
      { text: 'hola', match: true }
    ]);
    expect(highlightParts('abc', ' ')).toEqual([{ text: 'abc', match: false }]);
  });

  it('finds the links of a text', () => {
    expect(linksIn('See https://a.com/x?y=1, (www.b.org) and https://a.com/x?y=1.')).toEqual(['https://a.com/x?y=1', 'www.b.org']);
    expect(linksIn(null)).toEqual([]);
    expect(linkHref('www.b.org')).toBe('https://www.b.org');
  });

  it('moves between results without leaving the list', () => {
    expect(stepIndex(0, 1, 2)).toBe(1);
    expect(stepIndex(1, 1, 2)).toBeNull();
    expect(stepIndex(0, -1, 2)).toBeNull();
  });

  it('offers the sections with something in them', () => {
    expect(visibleSections({ photos: 0, audio: 3, links: 1 }, 'audio')).toEqual(['audio', 'links']);
    expect(visibleSections({ audio: 3 }, 'photos')).toEqual(['photos', 'audio']);
    expect(visibleSections(undefined, 'photos')).toHaveLength(7);
    expect(initialSection(null, { photos: 0, documents: 2 })).toBe('documents');
    expect(initialSection('voice', { photos: 1 })).toBe('voice');
    expect(initialSection('nope', undefined)).toBe('photos');
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
