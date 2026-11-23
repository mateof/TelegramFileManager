import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the apps feature (links to the sister apps). */
export const routes: RouteObject[] = [{ path: 'apps', lazy: lazyPage(() => import('./pages/AppsPage')) }];
