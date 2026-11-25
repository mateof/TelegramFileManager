import { describe, expect, it, vi } from 'vitest';
import { UploadQueue, type UploadTaskContext } from './uploadQueue';

interface Deferred {
  resolve(): void;
  reject(e: unknown): void;
  ctx: UploadTaskContext;
  name: string;
}

function makeQueue(concurrency = 2) {
  const running: Deferred[] = [];
  const queue = new UploadQueue({
    concurrency,
    upload: (item, ctx) =>
      new Promise<void>((resolve, reject) => {
        const d = { resolve, reject, ctx, name: item.file.name };
        running.push(d);
        ctx.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
      })
  });
  return { queue, running };
}

const file = (name: string, size = 100) => new File([new Uint8Array(size)], name);
const flush = () => new Promise((r) => setTimeout(r, 0));
const statuses = (q: UploadQueue) => q.getSnapshot().map((i) => `${i.file.name}:${i.status}`);

describe('UploadQueue', () => {
  it('respects the concurrency and starts the next one when a slot frees', async () => {
    const { queue, running } = makeQueue(2);
    queue.add([file('a'), file('b'), file('c')], '/');
    expect(statuses(queue)).toEqual(['a:uploading', 'b:uploading', 'c:queued']);
    running[0].resolve();
    await flush();
    expect(statuses(queue)).toEqual(['a:done', 'b:uploading', 'c:uploading']);
    expect(running).toHaveLength(3);
  });

  it('reports progress and marks done with the full size', async () => {
    const { queue, running } = makeQueue(1);
    const listener = vi.fn();
    queue.subscribe(listener);
    queue.add([file('a', 1000)], '/music');
    running[0].ctx.onProgress(400);
    expect(queue.getSnapshot()[0]).toMatchObject({ loaded: 400, total: 1000, targetPath: '/music' });
    running[0].resolve();
    await flush();
    expect(queue.getSnapshot()[0]).toMatchObject({ status: 'done', loaded: 1000 });
    expect(listener).toHaveBeenCalled();
  });

  it('cancels running and queued items, then retries them', async () => {
    const { queue, running } = makeQueue(1);
    const [a, b] = queue.add([file('a'), file('b')], '/');
    queue.cancel(b);
    expect(statuses(queue)).toEqual(['a:uploading', 'b:canceled']);
    queue.cancel(a);
    await flush();
    expect(statuses(queue)).toEqual(['a:canceled', 'b:canceled']);
    queue.retry(b);
    expect(statuses(queue)).toEqual(['a:canceled', 'b:uploading']);
    running[1].resolve();
    await flush();
    expect(statuses(queue)).toEqual(['a:canceled', 'b:done']);
  });

  it('marks failures with the error message and retries them', async () => {
    const { queue, running } = makeQueue(1);
    const settled = vi.fn();
    queue.onSettled(settled);
    const [a] = queue.add([file('a')], '/');
    running[0].reject(new Error('disk full'));
    await flush();
    expect(queue.getSnapshot()[0]).toMatchObject({ status: 'error', error: 'disk full' });
    expect(settled).toHaveBeenCalledWith(expect.objectContaining({ status: 'error' }));
    queue.retry(a);
    expect(queue.getSnapshot()[0].status).toBe('uploading');
  });

  it('removes items, clears the finished ones and uses per item run functions', async () => {
    const { queue, running } = makeQueue(2);
    const custom = vi.fn(() => Promise.resolve());
    queue.add([file('own')], '/', 'p1', custom);
    const [x] = queue.add([file('x')], '/');
    await flush();
    expect(custom).toHaveBeenCalledTimes(1);
    queue.remove(x);
    await flush();
    expect(statuses(queue)).toEqual(['own:done']);
    expect(running).toHaveLength(1);
    queue.clearFinished();
    expect(queue.getSnapshot()).toHaveLength(0);
  });
});
