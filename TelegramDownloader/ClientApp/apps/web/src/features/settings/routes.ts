import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the settings feature (relative to /app). One page: `:section` selects what it shows. */
export const routes: RouteObject[] = [
  { path: 'settings', lazy: lazyPage(() => import('./pages/SettingsPage')) },
  { path: 'settings/:section', lazy: lazyPage(() => import('./pages/SettingsPage')) }
];
