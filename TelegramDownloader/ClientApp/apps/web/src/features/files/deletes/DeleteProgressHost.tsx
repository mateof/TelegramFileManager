import { lazy, Suspense } from 'react';
import { useDeletes } from './deleteStore';

const DeleteProgressPanel = lazy(() => import('./DeleteProgressPanel'));

/**
 * Rendered once by the layout (bottom left of the content area, above the
 * player and the mobile bottom nav), so the progress of a delete survives
 * navigation. Only the store is in the initial bundle: the cards are a lazy
 * chunk loaded with the first delete.
 */
export function DeleteProgressHost() {
  const any = useDeletes((s) => s.ops.length > 0);
  if (!any) return null;
  return (
    <Suspense fallback={null}>
      <DeleteProgressPanel />
    </Suspense>
  );
}
