import { Navigate, type RouteObject } from 'react-router';
import { TelegramGate, WebAuthGate } from '@/auth/gates';
import { routes as appsRoutes } from '@/features/apps/routes';
import { routes as channelsRoutes } from '@/features/channels/routes';
import { routes as chatRoutes } from '@/features/chat/routes';
import { routes as devUiRoutes } from '@/features/dev-ui/routes';
import { routes as libraryRoutes } from '@/features/library/routes';
import { routes as localRoutes } from '@/features/local/routes';
import { routes as logsRoutes } from '@/features/logs/routes';
import { routes as maintenanceRoutes } from '@/features/maintenance/routes';
import { routes as playlistsRoutes } from '@/features/playlists/routes';
import { routes as searchRoutes } from '@/features/search/routes';
import { routes as settingsRoutes } from '@/features/settings/routes';
import { routes as setupRoutes } from '@/features/setup/routes';
import { routes as sharedRoutes } from '@/features/shared/routes';
import { routes as systemRoutes } from '@/features/system/routes';
import { routes as transfersRoutes } from '@/features/transfers/routes';
import { RouteError } from './RouteError';
import NotFoundPage from './NotFoundPage';

/**
 * Route table (paths are relative to the /app basename). Each feature owns its
 * `features/<feature>/routes.ts` (lazy pages); add a new feature by importing
 * its routes here. Order inside the layout does not matter (ranked matching).
 */
export const appRoutes: RouteObject[] = [
  {
    element: <WebAuthGate />,
    errorElement: <RouteError />,
    children: [
      // Full-screen pages reachable before the Telegram login
      ...setupRoutes,
      {
        element: <TelegramGate />,
        errorElement: <RouteError />,
        children: [
          { index: true, element: <Navigate to="/channels" replace /> },
          {
            errorElement: <RouteError />,
            children: [
              ...channelsRoutes,
              ...localRoutes,
              ...sharedRoutes,
              ...transfersRoutes,
              ...playlistsRoutes,
              ...libraryRoutes,
              ...searchRoutes,
              ...chatRoutes,
              ...settingsRoutes,
              ...logsRoutes,
              ...systemRoutes,
              ...maintenanceRoutes,
              ...appsRoutes,
              ...devUiRoutes,
              { path: '*', element: <NotFoundPage /> }
            ]
          }
        ]
      }
    ]
  }
];
