import { Button, cn, Dialog, DialogContent, Input, Sheet, SheetContent, useMediaQuery } from '@tfm/ui';
import { useMemo, useRef, useState, type ReactNode } from 'react';
import { isValidName } from '../core/paths';
import { useFileManager, type FileManagerController, type UseFileManagerOptions } from '../hooks/useFileManager';
import type { FileAction, FileEntry } from '../types';
import { createBuiltinActions } from './builtinActions';
import { FileManagerUiContext, useFileManagerUi, type FileManagerUi } from './context';
import { DetailsPanel } from './DetailsPanel';
import { FolderTree } from './FolderTree';
import { ItemsView } from './ItemsView';
import { ClipboardBanner, SelectionBar } from './SelectionBar';
import { defaultStrings, type FileManagerStrings } from './strings';
import { Toolbar } from './Toolbar';
import { UploadPanel } from './UploadPanel';

export interface FileManagerViewProps {
  /** Extra actions (download to server, share...). */
  actions?: FileAction[];
  /** Ids of built-in actions to hide (`fm:open`, `fm:download`, `fm:cut`, `fm:copy`, `fm:paste`, `fm:rename`, `fm:delete`, `fm:details`...). */
  hiddenActions?: string[];
  strings?: Partial<FileManagerStrings>;
  /** Locale of dates (default: the browser's). */
  locale?: string;
  /** Icon of the root in the breadcrumb and the tree. */
  rootIcon?: ReactNode;
  /** Offer the folder tree (desktop, lg+). Default true. */
  tree?: boolean;
  defaultShowTree?: boolean;
  defaultShowDetails?: boolean;
  /** Small thumbnails in the list/details views (the grid always shows them). Default true. */
  thumbnails?: boolean;
  /** With a mouse, open folders with a single click (Ctrl/Shift+click and the checkbox still select). Default true. */
  singleClickFolders?: boolean;
  /** Content above the toolbar (page header...). */
  header?: ReactNode;
  /** Extra toolbar content (right side). */
  toolbarExtra?: ReactNode;
  /** Extra content of the details panel for the selection. */
  renderDetails?(entries: FileEntry[]): ReactNode;
  className?: string;
}

