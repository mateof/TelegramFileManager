import { QueryClientProvider } from '@tanstack/react-query';
import { ConfirmHost, ThemeController, Toaster, TooltipProvider } from '@tfm/ui';
import { useMemo } from 'react';
import { createBrowserRouter, RouterProvider } from 'react-router';
import { queryClient } from '@/lib/queryClient';
import { appRoutes } from './routes';

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
