import { useEffect, useRef, type RefObject } from 'react';

/** Calls `onVisible` while `target` is on screen inside `root`. */
export function useSentinel(root: RefObject<HTMLElement | null>, target: RefObject<HTMLElement | null>, enabled: boolean, onVisible: () => void) {
  const cb = useRef(onVisible);
  useEffect(() => {
    cb.current = onVisible;
  });
  useEffect(() => {
    if (!enabled || !target.current || typeof IntersectionObserver === 'undefined') return;
    const io = new IntersectionObserver((entries) => entries.some((e) => e.isIntersecting) && cb.current(), {
      root: root.current,
      rootMargin: '400px 0px'
    });
    io.observe(target.current);
    return () => io.disconnect();
  }, [root, target, enabled]);
}
