import type { ImportProgressEvent } from '@tfm/api-client';
import { useState } from 'react';
import { useEventsMessage } from '@/lib/realtime';

/** Live progress of an import started by `POST .../files/import` or `POST /api/v1/shares/import`. */
export function useImportProgress(importId: string | null, onDone?: (event: ImportProgressEvent) => void) {
  const [progress, setProgress] = useState<ImportProgressEvent | null>(null);
  const [seenId, setSeenId] = useState(importId);
  if (seenId !== importId) {
    setSeenId(importId);
    setProgress(null);
  }
  useEventsMessage('ImportProgress', (e) => {
    if (!importId || e.id !== importId) return;
    setProgress(e);
    if (e.done) onDone?.(e);
  });
  return progress;
}
