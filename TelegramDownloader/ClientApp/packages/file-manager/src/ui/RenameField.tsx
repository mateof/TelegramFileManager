import { cn } from '@tfm/ui';
import { useEffect, useRef, useState } from 'react';
import type { FileEntry } from '../types';
import { useFileManagerUi } from './context';

/** Inline rename input: Enter saves, Escape cancels, blur saves. Selects the name without the extension. */
export function RenameField({ entry, className }: { entry: FileEntry; className?: string }) {
  const { fm, strings } = useFileManagerUi();
  const [value, setValue] = useState(entry.name);
  const ref = useRef<HTMLInputElement>(null);
  const done = useRef(false);

  useEffect(() => {
    const input = ref.current;
    if (!input) return;
    input.focus();
    const dot = entry.kind === 'file' ? entry.name.lastIndexOf('.') : -1;
    input.setSelectionRange(0, dot > 0 ? dot : entry.name.length);
  }, [entry]);

  const commit = () => {
    if (done.current) return;
    done.current = true;
    void fm.rename.commit(value);
  };

  return (
    <input
      ref={ref}
      value={value}
      aria-label={strings.rename}
      onChange={(e) => setValue(e.target.value)}
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
      onPointerDown={(e) => e.stopPropagation()}
      onKeyDown={(e) => {
        e.stopPropagation();
        if (e.key === 'Enter') commit();
        if (e.key === 'Escape') {
          done.current = true;
          fm.rename.cancel();
        }
      }}
      onBlur={commit}
      className={cn(
        'h-7 w-full min-w-0 rounded-md border border-accent-500/70 bg-surface-2 px-2 text-sm text-fg outline-none ring-3 ring-accent-500/20',
        className
      )}
    />
  );
}
