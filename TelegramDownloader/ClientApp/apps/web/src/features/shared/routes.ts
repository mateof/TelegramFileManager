import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the shared feature (relative to /app). Owned by the shared feature. */
export const routes: RouteObject[] = [
  { path: 'shared', lazy: lazyPage(() => import('./pages/SharedIndexPage')) },
  { path: 'shared/:id/*', lazy: lazyPage(() => import('./pages/SharedCollectionPage')) }
];
