import { useQuery } from '@tanstack/react-query';
import { cn, Spinner } from '@tfm/ui';
import { ChevronRight, Folder, FolderOpen, HardDrive } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { fileManagerQueryKey } from '../hooks/useFileManager';
import { isSameOrDescendant } from '../core/paths';
import { useFileManagerUi } from './context';

const TREE_PAGE = 500;

interface NodeProps {
  path: string;
  name: string;
  depth: number;
  icon?: ReactNode;
  hasChildren?: boolean;
  expanded: Set<string>;
  collapsed: Set<string>;
  toggle(path: string, open: boolean): void;
}

function TreeNode({ path, name, depth, icon, hasChildren = true, expanded, collapsed, toggle }: NodeProps) {
  const ui = useFileManagerUi();
  const { fm } = ui;
  const [over, setOver] = useState(false);
  const onCurrentBranch = isSameOrDescendant(fm.path, path) && fm.path !== path;
  const open = (expanded.has(path) || onCurrentBranch || depth === 0) && !collapsed.has(path);
  const active = fm.path === path;
  const { data, isLoading } = useQuery({
    queryKey: [...fileManagerQueryKey(fm.provider.id), 'tree', path],
    queryFn: ({ signal }) => fm.provider.list(path, { page: 1, pageSize: TREE_PAGE, filter: { kind: 'folder' }, sort: { field: 'name', direction: 'asc' }, signal }),
    enabled: open && hasChildren,
    staleTime: 30_000
  });
  const children = data?.items.filter((e) => e.kind === 'folder') ?? [];
  const accepts = () =>
    !!ui.dragging?.length && !ui.dragging.some((d) => d.parentPath === path || (d.kind === 'folder' && isSameOrDescendant(path, d.path)));

  return (
    <li role="treeitem" aria-expanded={hasChildren ? open : undefined} aria-selected={active}>
      <div
        className={cn(
          'group flex h-8 items-center gap-1 rounded-lg pr-2 text-sm transition-colors',
          active ? 'bg-primary-soft text-fg' : 'text-fg-muted hover:bg-surface-2 hover:text-fg',
          over && 'ring-2 ring-accent-400'
        )}
        style={{ paddingLeft: depth * 12 + 4 }}
        onDragOver={(e) => {
          if (!accepts()) return;
          e.preventDefault();
          setOver(true);
        }}
        onDragLeave={() => setOver(false)}
        onDrop={(e) => {
          setOver(false);
          if (!accepts() || !ui.dragging) return;
          e.preventDefault();
          const list = ui.dragging;
          ui.setDragging(null);
          void fm.moveEntries(list, path, e.ctrlKey || e.altKey ? 'copy' : 'cut');
        }}
      >
        <button
          type="button"
          tabIndex={-1}
          aria-hidden
          className={cn('flex size-5 shrink-0 items-center justify-center rounded text-fg-subtle hover:text-fg', !hasChildren && 'invisible')}
          onClick={() => toggle(path, !open)}
        >
          {isLoading && open ? <Spinner size="sm" /> : <ChevronRight className={cn('size-3.5 transition-transform', open && 'rotate-90')} />}
        </button>
        <button type="button" className="flex min-w-0 flex-1 items-center gap-2 text-left" onClick={() => fm.navigate(path)}>
          <span className="flex shrink-0 text-accent-300 [&_svg]:size-4">{icon ?? (open ? <FolderOpen /> : <Folder />)}</span>
          <span className="truncate">{name}</span>
        </button>
      </div>
      {open && children.length > 0 && (
        <ul role="group">
          {children.map((c) => (
            <TreeNode
              key={c.id}
              path={c.path}
              name={c.name}
              depth={depth + 1}
              hasChildren={c.hasChildren ?? true}
              expanded={expanded}
              collapsed={collapsed}
              toggle={toggle}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

/** Lazy folder tree of the provider (children are listed when a node opens). */
export function FolderTree({ rootIcon, className }: { rootIcon?: ReactNode; className?: string }) {
  const { fm, strings } = useFileManagerUi();
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [collapsed, setCollapsed] = useState<Set<string>>(() => new Set());
  const toggle = (path: string, open: boolean) => {
    setExpanded((s) => {
      const n = new Set(s);
      if (open) n.add(path);
      else n.delete(path);
      return n;
    });
    setCollapsed((s) => {
      const n = new Set(s);
      if (open) n.delete(path);
      else n.add(path);
      return n;
    });
  };
  return (
    <nav aria-label={strings.folders} className={cn('min-h-0 overflow-y-auto p-2', className)}>
      <p className="px-2 pb-1.5 pt-1 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle">{strings.folders}</p>
      <ul role="tree">
        <TreeNode path="/" name={fm.provider.rootName} depth={0} icon={rootIcon ?? <HardDrive />} expanded={expanded} collapsed={collapsed} toggle={toggle} />
      </ul>
    </nav>
  );
}
