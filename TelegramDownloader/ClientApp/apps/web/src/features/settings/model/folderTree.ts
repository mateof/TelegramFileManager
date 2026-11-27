import { normalizeFolderPath } from './configForm';

/** A folder of a channel index: `path` is normalised (`/A/B/`), the root is `/`. */
export interface FolderNode {
  name: string;
  path: string;
  children: FolderNode[];
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

function sortTree(node: FolderNode) {
  node.children.sort((a, b) => collator.compare(a.name, b.name));
  for (const child of node.children) sortTree(child);
}

/**
 * The folder paths of a channel as a tree. Parents that are not in the list on their own
 * (only `/A/B/` is, not `/A/`) are added, so every folder hangs from the root.
 */
export function buildFolderTree(paths: readonly string[]): FolderNode {
  const root: FolderNode = { name: '', path: '/', children: [] };
  const index = new Map<string, FolderNode>([['/', root]]);
  for (const raw of paths) {
    let parent = root;
    let path = '/';
    for (const name of normalizeFolderPath(raw).split('/').filter(Boolean)) {
      path += `${name}/`;
      let node = index.get(path);
      if (!node) {
        node = { name, path, children: [] };
        index.set(path, node);
        parent.children.push(node);
      }
      parent = node;
    }
  }
  sortTree(root);
  return root;
}

/**
 * The folders whose name contains `query` (case-insensitive), with their parents so they keep
 * their place in the tree, and with all their own subfolders, to pick one below a match.
 * `open` are the folders to show expanded (those with a match below), `matches` how many matched.
 */
export function filterFolderTree(root: FolderNode, query: string): { root: FolderNode; matches: number; open: Set<string> } {
  const q = query.trim().toLowerCase();
  const open = new Set<string>();
  if (!q) return { root, matches: 0, open };
  let matches = 0;
  const visit = (node: FolderNode): FolderNode | null => {
    const children = node.children.map(visit).filter((c): c is FolderNode => c !== null);
    const hit = node.path !== '/' && node.name.toLowerCase().includes(q);
    if (hit) matches++;
    if (children.length > 0) open.add(node.path);
    if (hit) return node;
    return children.length > 0 ? { ...node, children } : null;
  };
  return { root: visit(root) ?? { ...root, children: [] }, matches, open };
}

/** `/A/B/C/` → `['/A/', '/A/B/']`: the folders to open so that the given one is visible. */
export function ancestorsOf(path: string): string[] {
  const names = normalizeFolderPath(path).split('/').filter(Boolean);
  return names.slice(0, -1).map((_, i) => `/${names.slice(0, i + 1).join('/')}/`);
}
