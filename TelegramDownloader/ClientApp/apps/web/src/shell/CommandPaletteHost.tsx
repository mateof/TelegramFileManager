import { lazy, Suspense, useEffect, useState } from 'react';
import { useUi } from '@/stores/ui';

const CommandPaletteContent = lazy(() => import('./CommandPaletteContent'));

/**
 * Ctrl/Cmd+K palette: pages, channels, actions and feature-contributed
 * commands (see `useRegisterCommands`). The palette code loads on first open.
 */
export function CommandPaletteHost() {
  const open = useUi((s) => s.commandOpen);
  const setOpen = useUi((s) => s.setCommandOpen);
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setOpen(!useUi.getState().commandOpen);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [setOpen]);

  if (open && !mounted) setMounted(true);
  if (!mounted) return null;
  return (
    <Suspense fallback={null}>
      <CommandPaletteContent />
    </Suspense>
  );
}
