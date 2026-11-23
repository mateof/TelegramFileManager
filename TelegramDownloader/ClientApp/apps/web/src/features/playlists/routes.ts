import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the playlists feature (relative to /app). Owned by the playlists feature. */
export const routes: RouteObject[] = [
  { path: 'playlists', lazy: lazyPage(() => import('./pages/PlaylistsPage')) },
  { path: 'playlists/:id', lazy: lazyPage(() => import('./pages/PlaylistPage')) }
];
