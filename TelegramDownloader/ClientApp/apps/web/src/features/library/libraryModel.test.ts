import { ApiError, type ApiLibraryEpisodeDto, type ApiLibraryFileDto, type ApiLibraryItemDto, type ApiWatchStateDto } from '@tfm/api-client';
import { describe, expect, it } from 'vitest';
import {
  continueEntries,
  decades,
  episodeCode,
  episodeFile,
  formatClock,
  formatRuntime,
  genreRows,
  imageAtSize,
  isLibraryDisabled,
  matchesDecade,
  minutesLeft,
  parseBrowseFilters,
  pickFeatured,
  progressPercent,
  resumeSeconds,
  scanPercent,
  versionLabel
} from './libraryModel';

function watch(extra: Partial<ApiWatchStateDto> = {}): ApiWatchStateDto {
  return {
    channelId: 1,
    fileId: 'f',
    fileName: null,
    itemId: null,
    season: null,
    episode: null,
    positionMs: 0,
    durationMs: 0,
    progress: 0,
    completed: false,
    playCount: 0,
    firstPlayedAt: '',
    lastPlayedAt: '',
    ...extra
  };
}

function item(id: string, extra: Partial<ApiLibraryItemDto> = {}): ApiLibraryItemDto {
  return {
    id,
    kind: 'movie',
    title: id,
    originalTitle: null,
    year: 2000,
    overview: null,
    genres: [],
    rating: null,
    voteCount: null,
    runtime: null,
    posterUrl: null,
    backdropUrl: null,
    provider: 'tmdb',
    providerId: id,
    imdbId: null,
    externalIds: {},
    locked: false,
    fileCount: 1,
    channelIds: [1],
    seasonCount: 0,
    addedAt: '',
    watch: { completed: false, inProgress: false, positionMs: 0, durationMs: 0, progress: 0, episodesTotal: 0, episodesWatched: 0, lastPlayedAt: null },
    ...extra
  };
}

function file(fileId: string, extra: Partial<ApiLibraryFileDto> = {}): ApiLibraryFileDto {
  return {
    channelId: 1,
    fileId,
    fileName: `${fileId}.mkv`,
    folderPath: '/',
    size: 1,
    sizeText: '1 GB',
    status: 'matched',
    confidence: 1,
    matchSource: 'auto',
    locked: false,
    parsed: { title: `parsed ${fileId}`, year: null, season: null, episode: null, isSeries: false, source: '' },
    itemId: null,
    kind: null,
    season: null,
    episode: null,
    file: {} as ApiLibraryFileDto['file'],
    watch: null,
    item: null,
    scannedAt: '',
    error: null,
    ...extra
  };
}

describe('progress helpers', () => {
  it('computes the percentage from progress, or from the position', () => {
    expect(progressPercent(watch({ progress: 0.4464 }))).toBe(44.6);
    expect(progressPercent(watch({ positionMs: 30, durationMs: 120 }))).toBe(25);
    expect(progressPercent(watch({ progress: 1.7 }))).toBe(100);
    expect(progressPercent(null)).toBe(0);
  });

  it('resumes from the position unless finished or barely started', () => {
    expect(resumeSeconds(watch({ positionMs: 125_400 }))).toBe(125);
    expect(resumeSeconds(watch({ positionMs: 5_000 }))).toBe(0);
    expect(resumeSeconds(watch({ positionMs: 125_400, completed: true }))).toBe(0);
  });

  it('formats runtimes, clocks and minutes left', () => {
    expect(formatRuntime(132)).toBe('2 h 12 min');
    expect(formatRuntime(45)).toBe('45 min');
    expect(formatRuntime(120)).toBe('2 h');
    expect(formatRuntime(null)).toBe('');
    expect(formatClock(3_725_000)).toBe('1:02:05');
    expect(formatClock(65_000)).toBe('1:05');
    expect(minutesLeft(watch({ positionMs: 60_000, durationMs: 150_000 }))).toBe(2);
  });

  it('builds episode codes', () => {
    expect(episodeCode(1, 2)).toBe('S01E02');
    expect(episodeCode(10, null)).toBe('S10');
    expect(episodeCode(null, null)).toBe('');
  });
});

describe('continueEntries', () => {
  it('keeps the most recent file per item and drops finished ones', () => {
    const series = item('bb', { kind: 'series', title: 'Breaking Bad', backdropUrl: 'http://h/b?size=w1280' });
    const files = [
      file('done', { watch: watch({ completed: true }), item: item('x') }),
      file('e5', { item: series, season: 2, episode: 5, watch: watch({ progress: 0.5 }) }),
      file('e4', { item: series, season: 2, episode: 4, watch: watch({ progress: 0.1 }) }),
      file('loose', { watch: watch({ progress: 0.3 }) })
    ];
    const entries = continueEntries(files);
    expect(entries.map((e) => e.key)).toEqual(['bb', '1:loose']);
    expect(entries[0]).toMatchObject({ title: 'Breaking Bad', subtitle: 'S02E05', percent: 50, imageUrl: 'http://h/b?size=w1280', itemId: 'bb' });
    expect(entries[1]).toMatchObject({ title: 'parsed loose', itemId: null });
  });
});

