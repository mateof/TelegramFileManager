import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the transfers feature (relative to /app). Owned by the transfers feature. */
export const routes: RouteObject[] = [
  { path: 'transfers', lazy: lazyPage(() => import('./pages/TransfersPage')) }
];
