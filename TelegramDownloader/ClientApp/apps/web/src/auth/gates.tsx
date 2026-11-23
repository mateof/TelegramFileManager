import { apiEvents, AuthStep, isApiError, UNAUTHORIZED_EVENT } from '@tfm/api-client';
import { Button } from '@tfm/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { RefreshCw, ServerCrash } from 'lucide-react';
import { lazy, Suspense, useEffect, type ReactNode } from 'react';
import { Navigate, Outlet } from 'react-router';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { startRealtime, useEventsMessage } from '@/lib/realtime';
import { AppLayout } from '@/shell/AppLayout';
import { useTelegramStatus } from './useTelegramStatus';
import { AuthShell, SplashScreen } from './AuthShell';

// Login screens are only needed when signed out: keep them off the initial bundle
const TelegramLogin = lazy(() => import('./TelegramLogin').then((m) => ({ default: m.TelegramLogin })));
const WebLoginScreen = lazy(() => import('./WebAuthScreens').then((m) => ({ default: m.WebLoginScreen })));
const WebSetupScreen = lazy(() => import('./WebAuthScreens').then((m) => ({ default: m.WebSetupScreen })));

function ServerError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  return (
    <AuthShell title={t('Cannot reach the server')} subtitle={errorMessage(error)}>
      <div className="flex flex-col items-center gap-4 text-center">
        <ServerCrash className="size-8 text-fg-subtle" />
        <Button variant="secondary" icon={<RefreshCw />} onClick={onRetry}>
          {t('Retry')}
        </Button>
      </div>
    </AuthShell>
  );
}

/**
 * Web session gate (cookie login). Order: no password yet → setup screen; no
 * session → login screen; otherwise the children. Any 401 from the API
 * re-checks the status, which brings the login screen back.
 *
 * A server without the web-auth API (404) is treated as open, so the app keeps
 * working against older back-ends during development.
 */
export function WebAuthGate({ children }: { children?: ReactNode }) {
  const qc = useQueryClient();
  const status = useQuery({
    queryKey: queryKeys.webAuthStatus,
    queryFn: async ({ signal }) => {
      try {
        return await api.webAuth.status(signal);
      } catch (e) {
        if (isApiError(e) && e.status === 404) {
          return { passwordSet: true, authenticated: true, lanBypassEnabled: false, lanBypassActive: false };
        }
        throw e;
      }
    },
    staleTime: Infinity,
    retry: 1
  });

  useEffect(() => {
    const onUnauthorized = () => void qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus });
    apiEvents.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
    return () => apiEvents.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
  }, [qc]);

  if (status.isPending) return <SplashScreen />;
  if (status.isError) return <ServerError error={status.error} onRetry={() => void status.refetch()} />;
  if (!status.data.passwordSet && !status.data.lanBypassActive) {
    return (
      <Suspense fallback={<SplashScreen />}>
        <WebSetupScreen />
      </Suspense>
    );
  }
  if (!status.data.authenticated) {
    return (
      <Suspense fallback={<SplashScreen />}>
        <WebLoginScreen />
      </Suspense>
    );
  }
  return <>{children ?? <Outlet />}</>;
}

/**
 * Telegram session gate: app not configured → /setup wizard; not logged in →
 * Telegram login; otherwise the app layout. Also starts the realtime hubs.
 */
export function TelegramGate() {
  const qc = useQueryClient();
  const status = useTelegramStatus();

  useEffect(() => {
    startRealtime();
  }, []);

  useEventsMessage('AuthState', () => {
    void qc.invalidateQueries({ queryKey: queryKeys.telegramStatus });
  });

  if (status.isPending) return <SplashScreen />;
  if (status.isError) return <ServerError error={status.error} onRetry={() => void status.refetch()} />;
  if (status.data.step === AuthStep.SetupRequired || !status.data.isConfigured) return <Navigate to="/setup" replace />;
  if (!status.data.isAuthenticated) {
    return (
      <Suspense fallback={<SplashScreen />}>
        <TelegramLogin status={status.data} />
      </Suspense>
    );
  }
  return <AppLayout />;
}
