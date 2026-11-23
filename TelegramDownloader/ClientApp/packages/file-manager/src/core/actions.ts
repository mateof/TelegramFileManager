import type { ActionPlacement, ActionSelection, FileAction, FileActionContext, FileEntry } from '../types';

/** Does a selection of `count` entries satisfy the action's `selection` rule? */
export function selectionMatches(rule: ActionSelection | undefined, count: number): boolean {
  switch (rule ?? 'any') {
    case 'none':
      return count === 0;
    case 'single':
      return count === 1;
    case 'multiple':
      return count >= 2;
    case 'any':
      return count >= 1;
  }
}

function placementsOf(action: FileAction): ActionPlacement[] {
  if (action.placement) return action.placement;
  return action.selection === 'none' ? ['toolbar', 'context'] : ['context', 'details', 'selection'];
}

export interface ResolvedAction {
  action: FileAction;
  label: string;
  enabled: boolean;
}

/**
 * The actions to show in `placement` for `entries` (empty = the current
 * folder), sorted by group then order. Hidden ones (`when` false, wrong
 * selection size) are left out.
 */
export function resolveActions(
  actions: readonly FileAction[] | undefined,
  placement: ActionPlacement,
  entries: FileEntry[],
  context: FileActionContext
): ResolvedAction[] {
  if (!actions?.length) return [];
  return actions
    .filter((a) => placementsOf(a).includes(placement))
    .filter((a) => selectionMatches(a.selection, entries.length))
    .filter((a) => !a.when || a.when(entries, context))
    .map((action, index) => ({ action, index }))
    .sort((x, y) => {
      const gx = x.action.group ?? '';
      const gy = y.action.group ?? '';
      if (gx !== gy) return gx < gy ? -1 : 1;
      return (x.action.order ?? 100) - (y.action.order ?? 100) || x.index - y.index;
    })
    .map(({ action }) => ({
      action,
      label: typeof action.label === 'function' ? action.label(entries) : action.label,
      enabled: !action.isEnabled || action.isEnabled(entries, context)
    }));
}

/** Groups resolved actions by `group`, keeping their order (for separators). */
export function groupActions(resolved: ResolvedAction[]): ResolvedAction[][] {
  const groups: ResolvedAction[][] = [];
  let last: string | undefined | null = null;
  for (const r of resolved) {
    const g = r.action.group;
    if (groups.length === 0 || g !== last) groups.push([]);
    groups[groups.length - 1].push(r);
    last = g;
  }
  return groups;
}
