import type { ApiChatMessageDto } from '@tfm/api-client';
import { describe, expect, it } from 'vitest';
import { dayLabel, fileTypeLabel, groupByDay, hasOlderPage, isVisualMedia, localDay, mergePages, telegramMessageLink } from './chatModel';

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
