import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the maintenance feature (relative to /app). Owned by the maintenance feature. */
export const routes: RouteObject[] = [
  { path: 'maintenance', lazy: lazyPage(() => import('./pages/MaintenancePage')) }
];
