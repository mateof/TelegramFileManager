import { ContextMenuItem, ContextMenuSeparator, DropdownMenuItem, DropdownMenuSeparator } from '@tfm/ui';
import { Fragment, useState } from 'react';
import { groupActions, resolveActions, type ResolvedAction } from '../core/actions';
import { formatShortcut, isMacPlatform } from '../core/keyboard';
import type { ActionPlacement, FileEntry } from '../types';
import { useFileManagerUi } from './context';

export interface ActionMenuItemsProps {
  /** Radix flavour of the surrounding menu. */
  menu: 'context' | 'dropdown';
  entries: FileEntry[];
  placement?: ActionPlacement;
  /** Leave out these action ids (already shown as buttons). */
  exclude?: readonly string[];
}

export function useResolvedActions(placement: ActionPlacement, entries: FileEntry[], exclude?: readonly string[]): ResolvedAction[] {
  const { actions, fm } = useFileManagerUi();
  return resolveActions(actions, placement, entries, fm.actionContext).filter((r) => !exclude?.includes(r.action.id));
}

/** Menu items of the actions that apply to `entries` (empty = current folder). */
export function ActionMenuItems({ menu, entries, placement = 'context', exclude }: ActionMenuItemsProps) {
  const { fm } = useFileManagerUi();
  const [isMac] = useState(isMacPlatform);
  const groups = groupActions(useResolvedActions(placement, entries, exclude));
  const Item = menu === 'context' ? ContextMenuItem : DropdownMenuItem;
  const Separator = menu === 'context' ? ContextMenuSeparator : DropdownMenuSeparator;
  return (
    <>
      {groups.map((group, gi) => (
        <Fragment key={gi}>
          {gi > 0 && <Separator />}
          {group.map(({ action, label, enabled }) => (
            <Item
              key={action.id}
              icon={action.icon}
              destructive={action.destructive}
              disabled={!enabled}
              shortcut={action.shortcut ? formatShortcut(action.shortcut, isMac) : undefined}
              onSelect={() => void action.run(entries, fm.actionContext)}
            >
              {label}
            </Item>
          ))}
        </Fragment>
      ))}
    </>
  );
}