function NewFolderDialog({ open, onOpenChange }: { open: boolean; onOpenChange(open: boolean): void }) {
  const { fm, strings } = useFileManagerUi();
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const valid = isValidName(name);
  const submit = async () => {
    if (!valid) return;
    setBusy(true);
    try {
      await fm.createFolder(name);
      onOpenChange(false);
      setName('');
    } catch {
      // reported through onError
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="sm"
        title={strings.newFolder}
        footer={
          <>
            <Button variant="secondary" onClick={() => onOpenChange(false)}>
              {strings.cancel}
            </Button>
            <Button onClick={() => void submit()} disabled={!valid} loading={busy}>
              {strings.create}
            </Button>
          </>
        }
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void submit();
          }}
        >
          <Input autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder={strings.newFolderName} aria-label={strings.newFolderName} />
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Default UI around a controller from {@link useFileManager}. */
export function FileManagerView({
  fm,
  actions,
  hiddenActions,
  strings: stringOverrides,
  locale,
  rootIcon,
  tree = true,
  defaultShowTree = false,
  defaultShowDetails = false,
  thumbnails = true,
  singleClickFolders = true,
  header,
  toolbarExtra,
  renderDetails,
  className
}: FileManagerViewProps & { fm: FileManagerController }) {
  const isDesktop = useMediaQuery('(min-width: 768px)');
  const isMobile = !isDesktop;
  const strings = useMemo(() => ({ ...defaultStrings, ...stringOverrides }), [stringOverrides]);
  const [showTree, setShowTree] = useState(defaultShowTree);
  const [showDetails, setShowDetails] = useState(defaultShowDetails);
  const [mobileDetails, setMobileDetails] = useState(false);
  const [newFolderOpen, setNewFolderOpen] = useState(false);
  const [dragging, setDragging] = useState<FileEntry[] | null>(null);
  const [dropTargetId, setDropTargetId] = useState<string | null>(null);
  const [fileInput, setFileInput] = useState<HTMLInputElement | null>(null);
  const [uploadTarget, setUploadTarget] = useState<string | undefined>(undefined);
  const lastClick = useRef<{ entry: FileEntry; opened: boolean; at: number } | null>(null);

  // Ctrl+Shift+N from the keyboard handler of the controller
  const [seenNewFolderToken, setSeenNewFolderToken] = useState(fm.newFolderToken);
  if (fm.newFolderToken !== seenNewFolderToken) {
    setSeenNewFolderToken(fm.newFolderToken);
    setNewFolderOpen(true);
  }

  const openDetails = () => (isMobile ? setMobileDetails(true) : setShowDetails(true));
  const requestUpload = (targetPath?: string) => {
    setUploadTarget(targetPath);
    fileInput?.click();
  };

  const builtins = createBuiltinActions(fm, strings, {
      requestNewFolder: () => setNewFolderOpen(true),
      requestUpload: () => requestUpload(),
      openDetails
    }).filter((a) => !hiddenActions?.includes(a.id));
  const allActions = [...builtins, ...(actions ?? []).map((a) => (a.group ? a : { ...a, group: 'b-app' }))];

  const ui: FileManagerUi = {
    fm,
    strings,
    actions: allActions,
    locale,
    isMobile,
    thumbnails,
    singleClickFolders,
    rememberClick: (entry, opened) => {
      lastClick.current = { entry, opened, at: Date.now() };
    },
    recentClick: (ms) => (lastClick.current && Date.now() - lastClick.current.at < ms ? lastClick.current : null),
    openDetails,
    requestNewFolder: () => setNewFolderOpen(true),
    requestUpload,
    dragging,
    setDragging,
    dropTargetId,
    setDropTargetId
  };

  const details = (
    <DetailsPanel onClose={isMobile ? () => setMobileDetails(false) : () => setShowDetails(false)} className="h-full">
      {renderDetails?.(fm.selection.entries)}
    </DetailsPanel>
  );

  return (
    <FileManagerUiContext.Provider value={ui}>
      <div className={cn('relative flex h-full min-h-0 flex-col overflow-hidden', className)}>
        {header}
        <Toolbar
          rootIcon={rootIcon}
          showTree={showTree}
          onToggleTree={tree ? () => setShowTree((v) => !v) : undefined}
          showDetails={showDetails}
          onToggleDetails={() => setShowDetails((v) => !v)}
          extra={toolbarExtra}
        />
        {!isMobile && <SelectionBar />}
        <ClipboardBanner />
        <div className="flex min-h-0 flex-1">
          {tree && showTree && !isMobile && <FolderTree rootIcon={rootIcon} className="hidden w-60 shrink-0 border-r border-border lg:block" />}
          <ItemsView />
          {showDetails && !isMobile && <div className="hidden w-80 shrink-0 border-l border-border md:flex md:flex-col">{details}</div>}
        </div>
        {isMobile && <SelectionBar />}
        <div className={cn('pointer-events-none absolute right-3 z-30 flex justify-end', isMobile ? 'bottom-20' : 'bottom-3')}>
          <UploadPanel className="pointer-events-auto" />
        </div>
        <NewFolderDialog open={newFolderOpen} onOpenChange={setNewFolderOpen} />
        {isMobile && (
          <Sheet open={mobileDetails} onOpenChange={setMobileDetails}>
            <SheetContent side="bottom" title={strings.details} hideHeader>
              {details}
            </SheetContent>
          </Sheet>
        )}
        <input
          ref={setFileInput}
          type="file"
          multiple
          hidden
          tabIndex={-1}
          aria-hidden
          onChange={(e) => {
            const files = Array.from(e.target.files ?? []);
            if (files.length) fm.uploads.add(files, uploadTarget);
            e.target.value = '';
          }}
        />
      </div>
    </FileManagerUiContext.Provider>
  );
}

export type FileManagerProps = UseFileManagerOptions & FileManagerViewProps;

/** Complete file manager: `useFileManager(options)` + the default UI. */
export function FileManager(props: FileManagerProps) {
  const fm = useFileManager(props);
  return <FileManagerView {...props} fm={fm} />;
}
