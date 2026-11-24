import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useDeletes, type TrackedDelete } from './deleteStore';

vi.mock('./deleteTracker', () => ({ cancelDelete: vi.fn(), dismissDelete: vi.fn() }));

const tracker = await import('./deleteTracker');
const { default: DeleteProgressPanel } = await import('./DeleteProgressPanel');
const { DeleteProgressHost } = await import('./DeleteProgressHost');

function op(patch: Partial<TrackedDelete> = {}): TrackedDelete {
  return {
    key: 'k1',
    kind: 'channel',
    channelId: '1001',
    placeName: 'Música',
    itemCount: 1,
    firstName: 'Rock',
    providerId: 'channel:1001',
    entryIds: ['rock'],
    mode: 'tracked',
    operationId: 'op1',
    status: 'running',
    failures: [],
    failedCount: 0,
    lastUpdate: Date.now(),
    startedAt: Date.now(),
    progress: {
      id: 'op1',
      kind: 'channel',
      itemCount: 1,
      phase: 'deleting',
      counted: true,
      finished: false,
      totalFiles: 26,
      totalFolders: 2,
      totalBytes: 0,
      processedFiles: 13,
      processedFolders: 0,
      processedBytes: 0,
      messagesDeleted: 13,
      indexOnly: false,
      currentItem: '/Rock/Pista 13.mp3',
      failedCount: 0,
      failed: [],
      cancelRequested: false,
      startedAt: new Date().toISOString()
    },
    ...patch
  };
}

describe('delete progress panel', () => {
  beforeEach(() => useDeletes.setState({ ops: [], hidden: {} }));

  it('renders nothing without deletes', () => {
    const { container } = render(<DeleteProgressHost />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows the title, counts, current item and a cancel button', async () => {
    useDeletes.setState({ ops: [op()] });
    render(<DeleteProgressPanel />);

    expect(screen.getByText('Deleting "Rock" from Música')).toBeInTheDocument();
    expect(screen.getByText(/13 of 26 files/)).toBeInTheDocument();
    expect(screen.getByText('/Rock/Pista 13.mp3')).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', String((13 / 28) * 100));

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(tracker.cancelDelete).toHaveBeenCalledWith('k1');
  });

  it('lists the failed items in the details of a partial result', async () => {
    useDeletes.setState({
      ops: [op({ status: 'partial', failedCount: 3, failures: [{ path: '/Rock/a.mp3', reason: 'Telegram: FORBIDDEN' }] })]
    });
    render(<DeleteProgressPanel />);

    expect(screen.getByText('Deleted with errors')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /3 entries could not be deleted/ }));
    expect(screen.getByText('/Rock/a.mp3')).toBeInTheDocument();
    expect(screen.getByText('Telegram: FORBIDDEN')).toBeInTheDocument();
    expect(screen.getByText('and 2 more')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(tracker.dismissDelete).toHaveBeenCalledWith('k1');
  });

  it('stacks several deletes and explains index-only deletes', () => {
    useDeletes.setState({
      ops: [
        op(),
        op({ key: 'k2', operationId: 'op2', progress: { ...op().progress!, indexOnly: true, phase: 'counting', counted: false } })
      ]
    });
    render(<DeleteProgressPanel />);
    expect(screen.getAllByTestId('delete-card')).toHaveLength(2);
    expect(screen.getByText('Counting...')).toBeInTheDocument();
    expect(screen.getByText(/Only the index is cleaned/)).toBeInTheDocument();
  });
});
