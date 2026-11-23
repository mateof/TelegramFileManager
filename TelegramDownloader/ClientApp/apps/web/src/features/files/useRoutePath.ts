import { normalizePath } from '@tfm/file-manager';
import { useCallback } from 'react';
import { useNavigate, useParams } from 'react-router';

/** `/music/rock` → `/music/rock` with every segment URL-encoded. */
export function encodePath(path: string): string {
  const p = normalizePath(path);
  return p === '/' ? '' : p.split('/').map(encodeURIComponent).join('/');
}

/**
 * The folder path carried by the route splat (`channels/:id/*`, `local/*`),
 * e.g. `/app/channels/42/music/rock` → `/music/rock`, plus a navigate function
 * that writes it back (browser back/forward then move the file manager too).
 */
export function useRoutePath(base: string): [string, (path: string) => void] {
  const params = useParams();
  const navigate = useNavigate();
  const path = normalizePath(params['*'] ?? '');
  const go = useCallback((p: string) => navigate(`${base}${encodePath(p)}`), [navigate, base]);
  return [path, go];
}
