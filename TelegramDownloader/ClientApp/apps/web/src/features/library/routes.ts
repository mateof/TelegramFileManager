import type { RouteObject } from 'react-router';
import { lazyPage } from '@/app/lazyPage';

/** Routes of the library feature (relative to /app). Owned by the library feature. */
export const routes: RouteObject[] = [
  { path: 'library', lazy: lazyPage(() => import('./pages/LibraryPage')) },
  { path: 'library/review', lazy: lazyPage(() => import('./pages/LibraryReviewPage')) },
  { path: 'library/items/:id', lazy: lazyPage(() => import('./pages/LibraryItemPage')) },
  // movies | series (anything else redirects to the home page)
  { path: 'library/:section', lazy: lazyPage(() => import('./pages/LibrarySectionPage')) }
];
