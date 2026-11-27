import { describe, expect, it } from 'vitest';
import { ancestorsOf, buildFolderTree, filterFolderTree, type FolderNode } from './folderTree';

const names = (n: FolderNode): unknown => (n.children.length ? { [n.name || '/']: n.children.map(names) } : n.name);

describe('buildFolderTree', () => {
  it('nests the paths, adds missing parents and sorts naturally', () => {
    const root = buildFolderTree(['/Series/Show 10/', '/Series/Show 2/', '/', 'Movies', '/Games/Mame/cfg/']);
    expect(names(root)).toEqual({ '/': [{ Games: [{ Mame: ['cfg'] }] }, 'Movies', { Series: ['Show 2', 'Show 10'] }] });
    expect(root.children[0].children[0].path).toBe('/Games/Mame/');
  });

  it('does not repeat a folder listed twice or with another spelling', () => {
    const root = buildFolderTree(['/A/', 'A', '/A/B/', 'A\\B']);
    expect(names(root)).toEqual({ '/': [{ A: ['B'] }] });
  });
});

describe('filterFolderTree', () => {
  const root = buildFolderTree(['/Games/Mame/cfg/', '/Games/Snes/', '/Series/Mame Show/', '/Movies/']);

  it('keeps the matches with their subfolders and their parents', () => {
    const { root: filtered, matches, open } = filterFolderTree(root, 'mame');
    expect(matches).toBe(2);
    expect(names(filtered)).toEqual({ '/': [{ Games: [{ Mame: ['cfg'] }] }, { Series: ['Mame Show'] }] });
    expect([...open].sort()).toEqual(['/', '/Games/', '/Series/']);
  });

  it('opens a match that has another match below', () => {
    const { matches, open } = filterFolderTree(buildFolderTree(['/Mame/Mame 2/', '/Mame/Other/']), 'mame');
    expect(matches).toBe(2);
    expect(open.has('/Mame/')).toBe(true);
  });

  it('returns the whole tree for an empty query and an empty root when nothing matches', () => {
    expect(filterFolderTree(root, '  ').root).toBe(root);
    const none = filterFolderTree(root, 'zzz');
    expect(none.matches).toBe(0);
    expect(none.root.children).toEqual([]);
  });
});

describe('ancestorsOf', () => {
  it('lists the parents of a folder', () => {
    expect(ancestorsOf('/A/B/C/')).toEqual(['/A/', '/A/B/']);
    expect(ancestorsOf('/A/')).toEqual([]);
    expect(ancestorsOf('/')).toEqual([]);
  });
});
