import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the channels feature (relative to /app). Owned by the channels feature. */
export const routes: RouteObject[] = [
  { path: 'channels', lazy: lazyPage(() => import('./pages/ChannelsIndexPage')) },
  { path: 'channels/:id/*', lazy: lazyPage(() => import('./pages/ChannelPage')) }
];
