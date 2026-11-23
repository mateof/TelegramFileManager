import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the chat feature (relative to /app). Owned by the chat feature. */
export const routes: RouteObject[] = [
  { path: 'chat/:id', lazy: lazyPage(() => import('./pages/ChatPage')) }
];
