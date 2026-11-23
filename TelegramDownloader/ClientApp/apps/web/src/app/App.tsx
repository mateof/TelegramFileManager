import { QueryClientProvider } from '@tanstack/react-query';
import { configureUiStrings, ConfirmHost, ThemeController, Toaster, TooltipProvider } from '@tfm/ui';
import { useMemo } from 'react';
import { createBrowserRouter, RouterProvider } from 'react-router';
import { t } from '@/i18n';
import { queryClient } from '@/lib/queryClient';
import { appRoutes } from './routes';

// Built-in labels of @tfm/ui, translated once (the language only changes with a reload)
configureUiStrings({
  close: t('Close'),
  cancel: t('Cancel'),
  confirm: t('Confirm'),
  loading: t('Loading...'),
  noResults: t('No results'),
  searchPlaceholder: t('Type a command or search...'),
  breadcrumb: t('Breadcrumb'),
  more: t('More')
});

const ROUTER_BASENAME = '/app';

/** Providers: Query, theme, tooltips, toasts, confirm dialogs, router. */
export function App() {
  const router = useMemo(() => createBrowserRouter(appRoutes, { basename: ROUTER_BASENAME }), []);
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeController />
      <TooltipProvider delayDuration={350}>
        <RouterProvider router={router} />
        <ConfirmHost />
      </TooltipProvider>
      <Toaster />
    </QueryClientProvider>
  );
}
