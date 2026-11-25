import type { ApiChannelDto } from '@tfm/api-client';
import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { ChannelCombobox } from './ChannelCombobox';
import { filterChannels, initialTab, sortChannels } from './channelFilter';

function ch(id: number, name: string, extra: Partial<ApiChannelDto> = {}): ApiChannelDto {
  return { id, name, isOwner: false, isFavorite: false, hasDatabase: true, type: 'channel', ...extra } as ApiChannelDto;
}

const CHANNELS = [
  ch(1, 'Zeta ajena'),
  ch(2, 'Música', { isOwner: true }),
  ch(3, 'pruebas 11', { isOwner: true }),
  ch(4, 'pruebas 2', { isOwner: true }),
  ch(5, 'Favorito ajeno', { isFavorite: true }),
  ch(6, 'Musica compartida')
];

describe('channel combobox helpers', () => {
  it('sorts own channels first, then favourites, in natural order', () => {
    expect(sortChannels(CHANNELS).map((c) => c.id)).toEqual([2, 4, 3, 5, 6, 1]);
  });

  it('opens on the tab that has channels', () => {
    expect(initialTab(CHANNELS)).toBe('mine');
    expect(initialTab([ch(1, 'a', { isFavorite: true })])).toBe('favourites');
    expect(initialTab([ch(1, 'a')])).toBe('all');
  });

  it('filters by tab and searches ignoring accents and case', () => {
    expect(filterChannels(CHANNELS, 'mine', 'musica').map((c) => c.id)).toEqual([2]);
    expect(filterChannels(CHANNELS, 'all', 'MÚSICA').map((c) => c.id)).toEqual([2, 6]);
    expect(filterChannels(CHANNELS, 'favourites', '').map((c) => c.id)).toEqual([5]);
  });
});

function Harness() {
  const [value, setValue] = useState('');
  return (
    <>
      <ChannelCombobox channels={CHANNELS} value={value} onChange={setValue} placeholder="Choose a channel" />
      <output data-testid="value">{value}</output>
    </>
  );
}

describe('ChannelCombobox', () => {
  it('shows own channels first and offers the matches in other tabs', () => {
    render(<Harness />);
    fireEvent.click(screen.getByRole('combobox'));
    const options = screen.getAllByRole('option').map((o) => o.textContent);
    expect(options).toEqual(['Música', 'pruebas 2', 'pruebas 11']);

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'musica' } });
    expect(screen.getAllByRole('option')).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: '1 more in all channels' }));
    expect(screen.getAllByRole('option')).toHaveLength(2);
  });

  it('picks the highlighted channel with the keyboard', () => {
    render(<Harness />);
    fireEvent.click(screen.getByRole('combobox'));
    const input = screen.getByRole('textbox');
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(screen.getByTestId('value').textContent).toBe('4');
  });
});
