import { Button, cn, DropdownMenu, DropdownMenuContent, DropdownMenuTrigger, IconButton } from '@tfm/ui';
import { ClipboardPaste, MoreHorizontal, SquareCheckBig, X } from 'lucide-react';
import { formatBytes } from './format';
import { ActionMenuItems, useResolvedActions } from './ActionMenu';
import { useFileManagerUi } from './context';
import { fmt } from './strings';

/**
 * Bar with the actions of the selection: under the toolbar on desktop, at the
 * bottom (thumb reach) on mobile.
 */
export function SelectionBar() {
  const { fm, strings, isMobile } = useFileManagerUi();
  const selected = fm.selection.entries;
  const actions = useResolvedActions('selection', selected);
  if (selected.length === 0) return null;
  const inline = actions.slice(0, isMobile ? 4 : 5);
  const shownIds = inline.map((a) => a.action.id);
  const bytes = selected.reduce((sum, e) => sum + (e.kind === 'file' ? e.size ?? 0 : 0), 0);

  return (
    <div
      role="toolbar"
      aria-label={fmt(strings.selected, { count: selected.length })}
      className={cn(
        'flex shrink-0 items-center gap-1 animate-fade-in',
        isMobile
          ? 'glass-strong mx-2 mb-2 rounded-2xl border border-border px-2 py-1.5 shadow-elevated'
          : 'border-b border-border bg-primary-soft/40 px-2 py-1.5'
      )}
    >
      <IconButton
        label={strings.clearSelection}
        size="sm"
        onClick={() => {
          fm.selection.dispatch({ type: 'clear' });
          fm.selection.setSelectionMode(false);
        }}
      >
        <X />
      </IconButton>
      <div className="mr-1 flex min-w-0 flex-col leading-tight">
        <span className="truncate text-sm font-semibold text-fg">{fmt(strings.selected, { count: selected.length })}</span>
        {bytes > 0 && <span className="text-[11px] text-fg-subtle">{formatBytes(bytes)}</span>}
      </div>
      {!isMobile && (
        <Button variant="ghost" size="xs" icon={<SquareCheckBig />} onClick={() => fm.selection.dispatch({ type: 'selectAll' })}>
          {strings.selectAll}
        </Button>
      )}
      <div className={cn('flex min-w-0 flex-1 items-center gap-0.5', isMobile ? 'justify-end' : 'justify-end sm:justify-start')}>
        {inline.map(({ action, label, enabled }) =>
          isMobile ? (
            <IconButton
              key={action.id}
              label={label}
              size="md"
              disabled={!enabled}
              className={cn(action.destructive && 'text-danger hover:bg-danger-soft hover:text-danger')}
              onClick={() => void action.run(selected, fm.actionContext)}
            >
              {action.icon}
            </IconButton>
          ) : (
            <Button
              key={action.id}
              variant="ghost"
              size="xs"
              icon={action.icon}
              disabled={!enabled}
              className={cn(action.destructive && 'text-danger hover:bg-danger-soft hover:text-danger')}
              onClick={() => void action.run(selected, fm.actionContext)}
            >
              <span className="hidden xl:inline">{label}</span>
            </Button>
          )
        )}
        {actions.length > inline.length && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <IconButton label={strings.more} size={isMobile ? 'md' : 'sm'}>
                <MoreHorizontal />
              </IconButton>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" side={isMobile ? 'top' : 'bottom'} className="min-w-52">
              <ActionMenuItems menu="dropdown" entries={selected} placement="selection" exclude={shownIds} />
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </div>
  );
}

/** "3 cut — paste them in another folder" banner while the clipboard holds entries of this provider. */
export function ClipboardBanner() {
  const { fm, strings } = useFileManagerUi();
  const clip = fm.clipboard.state;
  if (!clip || clip.providerId !== fm.provider.id || fm.selection.entries.length > 0) return null;
  const text = fmt(clip.mode === 'cut' ? strings.clipboardCut : strings.clipboardCopied, { count: clip.entries.length });
  return (
    <div className="flex shrink-0 items-center gap-2 border-b border-border bg-surface-2/50 px-3 py-1.5 text-xs text-fg-muted animate-fade-in">
      <ClipboardPaste className="size-4 text-accent-300" />
      <span className="min-w-0 flex-1 truncate">{text}</span>
      <Button size="xs" variant="soft" disabled={!!fm.clipboard.blockReason} onClick={() => void fm.clipboard.paste()}>
        {strings.paste}
      </Button>
      <IconButton label={strings.cancel} size="xs" onClick={fm.clipboard.clear}>
        <X />
      </IconButton>
    </div>
  );
}
