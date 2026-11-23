import { describe, expect, it } from 'vitest';
import { buildLogQuery, EMPTY_FILTERS, filtersFromParams, filtersToParams, levelTone, shortLogger } from './model';

describe('logs model', () => {
  it('builds the query with only the active filters', () => {
    expect(buildLogQuery(EMPTY_FILTERS, 1, 100)).toEqual({ page: 1, pageSize: 100, sortDescending: true });
    const q = buildLogQuery({ ...EMPTY_FILTERS, level: 'Error', search: ' timeout ', from: '2026-01-02T10:30' }, 2, 50);
    expect(q).toMatchObject({ page: 2, pageSize: 50, level: 'Error', search: 'timeout' });
    expect(q.fromDate).toBe(new Date('2026-01-02T10:30').toISOString());
    expect(q.toDate).toBeUndefined();
  });

  it('round-trips the filters through the URL', () => {
    const f = { ...EMPTY_FILTERS, logger: 'A.B.C', version: '3.7' };
    expect(filtersFromParams(new URLSearchParams(filtersToParams(f)))).toEqual(f);
    expect(filtersToParams(EMPTY_FILTERS)).toEqual({});
  });

  it('shortens logger names and maps levels to tones', () => {
    expect(shortLogger('TelegramDownloader.Data.TelegramService')).toBe('TelegramService');
    expect(shortLogger(null)).toBe('');
    expect(levelTone('Fatal')).toBe('danger');
    expect(levelTone('Warning')).toBe('warning');
  });
});
