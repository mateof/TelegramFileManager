import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the system feature (relative to /app). Owned by the system feature. */
export const routes: RouteObject[] = [
  { path: 'system', lazy: lazyPage(() => import('./pages/SystemPage')) }
];
