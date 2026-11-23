import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the settings feature (relative to /app). Owned by the settings feature. */
export const routes: RouteObject[] = [
  { path: 'settings', lazy: lazyPage(() => import('./pages/SettingsPage')) },
  { path: 'settings/:section', lazy: lazyPage(() => import('./pages/SettingsSectionPage')) }
];
