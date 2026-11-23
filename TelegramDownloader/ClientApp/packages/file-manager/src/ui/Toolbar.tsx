import {
  Breadcrumb,
  cn,
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  IconButton,
  Input,
  SegmentedControl,
  Tooltip
} from '@tfm/ui';
import {
  ArrowDownUp,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  FolderPlus,
  FolderTree,
  HardDrive,
  LayoutGrid,
  List,
  ListFilter,
  MoreHorizontal,
  PanelRight,
  RefreshCw,
  Rows3,
  Search,
  Upload,
  X
} from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { isSameOrDescendant } from '../core/paths';
import type { MediaKind, SortField, ViewMode } from '../types';
import { useResolvedActions } from './ActionMenu';
import { useFileManagerUi } from './context';

const DEFAULT_KINDS: MediaKind[] = ['audio', 'video', 'image', 'document', 'archive'];

function useKindLabels() {
  const { strings: s } = useFileManagerUi();
  return {
    audio: s.kindAudio,
    video: s.kindVideo,
    image: s.kindImage,
    document: s.kindDocument,
    archive: s.kindArchive,
    text: s.kindText,
    other: s.kindOther
  } satisfies Record<MediaKind, string>;
}

/** Back / forward / up buttons. */
function NavButtons({ compact }: { compact?: boolean }) {
  const { fm, strings } = useFileManagerUi();
  return (
    <div className="flex items-center">
      {!compact && (
        <>
          <IconButton label={strings.back} size="sm" disabled={!fm.canBack} onClick={fm.back}>
            <ArrowLeft />
          </IconButton>
          <IconButton label={strings.forward} size="sm" disabled={!fm.canForward} onClick={fm.forward}>
            <ArrowRight />
          </IconButton>
        </>
      )}
      <IconButton label={strings.up} size="sm" disabled={!fm.canUp} onClick={fm.up}>
        {compact ? <ArrowLeft /> : <ArrowUp />}
      </IconButton>
    </div>
  );
}

/** Breadcrumb whose hops are also drop targets (move entries up the tree). */
export function PathBar({ rootIcon, className }: { rootIcon?: ReactNode; className?: string }) {
  const ui = useFileManagerUi();
  const { fm, isMobile } = ui;
  const [over, setOver] = useState<string | null>(null);
  const items = fm.breadcrumbs.map((b, i) => ({
    label: b.name || '/',
    icon: i === 0 ? rootIcon ?? <HardDrive /> : undefined,
    render: (children: ReactNode, cls: string) => {
      const accepts = () =>
        !!ui.dragging?.length &&
        !ui.dragging.some((d) => d.parentPath === b.path || (d.kind === 'folder' && isSameOrDescendant(b.path, d.path)));
      return (
        <button
          type="button"
          className={cn(cls, over === b.path && 'bg-primary-soft text-fg ring-2 ring-accent-400')}
          onClick={() => fm.navigate(b.path)}
          onDragOver={(e) => {
            if (!accepts()) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = e.ctrlKey || e.altKey ? 'copy' : 'move';
            setOver(b.path);
          }}
          onDragLeave={() => setOver(null)}
          onDrop={(e) => {
            setOver(null);
            if (!accepts() || !ui.dragging) return;
            e.preventDefault();
            const list = ui.dragging;
            ui.setDragging(null);
            void fm.moveEntries(list, b.path, e.ctrlKey || e.altKey ? 'copy' : 'cut');
          }}
        >
          {children}
        </button>
      );
    }
  }));
  return <Breadcrumb items={items} maxItems={isMobile ? 3 : 6} className={cn('min-w-0 flex-1', className)} />;
}

function SearchBox({ autoFocus, onClose }: { autoFocus?: boolean; onClose?: () => void }) {
  const { fm, strings } = useFileManagerUi();
  const ref = useRef<HTMLInputElement>(null);
  const token = fm.focusSearchToken;
  useEffect(() => {
    if (token > 0 || autoFocus) ref.current?.focus();
  }, [token, autoFocus]);
  return (
    <Input
      ref={ref}
      inputSize="sm"
      type="search"
      value={fm.search}
      placeholder={strings.searchPlaceholder}
      aria-label={strings.search}
      onChange={(e) => fm.setSearch(e.target.value)}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          fm.setSearch('');
          onClose?.();
        }
      }}
      leading={<Search />}
      trailing={
        fm.search || onClose ? (
          <IconButton
            label={strings.clearSearch}
            size="xs"
            onClick={() => {
              fm.setSearch('');
              onClose?.();
            }}
          >
            <X />
          </IconButton>
        ) : undefined
      }
      className="w-full"
    />
  );
}

