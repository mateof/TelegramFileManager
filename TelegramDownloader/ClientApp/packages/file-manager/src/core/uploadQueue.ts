/**
 * Upload queue with concurrency, progress, cancel and retry. Framework free:
 * React reads it through `useSyncExternalStore(queue.subscribe, queue.getSnapshot)`.
 */

export type UploadStatus = 'queued' | 'uploading' | 'done' | 'error' | 'canceled';

export interface UploadItem {
  id: string;
  file: File;
  /** Destination folder. */
  targetPath: string;
  status: UploadStatus;
  /** Bytes sent. */
  loaded: number;
  total: number;
  error?: string;
  /** Provider id of the destination (a queue can be shared). */
  providerId?: string;
  /** Per item upload function (overrides the queue's `upload`). */
  run?: UploadFn;
}

export type UploadFn = (item: UploadItem, context: UploadTaskContext) => Promise<void>;

export interface UploadTaskContext {
  signal: AbortSignal;
  onProgress(loaded: number, total?: number): void;
}

export interface UploadQueueOptions {
  /** Parallel uploads (default 2). */
  concurrency?: number;
  /** Performs one upload (unless the item has its own `run`). Rejects to fail it; aborted signals mean canceled. */
  upload?: UploadFn;
  /** Called when an item finishes (successfully or not). */
  onSettled?(item: UploadItem): void;
  /** Called when the queue becomes idle after some work. */
  onIdle?(): void;
}

let nextId = 1;

function errorText(error: unknown): string {
  if (error instanceof Error) return error.message;
  return String(error);
}

export class UploadQueue {
  private items: UploadItem[] = [];
  private snapshot: readonly UploadItem[] = [];
  private readonly controllers = new Map<string, AbortController>();
  private readonly listeners = new Set<() => void>();
  private readonly settledListeners = new Set<(item: UploadItem) => void>();
  private concurrency: number;
  private readonly options: UploadQueueOptions;
  private worked = false;
  /** Running items removed by the user: dropped once they settle. */
  private readonly removed = new Set<string>();

  constructor(options: UploadQueueOptions = {}) {
    this.options = options;
    this.concurrency = Math.max(1, options.concurrency ?? 2);
  }

  /** Stable snapshot (a new array only when something changed). */
  getSnapshot = (): readonly UploadItem[] => this.snapshot;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  /** Listens to finished items (done, error, canceled). Returns the unsubscribe function. */
  onSettled(listener: (item: UploadItem) => void): () => void {
    this.settledListeners.add(listener);
    return () => {
      this.settledListeners.delete(listener);
    };
  }

  setConcurrency(n: number): void {
    this.concurrency = Math.max(1, n);
    this.pump();
  }

  /** Queues files for `targetPath`; returns their ids. */
  add(files: readonly File[], targetPath: string, providerId?: string, run?: UploadFn): string[] {
    const added = files.map<UploadItem>((file) => ({
      id: `u${nextId++}`,
      file,
      targetPath,
      providerId,
      run,
      status: 'queued',
      loaded: 0,
      total: file.size
    }));
    this.items = [...this.items, ...added];
    this.emit();
    this.pump();
    return added.map((i) => i.id);
  }

  cancel(id: string): void {
    const item = this.find(id);
    if (!item) return;
    if (item.status === 'uploading') {
      this.controllers.get(id)?.abort();
    } else if (item.status === 'queued') {
      this.update(id, { status: 'canceled' });
    }
  }

  cancelAll(): void {
    for (const item of this.items) this.cancel(item.id);
  }

  /** Re-queues a failed or canceled item. */
  retry(id: string): void {
    const item = this.find(id);
    if (!item || (item.status !== 'error' && item.status !== 'canceled')) return;
    this.update(id, { status: 'queued', loaded: 0, error: undefined });
    this.pump();
  }

  /** Removes a finished item from the list (running ones are canceled first). */
  remove(id: string): void {
    const item = this.find(id);
    if (item?.status === 'uploading') this.removed.add(id);
    this.cancel(id);
    this.items = this.items.filter((i) => i.id !== id || i.status === 'uploading');
    this.emit();
  }

  /** Removes every finished item (done, error, canceled). */
  clearFinished(): void {
    this.items = this.items.filter((i) => i.status === 'queued' || i.status === 'uploading');
    this.emit();
  }

  get active(): number {
    return this.items.filter((i) => i.status === 'uploading').length;
  }

  private find(id: string) {
    return this.items.find((i) => i.id === id);
  }

  private update(id: string, patch: Partial<UploadItem>) {
    this.items = this.items.map((i) => (i.id === id ? { ...i, ...patch } : i));
    this.emit();
  }

  private emit() {
    this.snapshot = this.items;
    this.listeners.forEach((l) => l());
  }

  private pump() {
    while (this.active < this.concurrency) {
      const next = this.items.find((i) => i.status === 'queued');
      if (!next) break;
      this.start(next);
    }
    if (this.worked && this.active === 0 && !this.items.some((i) => i.status === 'queued')) {
      this.worked = false;
      this.options.onIdle?.();
    }
  }

  private start(item: UploadItem) {
    const controller = new AbortController();
    this.controllers.set(item.id, controller);
    this.worked = true;
    this.update(item.id, { status: 'uploading', loaded: 0 });
    const context: UploadTaskContext = {
      signal: controller.signal,
      onProgress: (loaded, total) => {
        const current = this.find(item.id);
        if (!current || current.status !== 'uploading') return;
        this.update(item.id, { loaded, total: total ?? current.total });
      }
    };
    let task: Promise<void>;
    try {
      const current = this.find(item.id) ?? item;
      const fn = current.run ?? this.options.upload;
      task = fn ? Promise.resolve(fn(current, context)) : Promise.reject(new Error('No upload function'));
    } catch (error) {
      task = Promise.reject(error);
    }
    task.then(
      () => this.finish(item.id, { status: 'done' }),
      (error: unknown) =>
        this.finish(
          item.id,
          controller.signal.aborted ? { status: 'canceled' } : { status: 'error', error: errorText(error) }
        )
    );
  }

  private finish(id: string, patch: Partial<UploadItem>) {
    this.controllers.delete(id);
    const current = this.find(id);
    if (current) {
      const done = patch.status === 'done' ? { loaded: current.total } : {};
      if (this.removed.delete(id)) {
        this.items = this.items.filter((i) => i.id !== id);
        this.emit();
      } else {
        this.update(id, { ...patch, ...done });
      }
      const settled = { ...current, ...patch, ...done };
      this.options.onSettled?.(settled);
      this.settledListeners.forEach((l) => l(settled));
    }
    this.pump();
  }
}
