import { Toaster as SonnerToaster, toast } from 'sonner';
import { useResolvedTheme } from '../theme/ThemeController';

/**
 * Toast host (sonner), themed. Mount once. Show toasts with
 * `toast('Saved')`, `toast.success(...)`, `toast.error(...)`, `toast.promise(...)`.
 */
export function Toaster({ position = 'bottom-right' }: { position?: 'bottom-right' | 'top-center' | 'bottom-center' | 'top-right' }) {
  const { isLight } = useResolvedTheme();
  return (
    <SonnerToaster
      position={position}
      theme={isLight ? 'light' : 'dark'}
      closeButton
      richColors
      visibleToasts={4}
      offset={{ bottom: 'calc(env(safe-area-inset-bottom) + 1rem)', right: '1rem' }}
      mobileOffset={{ bottom: 'calc(env(safe-area-inset-bottom) + 5rem)' }}
    />
  );
}

export { toast };