function FilterMenuItems() {
  const { fm, strings } = useFileManagerUi();
  const labels = useKindLabels();
  const kinds = fm.provider.capabilities.filterKinds ?? DEFAULT_KINDS;
  const selected = new Set(fm.filter.mediaKinds ?? []);
  return (
    <>
      <DropdownMenuLabel>{strings.filter}</DropdownMenuLabel>
      <DropdownMenuCheckboxItem checked={selected.size === 0} onCheckedChange={() => fm.setFilter({ ...fm.filter, mediaKinds: undefined })}>
        {strings.filterAll}
      </DropdownMenuCheckboxItem>
      {kinds.map((k) => (
        <DropdownMenuCheckboxItem
          key={k}
          checked={selected.has(k)}
          onSelect={(e) => e.preventDefault()}
          onCheckedChange={(on) => {
            const next = new Set(selected);
            if (on) next.add(k);
            else next.delete(k);
            fm.setFilter({ ...fm.filter, mediaKinds: next.size ? [...next] : undefined });
          }}
        >
          {labels[k]}
        </DropdownMenuCheckboxItem>
      ))}
    </>
  );
}

function SortMenuItems() {
  const { fm, strings } = useFileManagerUi();
  const fields: [SortField, string][] = [
    ['name', strings.sortName],
    ['modifiedAt', strings.sortModified],
    ['size', strings.sortSize],
    ['type', strings.sortType]
  ];
  return (
    <>
      <DropdownMenuLabel>{strings.sortBy}</DropdownMenuLabel>
      <DropdownMenuRadioGroup value={fm.sort.field} onValueChange={(v) => fm.setSort({ ...fm.sort, field: v as SortField })}>
        {fields.map(([f, label]) => (
          <DropdownMenuRadioItem key={f} value={f}>
            {label}
          </DropdownMenuRadioItem>
        ))}
      </DropdownMenuRadioGroup>
      <DropdownMenuSeparator />
      <DropdownMenuRadioGroup value={fm.sort.direction} onValueChange={(v) => fm.setSort({ ...fm.sort, direction: v as 'asc' | 'desc' })}>
        <DropdownMenuRadioItem value="asc">{strings.ascending}</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="desc">{strings.descending}</DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>
    </>
  );
}

function ViewMenuItems() {
  const { fm, strings } = useFileManagerUi();
  return (
    <>
      <DropdownMenuLabel>{strings.viewMode}</DropdownMenuLabel>
      <DropdownMenuRadioGroup value={fm.viewMode} onValueChange={(v) => fm.setViewMode(v as ViewMode)}>
        <DropdownMenuRadioItem value="details">{strings.viewDetails}</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="list">{strings.viewList}</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="grid">{strings.viewGrid}</DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>
    </>
  );
}

/** App actions that work on the current folder (`selection: 'none'`, placement toolbar). */
function FolderActionButtons() {
  const { fm } = useFileManagerUi();
  const actions = useResolvedActions('toolbar', []);
  return (
    <>
      {actions.map(({ action, label, enabled }) => (
        <Tooltip key={action.id} content={label}>
          <IconButton label={label} size="sm" disabled={!enabled} onClick={() => void action.run([], fm.actionContext)}>
            {action.icon}
          </IconButton>
        </Tooltip>
      ))}
    </>
  );
}

export interface ToolbarProps {
  rootIcon?: ReactNode;
  showTree?: boolean;
  onToggleTree?: () => void;
  showDetails: boolean;
  onToggleDetails: () => void;
  /** Extra content at the end (app buttons). */
  extra?: ReactNode;
}

