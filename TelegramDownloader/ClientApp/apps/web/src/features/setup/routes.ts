import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the setup feature (relative to /app). Owned by the setup feature. */
export const routes: RouteObject[] = [
  { path: 'setup', lazy: lazyPage(() => import('./pages/SetupPage')) }
];