describe('home helpers', () => {
  it('features an item in progress with a backdrop first', () => {
    const a = item('a', { backdropUrl: 'x', rating: 9 });
    const b = item('b', { backdropUrl: 'y', watch: { ...item('b').watch, inProgress: true } });
    expect(pickFeatured([a, b])?.id).toBe('b');
    expect(pickFeatured([item('c'), a])?.id).toBe('a');
    expect(pickFeatured([item('c')])?.id).toBe('c');
    expect(pickFeatured([])).toBeNull();
  });

  it('rotates daily among the best rated backdrops', () => {
    const list = [item('a', { backdropUrl: 'x', rating: 9 }), item('b', { backdropUrl: 'y', rating: 8 })];
    const day0 = pickFeatured(list, new Date(0))?.id;
    const day1 = pickFeatured(list, new Date(86_400_000))?.id;
    expect(new Set([day0, day1])).toEqual(new Set(['a', 'b']));
  });

  it('picks the most populated genres', () => {
    const rows = genreRows(
      [
        { name: 'Drama', count: 10 },
        { name: 'Action', count: 10 },
        { name: 'Western', count: 1 },
        { name: 'Comedy', count: 5 }
      ],
      2
    );
    expect(rows.map((g) => g.name)).toEqual(['Action', 'Drama']);
  });
});

describe('misc helpers', () => {
  it('replaces the size of an image URL', () => {
    expect(imageAtSize('http://h/api/v1/library/images/abc?size=w342', 'w92')).toBe('http://h/api/v1/library/images/abc?size=w92');
    expect(imageAtSize('/img/k', 'w500')).toBe('/img/k?size=w500');
    expect(imageAtSize(null, 'w92')).toBeNull();
  });

  it('describes a version from its file name', () => {
    expect(versionLabel({ fileName: 'Dune.2021.1080p.BluRay.x265.Castellano.mkv', sizeText: '4.2 GB' })).toBe('1080p · BluRay · x265 · Castellano · 4.2 GB');
    expect(versionLabel({ fileName: 'movie.mp4', sizeText: '1 GB' })).toBe('1 GB');
  });

  it('picks the episode file in progress', () => {
    const ep = { files: [file('a'), file('b', { watch: watch({ positionMs: 1000 }) })] } as ApiLibraryEpisodeDto;
    expect(episodeFile(ep)?.fileId).toBe('b');
    expect(episodeFile({ files: [] } as unknown as ApiLibraryEpisodeDto)).toBeNull();
  });

  it('computes the scan percentage', () => {
    expect(scanPercent({ running: true, channelsTotal: 4, channelsScanned: 1 } as never)).toBe(25);
    expect(scanPercent({ running: true, channelsTotal: 0, channelsScanned: 0 } as never)).toBe(0);
    expect(scanPercent(null)).toBe(100);
  });

  it('recognises the disabled library error', () => {
    expect(isLibraryDisabled(new ApiError('library_disabled', 'off', 503))).toBe(true);
    expect(isLibraryDisabled(new ApiError('not_found', 'x', 404))).toBe(false);
    expect(isLibraryDisabled(new Error('x'))).toBe(false);
  });

  it('parses the browse filters with sensible sort directions', () => {
    expect(parseBrowseFilters(new URLSearchParams(''), 'movie')).toMatchObject({ sortBy: 'added', desc: true, genre: null, decade: null });
    expect(parseBrowseFilters(new URLSearchParams('sort=title'), 'movie')).toMatchObject({ sortBy: 'title', desc: false });
    expect(parseBrowseFilters(new URLSearchParams('sort=title&dir=desc&decade=1990&genre=Drama'), 'all')).toMatchObject({
      desc: true,
      decade: 1990,
      genre: 'Drama'
    });
    expect(parseBrowseFilters(new URLSearchParams('decade=1995&sort=bogus'), 'all')).toMatchObject({ decade: null, sortBy: 'added' });
  });

  it('filters by decade and lists decades', () => {
    expect(matchesDecade({ year: 1994 }, 1990)).toBe(true);
    expect(matchesDecade({ year: 2000 }, 1990)).toBe(false);
    expect(matchesDecade({ year: null }, 1990)).toBe(false);
    expect(matchesDecade({ year: null }, null)).toBe(true);
    expect(decades(new Date(2026, 0, 1), 2000)).toEqual([2020, 2010, 2000]);
  });
});