/** Top bar: navigation, breadcrumb, search, filter/sort/view, new folder, upload, refresh. */
export function Toolbar({ rootIcon, showTree, onToggleTree, showDetails, onToggleDetails, extra }: ToolbarProps) {
  const ui = useFileManagerUi();
  const { fm, strings, isMobile } = ui;
  const caps = fm.provider.capabilities;
  const [searchOpen, setSearchOpen] = useState(false);
  const filtered = !!fm.filter.mediaKinds?.length;
  const canCreate = caps.createFolder && !!fm.provider.createFolder;
  const canUpload = caps.upload && !!fm.provider.upload;
  const folderActions = useResolvedActions('toolbar', []);

  if (isMobile) {
    const searching = searchOpen || !!fm.search;
    return (
      <div className="flex h-12 shrink-0 items-center gap-1 border-b border-border px-1.5">
        {searching ? (
          <SearchBox autoFocus onClose={() => setSearchOpen(false)} />
        ) : (
          <>
            <NavButtons compact />
            <PathBar rootIcon={rootIcon} />
            <IconButton label={strings.search} size="sm" onClick={() => setSearchOpen(true)}>
              <Search />
            </IconButton>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <IconButton label={strings.more} size="sm" className={cn(filtered && 'text-accent-300')}>
                  <MoreHorizontal />
                </IconButton>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="max-h-[70dvh] min-w-56 overflow-y-auto">
                {canCreate && (
                  <DropdownMenuItem icon={<FolderPlus />} onSelect={ui.requestNewFolder}>
                    {strings.newFolder}
                  </DropdownMenuItem>
                )}
                {canUpload && (
                  <DropdownMenuItem icon={<Upload />} onSelect={() => ui.requestUpload()}>
                    {strings.upload}
                  </DropdownMenuItem>
                )}
                {folderActions.map(({ action, label, enabled }) => (
                  <DropdownMenuItem key={action.id} icon={action.icon} disabled={!enabled} onSelect={() => void action.run([], fm.actionContext)}>
                    {label}
                  </DropdownMenuItem>
                ))}
                <DropdownMenuItem icon={<RefreshCw />} onSelect={fm.refresh}>
                  {strings.refresh}
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <ViewMenuItems />
                <DropdownMenuSeparator />
                <SortMenuItems />
                <DropdownMenuSeparator />
                <FilterMenuItems />
              </DropdownMenuContent>
            </DropdownMenu>
          </>
        )}
      </div>
    );
  }

  return (
    <div className="flex shrink-0 flex-wrap items-center gap-x-2 gap-y-1.5 border-b border-border px-2 py-2">
      <NavButtons />
      <PathBar rootIcon={rootIcon} className="min-w-40" />
      <div className="w-full max-w-64 min-w-40 flex-1 sm:flex-none">
        <SearchBox />
      </div>
      <div className="flex items-center gap-0.5">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <IconButton label={strings.filter} size="sm" className={cn(filtered && 'bg-primary-soft text-accent-300')}>
              <ListFilter />
            </IconButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="min-w-48">
            <FilterMenuItems />
          </DropdownMenuContent>
        </DropdownMenu>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <IconButton label={strings.sortBy} size="sm">
              <ArrowDownUp />
            </IconButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="min-w-48">
            <SortMenuItems />
          </DropdownMenuContent>
        </DropdownMenu>
        <SegmentedControl
          size="sm"
          aria-label={strings.viewMode}
          value={fm.viewMode}
          onValueChange={fm.setViewMode}
          options={[
            { value: 'details', label: <span className="sr-only">{strings.viewDetails}</span>, icon: <Rows3 /> },
            { value: 'list', label: <span className="sr-only">{strings.viewList}</span>, icon: <List /> },
            { value: 'grid', label: <span className="sr-only">{strings.viewGrid}</span>, icon: <LayoutGrid /> }
          ]}
        />
      </div>
      <div className="ml-auto flex items-center gap-0.5">
        <FolderActionButtons />
        {canCreate && (
          <Tooltip content={strings.newFolder}>
            <IconButton label={strings.newFolder} size="sm" onClick={ui.requestNewFolder}>
              <FolderPlus />
            </IconButton>
          </Tooltip>
        )}
        {canUpload && (
          <Tooltip content={strings.upload}>
            <IconButton label={strings.upload} size="sm" onClick={() => ui.requestUpload()}>
              <Upload />
            </IconButton>
          </Tooltip>
        )}
        <Tooltip content={strings.refresh} shortcut="F5">
          <IconButton label={strings.refresh} size="sm" onClick={fm.refresh}>
            <RefreshCw className={cn(fm.isFetching && !fm.isLoading && 'animate-spin')} />
          </IconButton>
        </Tooltip>
        {onToggleTree && (
          <Tooltip content={strings.toggleTree}>
            <IconButton label={strings.toggleTree} size="sm" aria-pressed={showTree} onClick={onToggleTree} className={cn('hidden lg:inline-flex', showTree && 'text-accent-300')}>
              <FolderTree />
            </IconButton>
          </Tooltip>
        )}
        <Tooltip content={strings.toggleDetails}>
          <IconButton label={strings.toggleDetails} size="sm" aria-pressed={showDetails} onClick={onToggleDetails} className={cn(showDetails && 'text-accent-300')}>
            <PanelRight />
          </IconButton>
        </Tooltip>
        {extra}
      </div>
    </div>
  );
}
