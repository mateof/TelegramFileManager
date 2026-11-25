import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Component gallery at /app/dev/ui (not linked from the navigation; always available, lazy). */
export const routes: RouteObject[] = [{ path: 'dev/ui', lazy: lazyPage(() => import('./pages/UiGalleryPage')) }];
