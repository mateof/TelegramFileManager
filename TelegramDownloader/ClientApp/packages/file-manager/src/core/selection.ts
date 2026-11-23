/**
 * Selection model (pure). Works on the ordered list of ids currently shown, so
 * Shift ranges follow what the user sees (sort/filter included).
 */

export interface SelectionState {
  /** Selected ids. */
  selected: ReadonlySet<string>;
  /** Start of Shift ranges. */
  anchor: string | null;
  /** Keyboard cursor (the item with the focus ring). */
  focus: string | null;
}

export type SelectionAction =
  /** Pointer click: plain = select only it, ctrl/meta = toggle, shift = range from the anchor. */
  | { type: 'click'; id: string; ctrl?: boolean; shift?: boolean }
  /** Toggle one id (checkbox, long press, touch selection mode). */
  | { type: 'toggle'; id: string }
  | { type: 'selectAll' }
  | { type: 'clear' }
  | { type: 'set'; ids: readonly string[] }
  /** Move the cursor by `delta` positions (arrows); shift extends, ctrl moves the cursor only. */
  | { type: 'move'; delta: number; shift?: boolean; ctrl?: boolean }
  /** Jump the cursor to an index (Home/End/type-ahead). */
  | { type: 'moveTo'; index: number; shift?: boolean; ctrl?: boolean }
  /** Cursor only (no selection change). */
  | { type: 'focus'; id: string | null }
  /** Drop ids that are no longer in the listing. */
  | { type: 'prune' };

export const emptySelection: SelectionState = { selected: new Set(), anchor: null, focus: null };

function range(order: readonly string[], from: string | null, to: string): string[] {
  const end = order.indexOf(to);
  if (end < 0) return [];
  const start = from ? order.indexOf(from) : -1;
  if (start < 0) return [to];
  const [a, b] = start <= end ? [start, end] : [end, start];
  return order.slice(a, b + 1);
}

function moveCursor(state: SelectionState, order: readonly string[], target: number, shift?: boolean, ctrl?: boolean): SelectionState {
  if (order.length === 0) return state;
  const index = Math.max(0, Math.min(order.length - 1, target));
  const id = order[index];
  if (ctrl) return { ...state, focus: id };
  if (shift) {
    const anchor = state.anchor ?? state.focus ?? id;
    return { selected: new Set(range(order, anchor, id)), anchor, focus: id };
  }
  return { selected: new Set([id]), anchor: id, focus: id };
}

/** Applies an action. `order` is the ordered list of visible ids. */
export function selectionReducer(state: SelectionState, action: SelectionAction, order: readonly string[]): SelectionState {
  switch (action.type) {
    case 'click': {
      if (action.shift) {
        const anchor = state.anchor ?? state.focus;
        const ids = range(order, anchor, action.id);
        if (action.ctrl) {
          const next = new Set(state.selected);
          ids.forEach((i) => next.add(i));
          return { selected: next, anchor: anchor ?? action.id, focus: action.id };
        }
        return { selected: new Set(ids), anchor: anchor ?? action.id, focus: action.id };
      }
      if (action.ctrl) {
        const next = new Set(state.selected);
        if (next.has(action.id)) next.delete(action.id);
        else next.add(action.id);
        return { selected: next, anchor: action.id, focus: action.id };
      }
      return { selected: new Set([action.id]), anchor: action.id, focus: action.id };
    }
    case 'toggle': {
      const next = new Set(state.selected);
      if (next.has(action.id)) next.delete(action.id);
      else next.add(action.id);
      return { selected: next, anchor: action.id, focus: action.id };
    }
    case 'selectAll':
      return { selected: new Set(order), anchor: state.anchor ?? order[0] ?? null, focus: state.focus ?? order[0] ?? null };
    case 'clear':
      return { selected: new Set(), anchor: null, focus: state.focus };
    case 'set': {
      const ids = action.ids.filter((id) => order.includes(id));
      return { selected: new Set(ids), anchor: ids[0] ?? null, focus: ids[ids.length - 1] ?? state.focus };
    }
    case 'move': {
      const current = state.focus ? order.indexOf(state.focus) : -1;
      const target = current < 0 ? (action.delta >= 0 ? 0 : order.length - 1) : current + action.delta;
      return moveCursor(state, order, target, action.shift, action.ctrl);
    }
    case 'moveTo':
      return moveCursor(state, order, action.index, action.shift, action.ctrl);
    case 'focus':
      return { ...state, focus: action.id };
    case 'prune': {
      const present = new Set(order);
      let changed = false;
      const next = new Set<string>();
      state.selected.forEach((id) => {
        if (present.has(id)) next.add(id);
        else changed = true;
      });
      const focus = state.focus && present.has(state.focus) ? state.focus : null;
      const anchor = state.anchor && present.has(state.anchor) ? state.anchor : null;
      if (!changed && focus === state.focus && anchor === state.anchor) return state;
      return { selected: next, anchor, focus };
    }
  }
}
