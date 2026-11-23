import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the search feature (relative to /app). Owned by the search feature. */
export const routes: RouteObject[] = [
  { path: 'search', lazy: lazyPage(() => import('./pages/SearchPage')) }
];
