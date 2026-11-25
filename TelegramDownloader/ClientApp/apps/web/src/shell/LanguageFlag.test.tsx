import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LANGUAGES } from '@/i18n';
import { LanguageFlag } from './LanguageFlag';

describe('LanguageFlag', () => {
  it('draws a decorative flag for every language', () => {
    for (const { code } of LANGUAGES) {
      const { container, unmount } = render(<LanguageFlag lang={code} />);
      const svg = container.querySelector('svg')!;
      expect(svg).toHaveAttribute('data-flag', code);
      expect(svg).toHaveAttribute('aria-hidden', 'true');
      expect(svg.children.length).toBeGreaterThan(0);
      unmount();
    }
  });

  it('draws the Galician flag as a blue diagonal band on white', () => {
    const { container } = render(<LanguageFlag lang="gl" />);
    expect(container.querySelector('rect[fill="#FFFFFF"]')).not.toBeNull();
    expect(container.querySelector('path[stroke="#0099CC"]')).not.toBeNull();
  });
});
