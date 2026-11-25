import { useEffect, useState } from 'react';

export interface SearchText {
  /** What the input shows. */
  value: string;
  /** What to search: `value` once the typing stops, or at once after `commit`. */
  query: string;
  /** `value` has changes that are not searched yet. */
  pending: boolean;
  setValue(value: string): void;
  /** Searches now (Enter); with a value, sets it too. */
  commit(value?: string): void;
}

/** Text of a search that costs a request: it waits until the typing stops, and Enter does not wait. */
export function useSearchText(initial: string, delayMs: number): SearchText {
  const [value, setValue] = useState(initial);
  const [query, setQuery] = useState(initial.trim());
  const trimmed = value.trim();
  const pending = trimmed !== query;
  useEffect(() => {
    if (!pending) return;
    const timer = setTimeout(() => setQuery(trimmed), delayMs);
    return () => clearTimeout(timer);
  }, [pending, trimmed, delayMs]);
  return {
    value,
    query,
    pending,
    setValue,
    commit: (next) => {
      if (next !== undefined) setValue(next);
      setQuery((next ?? value).trim());
    }
  };
}
