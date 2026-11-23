import type { ComponentType } from 'react';
import type { RouteObject } from 'react-router';

/**
 * Lazy route page: `{ path: 'transfers', lazy: lazyPage(() => import('./pages/TransfersPage')) }`.
 * The page module must `export default` its component; it becomes its own chunk.
 */
export function lazyPage(load: () => Promise<{ default: ComponentType }>): RouteObject['lazy'] {
  return async () => ({ Component: (await load()).default });
}
