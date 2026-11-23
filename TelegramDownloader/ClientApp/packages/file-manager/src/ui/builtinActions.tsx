import { ClipboardPaste, Copy, Download, FolderOpen, FolderPlus, Info, PencilLine, RefreshCw, Scissors, SquareCheckBig, Trash2, Upload } from 'lucide-react';
import type { FileManagerController } from '../hooks/useFileManager';
import type { FileAction } from '../types';
import type { FileManagerStrings } from './strings';

export interface BuiltinActionHooks {
  requestNewFolder(): void;
  requestUpload(): void;
  openDetails(): void;
}

/** The standard actions (open, cut/copy/paste, rename, delete...) as registry entries. */
export function createBuiltinActions(fm: FileManagerController, s: FileManagerStrings, hooks: BuiltinActionHooks): FileAction[] {
  const caps = fm.provider.capabilities;
  const p = fm.provider;
  const list: FileAction[] = [
    {
      id: 'fm:open',
      label: s.open,
      icon: <FolderOpen />,
      selection: 'single',
      group: 'a-open',
      order: 0,
      shortcut: 'enter',
      placement: ['context'],
      run: ([e]) => fm.open(e)
    },
    {
      id: 'fm:download',
      label: s.download,
      icon: <Download />,
      group: 'a-open',
      order: 10,
      when: () => caps.download && !!p.download,
      run: (entries) => fm.download(entries)
    },
    {
      id: 'fm:cut',
      label: s.cut,
      icon: <Scissors />,
      group: 'c-clipboard',
      order: 0,
      shortcut: 'mod+x',
      when: () => caps.move && !!p.move,
      run: (entries) => fm.clipboard.cut(entries)
    },
    {
      id: 'fm:copy',
      label: s.copy,
      icon: <Copy />,
      group: 'c-clipboard',
      order: 1,
      shortcut: 'mod+c',
      when: () => caps.copy && !!p.copy,
      run: (entries) => fm.clipboard.copy(entries)
    },
    {
      id: 'fm:paste-into',
      label: s.paste,
      icon: <ClipboardPaste />,
      selection: 'single',
      group: 'c-clipboard',
      order: 2,
      placement: ['context'],
      when: ([e]) => e.kind === 'folder' && !!fm.clipboard.state && fm.clipboard.state.providerId === p.id,
      run: ([e]) => fm.clipboard.paste(e.path)
    },
    {
      id: 'fm:paste',
      label: s.paste,
      icon: <ClipboardPaste />,
      selection: 'none',
      group: 'c-clipboard',
      order: 2,
      shortcut: 'mod+v',
      placement: ['context'],
      when: () => !!fm.clipboard.state && fm.clipboard.state.providerId === p.id,
      isEnabled: () => !fm.clipboard.blockReason,
      run: () => fm.clipboard.paste()
    },
    {
      id: 'fm:rename',
      label: s.rename,
      icon: <PencilLine />,
      selection: 'single',
      group: 'x-edit',
      order: 0,
      shortcut: 'f2',
      placement: ['context', 'details'],
      when: () => caps.rename && !!p.rename,
      run: ([e]) => fm.rename.start(e)
    },
    {
      id: 'fm:details',
      label: s.details,
      icon: <Info />,
      selection: 'any',
      group: 'x-edit',
      order: 5,
      placement: ['context'],
      run: () => hooks.openDetails()
    },
    {
      id: 'fm:delete',
      label: s.delete,
      icon: <Trash2 />,
      group: 'z-danger',
      destructive: true,
      shortcut: 'delete',
      when: () => caps.delete && !!p.delete,
      run: (entries) => fm.deleteEntries(entries)
    },
    // Background (current folder) actions
    {
      id: 'fm:new-folder',
      label: s.newFolder,
      icon: <FolderPlus />,
      selection: 'none',
      group: 'a-open',
      order: 0,
      placement: ['context'],
      shortcut: 'mod+shift+n',
      when: () => caps.createFolder && !!p.createFolder,
      run: () => hooks.requestNewFolder()
    },
    {
      id: 'fm:upload',
      label: s.upload,
      icon: <Upload />,
      selection: 'none',
      group: 'a-open',
      order: 1,
      placement: ['context'],
      when: () => caps.upload && !!p.upload,
      run: () => hooks.requestUpload()
    },
    {
      id: 'fm:select-all',
      label: s.selectAll,
      icon: <SquareCheckBig />,
      selection: 'none',
      group: 'x-edit',
      order: 0,
      shortcut: 'mod+a',
      placement: ['context'],
      when: (_e, ctx) => ctx.entries.length > 0,
      run: () => fm.selection.dispatch({ type: 'selectAll' })
    },
    {
      id: 'fm:refresh',
      label: s.refresh,
      icon: <RefreshCw />,
      selection: 'none',
      group: 'x-edit',
      order: 1,
      shortcut: 'f5',
      placement: ['context'],
      run: () => fm.refresh()
    }
  ];
  return list;
}
