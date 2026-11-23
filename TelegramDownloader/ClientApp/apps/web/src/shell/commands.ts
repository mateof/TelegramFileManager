import type { CommandGroup } from '@tfm/ui';
import { useEffect } from 'react';
import { create } from 'zustand';

/**
 * Extra command palette groups contributed by features while they are
 * mounted (e.g. the file manager adds "New folder", "Upload" for the current
 * channel):
 *
 * ```ts
 * useRegisterCommands(useMemo(() => ({ id: 'fm', heading: t('Current folder'), items: [...] }), [deps]));
 * ```
 */
interface CommandRegistry {
  groups: Record<string, CommandGroup>;
  set: (group: CommandGroup) => void;
  remove: (id: string) => void;
}

export const useCommandRegistry = create<CommandRegistry>()((set) => ({
  groups: {},
  set: (group) => set((s) => ({ groups: { ...s.groups, [group.id]: group } })),
  remove: (id) =>
    set((s) => {
      const groups = { ...s.groups };
      delete groups[id];
      return { groups };
    })
}));

/** Registers a command group while the calling component is mounted. Memoize `group`. */
export function useRegisterCommands(group: CommandGroup | null | undefined): void {
  useEffect(() => {
    if (!group) return;
    useCommandRegistry.getState().set(group);
    return () => useCommandRegistry.getState().remove(group.id);
  }, [group]);
}
