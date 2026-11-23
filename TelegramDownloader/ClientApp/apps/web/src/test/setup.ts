// Tests assert on the English source strings (i18n keys)
localStorage.setItem('tfm.web.language', 'en');

import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterEach } from 'vitest';

// Lazy chunks load slowly when every test file runs in parallel: be patient in findBy*/waitFor
configure({ asyncUtilTimeout: 4000 });

// Vitest runs without globals, so Testing Library can't unmount on its own
afterEach(() => cleanup());

// jsdom has no matchMedia: behave like a desktop dark screen
if (!window.matchMedia) {
  window.matchMedia = (query: string) =>
    ({
      matches: query.includes('min-width'),
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false
    }) as MediaQueryList;
}

// Radix uses ResizeObserver / pointer capture APIs jsdom does not have
if (!('ResizeObserver' in window)) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  (window as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;
}
if (!Element.prototype.hasPointerCapture) {
  Element.prototype.hasPointerCapture = () => false;
  Element.prototype.releasePointerCapture = () => {};
}
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {};
}
