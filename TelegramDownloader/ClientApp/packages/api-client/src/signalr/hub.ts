import {
  HttpTransportType,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection
} from '@microsoft/signalr';

/** Connection state as the UI cares about it. */
export type HubStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/** Message name → tuple of arguments the server sends with it. */
export type HubMessageMap = Record<string, unknown[]>;

export interface HubOptions {
  /** Server origin, empty for same-origin (default). */
  baseUrl?: string;
  /** API key for non-browser clients (sent as `access_token`); the web app relies on the cookie. */
  accessTokenFactory?: () => string | Promise<string>;
  /** SignalR client log level (default: warnings). */
  logLevel?: LogLevel;
  /**
   * Delays (ms) between reconnect attempts; the last one repeats forever, so the
   * connection comes back after a server restart however long it takes.
   */
  retryDelays?: number[];
}

/** Absolute hub URL (SignalR cannot resolve relative URLs outside a real browser, e.g. in tests). */
function hubUrl(path: string, baseUrl?: string): string {
  const base = (baseUrl ?? '').replace(/\/+$/, '');
  if (base) return `${base}${path}`;
  const origin = typeof location !== 'undefined' ? location.origin : '';
  return origin && origin !== 'null' ? new URL(path, origin).href : path;
}

const DEFAULT_DELAYS = [0, 1000, 2000, 5000, 10000, 20000, 30000];

/**
 * A SignalR connection with typed subscriptions, endless automatic reconnect
 * (initial start included) and a status observable.
 */
export class TypedHub<M extends HubMessageMap> {
  readonly connection: HubConnection;
  private statusValue: HubStatus = 'idle';
  private readonly statusListeners = new Set<(status: HubStatus) => void>();
  private readonly reconnectedListeners = new Set<() => void>();
  private readonly delays: number[];
  private stopped = true;
  private startPromise: Promise<void> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(path: string, options: HubOptions = {}) {
    this.delays = options.retryDelays?.length ? options.retryDelays : DEFAULT_DELAYS;
    const delays = this.delays;
    this.connection = new HubConnectionBuilder()
      .withUrl(hubUrl(path, options.baseUrl), {
        accessTokenFactory: options.accessTokenFactory,
        withCredentials: true,
        // The server's CSRF guard rejects cookie/LAN-authenticated POSTs under
        // /hubs (negotiate, long polling, SSE sends) without this header. The
        // WebSocket upgrade is a GET, so it does not need it.
        headers: { 'X-Requested-With': 'tfm' },
        transport: HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => delays[Math.min(ctx.previousRetryCount, delays.length - 1)]
      })
      .configureLogging(options.logLevel ?? LogLevel.Warning)
      .build();

    this.connection.onreconnecting(() => this.setStatus('reconnecting'));
    this.connection.onreconnected(() => {
      this.setStatus('connected');
      for (const l of this.reconnectedListeners) l();
    });
    this.connection.onclose(() => {
      // Automatic reconnect gave up (it never does with our policy) or stop() was called
      if (this.stopped) this.setStatus('disconnected');
      else this.scheduleStart(0);
    });
  }

  get status(): HubStatus {
    return this.statusValue;
  }

  /** Subscribe to a server message. Returns the unsubscribe function. */
  on<K extends keyof M & string>(message: K, handler: (...args: M[K]) => void): () => void {
    const h = handler as (...args: unknown[]) => void;
    this.connection.on(message, h);
    return () => this.connection.off(message, h);
  }

  /** Subscribe to status changes (called immediately with the current status). */
  onStatus(listener: (status: HubStatus) => void): () => void {
    this.statusListeners.add(listener);
    listener(this.statusValue);
    return () => this.statusListeners.delete(listener);
  }

  /** Called after every successful automatic reconnect (re-subscribe server side state here). */
  onReconnected(listener: () => void): () => void {
    this.reconnectedListeners.add(listener);
    return () => this.reconnectedListeners.delete(listener);
  }

  /** Invoke a hub method (waits for the connection if it is still starting). */
  async invoke<T = void>(method: string, ...args: unknown[]): Promise<T> {
    if (this.startPromise) await this.startPromise;
    return this.connection.invoke<T>(method, ...args);
  }

  /** Fire-and-forget hub method; ignored while disconnected. */
  send(method: string, ...args: unknown[]): void {
    if (this.connection.state === HubConnectionState.Connected) {
      void this.connection.send(method, ...args).catch(() => undefined);
    }
  }

  /** Starts the connection, retrying with backoff until it connects or stop() is called. */
  start(): Promise<void> {
    this.stopped = false;
    if (this.connection.state === HubConnectionState.Connected) return Promise.resolve();
    if (!this.startPromise) {
      this.startPromise = this.tryStart(0).finally(() => {
        this.startPromise = null;
      });
    }
    return this.startPromise;
  }

  async stop(): Promise<void> {
    this.stopped = true;
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = null;
    await this.connection.stop();
    this.setStatus('disconnected');
  }

  private async tryStart(attempt: number): Promise<void> {
    while (!this.stopped) {
      this.setStatus(attempt === 0 ? 'connecting' : 'reconnecting');
      try {
        if (this.connection.state === HubConnectionState.Disconnected) await this.connection.start();
        this.setStatus('connected');
        return;
      } catch {
        const delay = this.delays[Math.min(attempt + 1, this.delays.length - 1)];
        attempt++;
        await new Promise<void>((resolve) => {
          this.retryTimer = setTimeout(resolve, delay);
        });
      }
    }
  }

  private scheduleStart(delay: number) {
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(() => void this.start(), delay);
  }

  private setStatus(status: HubStatus) {
    if (status === this.statusValue) return;
    this.statusValue = status;
    for (const l of this.statusListeners) l(status);
  }
}
