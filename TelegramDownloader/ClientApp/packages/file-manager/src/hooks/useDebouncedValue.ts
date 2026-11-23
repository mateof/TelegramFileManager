import { useEffect, useState } from 'react';

/** `value`, updated `delayMs` after it stops changing. */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    if (Object.is(value, debounced)) return;
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs, debounced]);
  return debounced;
}
