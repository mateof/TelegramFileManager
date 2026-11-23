import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the logs feature (relative to /app). Owned by the logs feature. */
export const routes: RouteObject[] = [
  { path: 'logs', lazy: lazyPage(() => import('./pages/LogsPage')) }
];
