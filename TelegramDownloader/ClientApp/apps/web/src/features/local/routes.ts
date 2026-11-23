import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the local feature (relative to /app). Owned by the local feature. */
export const routes: RouteObject[] = [
  { path: 'local/*', lazy: lazyPage(() => import('./pages/LocalFilesPage')) }
];
