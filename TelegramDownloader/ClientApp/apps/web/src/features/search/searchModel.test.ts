import type { ApiFileDto, ApiGlobalSearchResultDto } from '@tfm/api-client';
import { describe, expect, it } from 'vitest';
import { containingFolderRoute, mediaKind, toAudioTrack, toPlaylistCandidate, toViewerItem, type MediaFileDto } from './fileMedia';
import { groupSearchResults, highlightParts, normaliseQuery, parseFilter, parseScope } from './searchModel';

function file(id: string, name: string, extra: Partial<MediaFileDto> = {}): MediaFileDto {
  const dot = name.lastIndexOf('.');
  return {
    id,
    name,
    path: '/music/',
    parentId: null,
    isFile: true,
    hasChildren: false,
    size: 1000,
    sizeText: '1000 B',
    type: dot > 0 ? name.slice(dot) : '',
    category: 'Audio',
    dateCreated: '2026-01-01T00:00:00Z',
    dateModified: '2026-01-01T00:00:00Z',
    messageId: 1,
    isSplit: false,
    md5Hash: null,
    xxHash: null,
    streamUrl: null,
    downloadUrl: null,
    ...extra
  };
}

describe('groupSearchResults', () => {
  const result: ApiGlobalSearchResultDto = {
    channels: [
      { channelId: '10', channelName: 'Music', files: [file('b', 'Track 10.mp3'), file('a', 'Track 2.mp3'), file('a', 'Track 2.mp3')] },
      { channelId: '20', channelName: '', files: [file('v', 'Clip.mp4', { category: 'Video' })] },
      { channelId: '30', channelName: 'Empty', files: [] }
    ],
    local: [file('music/x.flac', 'x.flac'), file('folder', 'folder', { isFile: false, category: 'Folder' })],
    truncated: true
  };

  it('keeps one group per channel with matches, then the local storage', () => {
    const s = groupSearchResults(result, 'Local files');
    expect(s.groups.map((g) => g.key)).toEqual(['channel:10', 'channel:20', 'local']);
    expect(s.channelCount).toBe(2);
    expect(s.truncated).toBe(true);
    expect(s.groups[2]).toMatchObject({ kind: 'local', channelId: null, title: 'Local files' });
  });

  it('dedupes, drops folders and sorts naturally by name', () => {
    const s = groupSearchResults(result, 'Local files');
    expect(s.groups[0].files.map((f) => f.name)).toEqual(['Track 2.mp3', 'Track 10.mp3']);
    expect(s.groups[2].files).toHaveLength(1);
    expect(s.total).toBe(4);
  });

  it('falls back to the channel id as title and counts categories', () => {
    const s = groupSearchResults(result, 'Local files');
    expect(s.groups[1].title).toBe('20');
    expect(s.groups[0].categories).toEqual({ Audio: 2 });
  });

  it('handles an empty answer', () => {
    expect(groupSearchResults(undefined, 'x')).toEqual({ groups: [], total: 0, channelCount: 0, truncated: false });
  });
});

describe('query helpers', () => {
  it('normalises the query and rejects too short ones', () => {
    expect(normaliseQuery('  ab ')).toBe('ab');
    expect(normaliseQuery('a')).toBeNull();
    expect(normaliseQuery(null)).toBeNull();
  });

  it('parses filter and scope with safe defaults', () => {
    expect(parseFilter('video')).toBe('video');
    expect(parseFilter('nope')).toBe('all');
    expect(parseScope('local')).toBe('local');
    expect(parseScope(null)).toBe('all');
  });

  it('splits text around case-insensitive matches', () => {
    expect(highlightParts('Flashes - FLASH.mp3', 'flash')).toEqual([
      { text: 'Flash', match: true },
      { text: 'es - ', match: false },
      { text: 'FLASH', match: true },
      { text: '.mp3', match: false }
    ]);
    expect(highlightParts('abc', '')).toEqual([{ text: 'abc', match: false }]);
  });
});

describe('file mapping', () => {
  it('detects the viewer kind', () => {
    expect(mediaKind(file('1', 'a.mp3'))).toBe('audio');
    expect(mediaKind(file('1', 'a.pdf', { category: 'Document' }))).toBe('pdf');
    expect(mediaKind(file('1', 'a.srt', { category: 'Document' }))).toBe('text');
    expect(mediaKind(file('1', 'a.zip', { category: 'Archive' }))).toBe('other');
  });

  it('builds the containing folder route', () => {
    expect(containingFolderRoute('123', '/Music/Rock & Roll/')).toBe('/channels/123/Music/Rock%20%26%20Roll');
    expect(containingFolderRoute('123', '/')).toBe('/channels/123');
    expect(containingFolderRoute(null, '/a/b/')).toBe('/local/a/b');
  });

  it('maps channel and local files to tracks, viewer items and playlist candidates', () => {
    const f = file('f1', '01 - Song.mp3', { contentUrl: 'http://h/c', playbackUrl: 'http://h/p', contentDownloadUrl: 'http://h/d' });
    expect(toAudioTrack(f, '10', 'Music')).toMatchObject({ id: '10:f1', title: '01 - Song', url: 'http://h/p', channelId: '10', fileId: 'f1', source: 'Music' });
    expect(toAudioTrack(file('x', 'x.mp3'), '10')).toBeNull();
    expect(toViewerItem(f, '10')).toMatchObject({ url: 'http://h/c', downloadUrl: 'http://h/d', kind: 'audio' });
    const local = file('music/a.mp3', 'a.mp3', { streamUrl: 'http://h/local/music/a.mp3' });
    expect(toAudioTrack(local, null)).toMatchObject({ id: 'music/a.mp3', localPath: 'music/a.mp3', channelId: undefined });
    expect(toPlaylistCandidate(local as ApiFileDto, null)).toEqual({
      channelId: 'local',
      fileId: 'music/a.mp3',
      fileName: 'a.mp3',
      fileSize: 1000,
      filePath: 'music/a.mp3',
      isLocalFile: true
    });
  });
});
