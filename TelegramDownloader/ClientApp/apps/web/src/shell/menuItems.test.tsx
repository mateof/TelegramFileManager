import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuTrigger, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@tfm/ui';

const link = (
  <a href="https://example.test/">
    <svg /> Open
  </a>
);

describe('menu items', () => {
  it('a dropdown item can be a link, with its icon and shortcut inside', () => {
    render(
      <DropdownMenu open>
        <DropdownMenuTrigger>Menu</DropdownMenuTrigger>
        <DropdownMenuContent>
          <DropdownMenuItem asChild>{link}</DropdownMenuItem>
          <DropdownMenuItem asChild icon={<i data-testid="icon" />} shortcut="Ctrl+P">
            <a href="https://example.test/print">Print</a>
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    );
    const open = screen.getByRole('menuitem', { name: 'Open' });
    expect(open.tagName).toBe('A');
    expect(open).toHaveAttribute('href', 'https://example.test/');
    const print = screen.getByRole('menuitem', { name: /Print/ });
    expect(print).toContainElement(screen.getByTestId('icon'));
    expect(print).toHaveTextContent('Ctrl+P');
  });

  it('a context menu item can be a link', () => {
    render(
      <ContextMenu>
        <ContextMenuTrigger>Area</ContextMenuTrigger>
        <ContextMenuContent>
          <ContextMenuItem asChild>{link}</ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>
    );
    fireEvent.contextMenu(screen.getByText('Area'));
    expect(screen.getByRole('menuitem', { name: 'Open' })).toHaveAttribute('href', 'https://example.test/');
  });

  it('a plain item still selects', () => {
    const onSelect = vi.fn();
    render(
      <DropdownMenu open>
        <DropdownMenuTrigger>Menu</DropdownMenuTrigger>
        <DropdownMenuContent>
          <DropdownMenuItem onSelect={onSelect}>Refresh</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    );
    fireEvent.click(screen.getByRole('menuitem', { name: 'Refresh' }));
    expect(onSelect).toHaveBeenCalledOnce();
  });
});
