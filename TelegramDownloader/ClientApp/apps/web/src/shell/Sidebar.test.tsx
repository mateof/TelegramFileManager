import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { TooltipProvider } from '@tfm/ui';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useUi } from '@/stores/ui';
import { Sidebar } from './Sidebar';

vi.mock('@/lib/api', () => ({
  api: {
    channels: {
      list: vi.fn(async () => ({
        items: [{ id: 1, name: 'Music', isOwner: true, isFavorite: false, hasDatabase: true, type: 'channel' }],
        page: { hasNext: false }
      }))
    }
  },
  errorMessage: (e: unknown) => String(e)
}));

function renderSidebar(inDrawer: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <MemoryRouter>
          <div style={{ height: 800 }}>
            <Sidebar inDrawer={inDrawer} />
          </div>
        </MemoryRouter>
      </TooltipProvider>
    </QueryClientProvider>
  );
}

beforeEach(() => {
  localStorage.removeItem('tfm.web.ui');
  useUi.setState({ drawerCollapsedSections: [], drawerChannelsExpanded: false });
});

describe('<Sidebar> in the mobile drawer', () => {
  it('folds navigation sections and remembers it', () => {
    renderSidebar(true);
    const media = screen.getByRole('button', { name: /Media/ });
    expect(media).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('link', { name: 'Playlists' })).toBeInTheDocument();

    fireEvent.click(media);
    expect(media).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('link', { name: 'Playlists' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Local files' })).toBeInTheDocument();
    expect(useUi.getState().drawerCollapsedSections).toEqual(['media']);
    expect(localStorage.getItem('tfm.web.ui')).toContain('"drawerCollapsedSections":["media"]');
  });

  it('expands the channel list to the whole drawer and back', () => {
    renderSidebar(true);
    fireEvent.click(screen.getByRole('button', { name: 'Expand the channel list' }));
    expect(screen.queryByRole('navigation', { name: 'Main navigation' })).toBeNull();
    expect(screen.getByLabelText('Filter channels')).toBeInTheDocument();
    expect(useUi.getState().drawerChannelsExpanded).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Show the menu' }));
    expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument();
    expect(useUi.getState().drawerChannelsExpanded).toBe(false);
  });
});

describe('<Sidebar> on desktop', () => {
  it('keeps plain section titles and no expand button, even with drawer state saved', () => {
    useUi.setState({ drawerCollapsedSections: ['media'], drawerChannelsExpanded: true });
    renderSidebar(false);
    expect(screen.getByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Playlists' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Media/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Expand the channel list' })).toBeNull();
  });
});
