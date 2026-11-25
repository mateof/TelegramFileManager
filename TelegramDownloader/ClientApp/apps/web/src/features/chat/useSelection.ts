import type { ApiChatMessageDto } from '@tfm/api-client';
import { useCallback, useMemo, useRef, useState } from 'react';
import { rangeOf } from './chatModel';

export interface MessageSelection {
  /** Ids in the order they were chosen. */
  ids: number[];
  messages: ApiChatMessageDto[];
  has(id: number): boolean;
  /** Adds or removes a message; with `range`, everything from the last one clicked, in the order of `list`. */
  toggle(message: ApiChatMessageDto, list: ApiChatMessageDto[], range?: boolean): void;
  set(messages: ApiChatMessageDto[]): void;
  clear(): void;
}

/** Chosen messages, kept when the list loads more. */
export function useSelection(): MessageSelection {
  const [selected, setSelected] = useState<ReadonlyMap<number, ApiChatMessageDto>>(() => new Map());
  const last = useRef<number | null>(null);

  const toggle = useCallback((message: ApiChatMessageDto, list: ApiChatMessageDto[], range = false) => {
    const from = last.current;
    last.current = message.id;
    setSelected((current) => {
      const next = new Map(current);
      if (range && from !== null && from !== message.id) {
        const ids = new Set(rangeOf(list.map((m) => m.id), from, message.id));
        for (const m of list) if (ids.has(m.id)) next.set(m.id, m);
      } else if (!next.delete(message.id)) next.set(message.id, message);
      return next;
    });
  }, []);
  const set = useCallback((messages: ApiChatMessageDto[]) => setSelected(new Map(messages.map((m) => [m.id, m]))), []);
  const clear = useCallback(() => {
    last.current = null;
    setSelected(new Map());
  }, []);

  return useMemo(
    () => ({ ids: [...selected.keys()], messages: [...selected.values()], has: (id: number) => selected.has(id), toggle, set, clear }),
    [selected, toggle, set, clear]
  );
}
