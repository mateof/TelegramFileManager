import { toast } from '@tfm/ui';
import { t } from '@/i18n';
import type { AudioTrack } from './public';
import { currentTrack, displayTitle, nextIndex, previousIndex, type QueuedTrack } from './queue';
import { usePlayer, type TimeRange } from './store';

// How long before the end the next track is loaded into the standby element
const PREPARE_LEAD_SECONDS = 30;
// Without crossfade the next track starts this close to the end. timeupdate
// fires every ~250 ms, so the changeover lands right at the boundary with the
// old track still sounding (no silent gap).
const GAPLESS_LEAD_SECONDS = 0.3;
// Sleep timer: fade the volume out over the last seconds before pausing
const SLEEP_FADE_SECONDS = 10;
// Never crossfade tracks shorter than this many times the fade length
const MIN_CROSSFADE_TRACK_RATIO = 3;
const PERSIST_EVERY_MS = 5000;
const MEDIA_POSITION_EVERY_MS = 5000;

export interface NowPlayingInfo {
  qid?: string;
  title?: string;
  artist?: string;
  album?: string;
  cover?: string;
}

export interface PlayOptions {
  /** The change happens unattended (end of track): keep the old one sounding until the new one starts. */
  auto?: boolean;
  /** Crossfade length for this changeover. */
  crossfadeSeconds?: number;
  /** Start position in seconds. */
  startAt?: number;
  /** Load without starting (restore). */
  paused?: boolean;
}

/**
 * Playback engine of the global player (port of the TFM Player PWA engine,
 * without the DSP chain and the offline cache).
 *
 * Two <audio> elements take turns: while one plays, the other loads the next
 * track; at the end the next one starts BEFORE the current one is released.
 * The page never goes silent between tracks, which gives gapless changes and
 * crossfades, and keeps mobile browsers from suspending a page with the screen
 * off (and dropping the lock-screen controls).
 */
export class AudioEngine {
  readonly elements: [HTMLAudioElement, HTMLAudioElement];
  /** The element the UI, the media session and every control act on. */
  audio: HTMLAudioElement;
  /** The previous element while it fades/overlaps into the new one. */
  outgoing: HTMLAudioElement | null = null;
  private outgoingWrapped = false;
  // startedAt stays 0 until the incoming track actually sounds
  private crossfade: { durationMs: number; startedAt: number } | null = null;
  /** qid of the track loaded in the standby element. */
  private prepared: string | null = null;
  private preparingFor: string | null = null;
  /** qid loaded in the active element. */
  private loadedQid: string | null = null;

  private pendingAutoPlay = false;
  private playbackIntent = false;
  // A new source was assigned and has not started yet: its 'pause' is not a user pause
  private transitioning = false;
  private playToken = 0;

  private lastTimeupdatePos = 0;
  private seekSuppressUntil = 0;
  private lastPersistAt = 0;
  private lastMediaPositionAt = 0;
  private lastBufferedKey = '';

  private levels = new Map<HTMLAudioElement, number>();
  private healthTimer: ReturnType<typeof setInterval> | null = null;
  private errorRetryQid: string | null = null;
  private errorRetryCount = 0;
  private consecutiveErrorSkips = 0;
  /** Tags read from the file (override the queue entry on the lock screen). */
  private display: NowPlayingInfo = {};
  private readonly mediaSession = typeof navigator !== 'undefined' && 'mediaSession' in navigator;
  private readonly cleanups: (() => void)[] = [];
  private readonly unsubscribe: () => void;

  constructor(createElement: () => HTMLAudioElement = () => new Audio()) {
    const make = () => {
      const el = createElement();
      el.preload = 'auto';
      return el;
    };
    this.elements = [make(), make()];
    this.audio = this.elements[0];
    this.elements.forEach((el) => this.listen(el));
    this.listenDocument();
    if (this.mediaSession) this.setupMediaSession();
    this.applyVolume();
    // Volume/mute changes from the UI
    this.unsubscribe = usePlayer.subscribe((s, prev) => {
      if (s.volume !== prev.volume || s.muted !== prev.muted) this.applyVolume();
    });
  }

  private get store() {
    return usePlayer.getState();
  }

  private get standby(): HTMLAudioElement {
    return this.audio === this.elements[0] ? this.elements[1] : this.elements[0];
  }

  // ---------------------------------------------------------------- volume

  private master(): number {
    const { volume, muted } = this.store;
    return muted ? 0 : volume;
  }

  private applyLevel(el: HTMLAudioElement): void {
    const level = this.levels.get(el) ?? 1;
    el.volume = Math.max(0, Math.min(1, this.master() * level));
  }

  private applyVolume(): void {
    this.elements.forEach((el) => this.applyLevel(el));
  }

  private setLevel(el: HTMLAudioElement, level: number): void {
    this.levels.set(el, Math.max(0, Math.min(1, level)));
    this.applyLevel(el);
  }

  setVolume(volume: number): void {
    this.store.setVolume(volume);
  }

  toggleMute(): void {
    const s = this.store;
    if (s.muted || s.volume === 0) {
      if (s.volume === 0) s.setVolume(0.5);
      s.setMuted(false);
    } else s.setMuted(true);
  }

  // ---------------------------------------------------------------- events

  private listenDocument(): void {
    if (typeof document === 'undefined') return;
    const onVisibility = () => {
      if (document.hidden) {
        this.persistPosition(true);
        return;
      }
      if (this.pendingAutoPlay && this.audio.paused && this.audio.getAttribute('src')) {
        this.audio.play().catch(() => undefined);
      }
    };
    const onPageHide = () => this.persistPosition(true);
    document.addEventListener('visibilitychange', onVisibility);
    window.addEventListener('pagehide', onPageHide);
    this.cleanups.push(
      () => document.removeEventListener('visibilitychange', onVisibility),
      () => window.removeEventListener('pagehide', onPageHide)
    );
  }

  private listen(el: HTMLAudioElement): void {
    // Events of the element that is not (or no longer) active are noise
    const isActive = () => el === this.audio;
    const setStatus = (status: ReturnType<typeof usePlayer.getState>['status']) => usePlayer.setState({ status });

    el.addEventListener('loadstart', () => {
      if (isActive() && this.transitioning) setStatus('loading');
    });
    const onDuration = () => {
      if (!isActive()) return;
      if (Number.isFinite(el.duration) && el.duration > 0) {
        usePlayer.setState({ duration: el.duration });
        this.updatePositionState();
      }
    };
    el.addEventListener('loadedmetadata', onDuration);
    el.addEventListener('durationchange', onDuration);
    el.addEventListener('canplay', () => {
      if (isActive() && this.pendingAutoPlay && el.paused) el.play().catch(() => undefined);
    });
    el.addEventListener('play', () => {
      if (!isActive()) return;
      setStatus('playing');
      this.updateMediaSession();
      if (this.mediaSession) navigator.mediaSession.playbackState = 'playing';
    });
    el.addEventListener('playing', () => {
      if (!isActive()) return;
      this.pendingAutoPlay = false;
      this.transitioning = false;
      this.errorRetryCount = 0;
      this.consecutiveErrorSkips = 0;
      usePlayer.setState({ status: 'playing', error: null });
      // The new track is sounding: the old one can go (unless it is fading out)
      if (this.outgoing && !this.crossfade) this.releaseOutgoing();
      if (this.crossfade && this.crossfade.startedAt === 0) this.crossfade.startedAt = performance.now();
    });
    el.addEventListener('pause', () => {
      if (!isActive()) return;
      // A track change is not a pause: keep announcing 'playing' to the OS
      if (this.transitioning || el.ended) return;
      setStatus('paused');
      if (this.mediaSession) navigator.mediaSession.playbackState = 'paused';
      this.persistPosition(true);
    });
    el.addEventListener('ended', () => {
      if (isActive()) void this.handleTrackEnd();
    });
    el.addEventListener('timeupdate', () => {
      if (el === this.outgoing) {
        this.handleOutgoingTick(el);
        return;
      }
      if (!isActive()) return;
      this.handleTransitionTiming();
      this.handleSleepTimer();
      this.updateCrossfade();
      usePlayer.setState({ position: el.currentTime });
      this.updateBuffered();
      this.maybePrepareNext();
      this.persistPosition(false);
      const now = Date.now();
      if (now - this.lastMediaPositionAt >= MEDIA_POSITION_EVERY_MS) {
        this.lastMediaPositionAt = now;
        this.updatePositionState();
      }
    });
    el.addEventListener('progress', () => {
      if (isActive()) this.updateBuffered();
    });
    el.addEventListener('waiting', () => {
      if (isActive()) setStatus('buffering');
    });
    el.addEventListener('error', () => this.handleError(el));
  }

  private handleError(el: HTMLAudioElement): void {
    // Clearing a source fires a spurious error
    if (!el.getAttribute('src')) return;
    if (el !== this.audio) {
      // The standby element failed to load the next track: it loads on demand later
      if (el === this.standby) {
        this.prepared = null;
        this.preparingFor = null;
      }
      return;
    }
    const track = currentTrack(this.store);
    if (this.playbackIntent && track) {
      if (this.errorRetryQid !== track.qid) {
        this.errorRetryQid = track.qid;
        this.errorRetryCount = 0;
      }
      if (this.errorRetryCount < 2) {
        this.errorRetryCount++;
        const position = el.currentTime;
        setTimeout(() => {
          if (this.playbackIntent && currentTrack(this.store)?.qid === track.qid) void this.load(track, { startAt: position });
        }, 1000);
        return;
      }
      const s = this.store;
      if (this.consecutiveErrorSkips < 5 && nextIndex(s) >= 0 && s.queue.length > 1) {
        this.consecutiveErrorSkips++;
        toast.error(t('Could not play "{title}"', { title: displayTitle(track) }));
        void this.next({ auto: true });
        return;
      }
    }
    this.releaseOutgoing();
    this.transitioning = false;
    usePlayer.setState({ status: 'error', error: t('The track could not be played') });
  }

  // ---------------------------------------------------------------- media session

  private setupMediaSession(): void {
    const handlers: [MediaSessionAction, MediaSessionActionHandler][] = [
      ['play', () => this.play()],
      ['pause', () => this.pause()],
      ['stop', () => this.pause()],
      ['previoustrack', () => void this.previous()],
      ['nexttrack', () => void this.next()],
      ['seekto', (d) => d.seekTime !== undefined && this.seek(d.seekTime)],
      ['seekbackward', (d) => this.seekBy(-(d.seekOffset || 10))],
      ['seekforward', (d) => this.seekBy(d.seekOffset || 10)]
    ];
    for (const [action, handler] of handlers) {
      try {
        navigator.mediaSession.setActionHandler(action, handler);
      } catch {
        // not supported by this browser
      }
    }
  }

  private updateMediaSession(): void {
    if (!this.mediaSession || typeof MediaMetadata === 'undefined') return;
    const track = currentTrack(this.store);
    if (!track) return;
    const d = this.display.qid === track.qid ? this.display : {};
    const art = d.cover ?? track.coverUrl;
    try {
      navigator.mediaSession.metadata = new MediaMetadata({
        title: d.title || displayTitle(track),
        artist: d.artist || track.artist || track.source || '',
        album: d.album || track.album || '',
        artwork: art ? [{ src: art, sizes: '512x512' }] : []
      });
    } catch {
      // ignore
    }
    this.updatePositionState();
  }

  /** Title/artist/cover read from the file tags of track `qid`: shown on the lock screen. */
  setNowPlaying(info: NowPlayingInfo): void {
    const d = this.display;
    if (d.qid === info.qid && d.title === info.title && d.artist === info.artist && d.album === info.album && d.cover === info.cover) return;
    this.display = info;
    this.updateMediaSession();
  }

  private updatePositionState(): void {
    if (!this.mediaSession) return;
    const { duration, currentTime } = this.audio;
    if (!Number.isFinite(duration) || duration <= 0 || duration > 86400 * 2) return;
    if (!Number.isFinite(currentTime) || currentTime < 0) return;
    try {
      navigator.mediaSession.setPositionState?.({
        duration,
        playbackRate: this.audio.playbackRate || 1,
        position: Math.min(currentTime, duration)
      });
    } catch {
      // ignore
    }
  }

  private updateBuffered(): void {
    const b = this.audio.buffered;
    if (!b) return;
    const ranges: TimeRange[] = [];
    for (let i = 0; i < b.length; i++) ranges.push({ start: b.start(i), end: b.end(i) });
    const key = ranges.map((r) => `${r.start.toFixed(1)}-${r.end.toFixed(1)}`).join(',');
    if (key === this.lastBufferedKey) return;
    this.lastBufferedKey = key;
    usePlayer.setState({ buffered: ranges });
  }

  // ---------------------------------------------------------------- elements

  private assign(el: HTMLAudioElement, url: string): void {
    el.loop = false;
    el.src = url;
  }

  /** Stop an element and let go of its source. */
  private releaseElement(el: HTMLAudioElement): void {
    el.loop = false;
    el.pause();
    if (el.getAttribute('src')) {
      el.removeAttribute('src');
      el.load();
    }
    this.setLevel(el, 1);
  }

  private releaseOutgoing(): void {
    const el = this.outgoing;
    this.outgoing = null;
    this.outgoingWrapped = false;
    this.crossfade = null;
    if (el && el !== this.audio) this.releaseElement(el);
    this.setLevel(this.audio, 1);
  }

  // The outgoing element still sounds: drive the fade, or once it wraps around
  // (next track still loading) keep it nearly silent so it holds the audio session
  private handleOutgoingTick(el: HTMLAudioElement): void {
    if (this.crossfade) {
      if (el.currentTime < 2 && this.crossfade.startedAt > 0) {
        this.releaseOutgoing();
        return;
      }
      this.updateCrossfade();
      return;
    }
    if (!this.outgoingWrapped && el.currentTime < 2) {
      this.outgoingWrapped = true;
      this.setLevel(el, 0.03);
    }
  }

  private updateCrossfade(): void {
    if (!this.crossfade || !this.outgoing || this.crossfade.startedAt === 0) return;
    const progress = Math.min(1, (performance.now() - this.crossfade.startedAt) / this.crossfade.durationMs);
    // Equal-power curves keep the loudness steady
    this.setLevel(this.outgoing, Math.cos((progress * Math.PI) / 2));
    this.setLevel(this.audio, Math.sin((progress * Math.PI) / 2));
    if (progress >= 1) this.releaseOutgoing();
  }

  private crossfadeSecondsFor(duration: number): number {
    const seconds = this.store.crossfadeSeconds;
    if (seconds <= 0 || !Number.isFinite(duration)) return 0;
    return duration >= seconds * MIN_CROSSFADE_TRACK_RATIO ? seconds : 0;
  }

  private upcoming(): QueuedTrack | null {
    const s = this.store;
    const i = nextIndex(s, true);
    return i >= 0 && i !== s.index ? s.queue[i] : null;
  }

  // Load the next track into the standby element while this one plays
  private maybePrepareNext(): void {
    const s = this.store;
    if (s.repeat === 'one' || this.transitioning || this.outgoing) return;
    const duration = this.audio.duration;
    if (!Number.isFinite(duration)) return;
    const lead = Math.max(PREPARE_LEAD_SECONDS, this.crossfadeSecondsFor(duration) + 15);
    if (duration - this.audio.currentTime > lead) return;
    const track = this.upcoming();
    if (!track || this.preparingFor === track.qid) return;
    this.preparingFor = track.qid;
    const el = this.standby;
    this.releaseElement(el);
    this.assign(el, track.url);
    el.load();
    this.prepared = track.qid;
  }

  // When the next track starts: early enough for the crossfade, or right at the
  // end (gapless) when it is loaded. If it is not ready, the loop wrap below
  // still triggers the change without silence.
  private handleTransitionTiming(): void {
    const el = this.audio;
    const pos = el.currentTime;
    const prev = this.lastTimeupdatePos;
    this.lastTimeupdatePos = pos;
    const duration = el.duration;
    if (!Number.isFinite(duration) || duration <= 0) return;

    const s = this.store;
    const hasFollowUp = !s.sleepAtEndOfTrack && nextIndex(s, true) >= 0;
    // End-guard: while something follows, the element loops, so 'ended' (silence) never fires
    if (el.loop !== hasFollowUp) el.loop = hasFollowUp;
    if (s.repeat === 'one' || this.transitioning || this.outgoing) return;

    if (s.sleepAtEndOfTrack && duration - pos <= GAPLESS_LEAD_SECONDS) {
      this.finishSleep();
      return;
    }
    if (!hasFollowUp) return;

    const next = this.upcoming();
    const nextReady = !!next && this.prepared === next.qid;
    const fade = this.crossfadeSecondsFor(duration);
    const remaining = duration - pos;
    if (nextReady && remaining <= Math.max(fade, GAPLESS_LEAD_SECONDS)) {
      this.transitioning = true;
      void this.next({ auto: true, crossfadeSeconds: fade ? Math.max(remaining, 1) : 0 });
      return;
    }

    // Fallback: the loop wrapped before the next track was ready
    if (Date.now() < this.seekSuppressUntil) return;
    const wrapped = (prev > duration - 10 && pos < 3 && prev - pos > 2) || prev - pos > Math.max(10, duration * 0.5);
    if (wrapped) {
      this.transitioning = true;
      void this.next({ auto: true });
    }
  }

  // ---------------------------------------------------------------- playback

  /** Load `track` (the store's current track) into an element and start it. */
  private async load(track: QueuedTrack, options: PlayOptions = {}): Promise<void> {
    const token = ++this.playToken;
    if (this.outgoing) this.releaseOutgoing();
    usePlayer.setState({ status: options.paused ? 'paused' : 'loading', error: null, position: options.startAt ?? 0, buffered: [] });

    const old = this.audio;
    const oldIsSounding = !!old.getAttribute('src') && !old.paused && !old.ended;
    // Keep the old track sounding until the new one starts when the change is
    // unattended (end of track, lock screen buttons, screen off)
    const overlap = !options.paused && oldIsSounding && (options.auto || (typeof document !== 'undefined' && document.hidden));

    const target = this.standby;
    if (!(this.prepared === track.qid && target.getAttribute('src'))) {
      this.releaseElement(target);
      this.assign(target, track.url);
    }
    this.prepared = null;
    this.preparingFor = null;
    if (token !== this.playToken) return;

    this.transitioning = !options.paused;
    this.audio = target;
    this.loadedQid = track.qid;
    this.lastBufferedKey = '';
    this.lastTimeupdatePos = 0;
    this.seekSuppressUntil = Date.now() + 2000;

    if (overlap) {
      this.outgoing = old;
      this.outgoingWrapped = false;
      // Loop until released: if the new source is slow, the old one must not reach 'ended'
      old.loop = true;
      const fade = options.crossfadeSeconds ?? 0;
      this.crossfade = fade > 0 ? { durationMs: fade * 1000, startedAt: 0 } : null;
      this.setLevel(target, fade > 0 ? 0 : 1);
    } else {
      this.setLevel(target, 1);
      if (old !== target) this.releaseElement(old);
    }

    const startAt = options.startAt ?? 0;
    if (startAt > 0) {
      const apply = () => {
        if (this.audio === target) target.currentTime = startAt;
      };
      if (target.readyState >= 1) apply();
      else target.addEventListener('loadedmetadata', apply, { once: true });
    }

    this.updateMediaSession();
    if (options.paused) {
      this.pendingAutoPlay = false;
      this.playbackIntent = false;
      target.load();
      return;
    }

    this.pendingAutoPlay = true;
    this.playbackIntent = true;
    this.startHealthMonitor();
    try {
      await target.play();
    } catch (error) {
      const name = error instanceof DOMException ? error.name : '';
      if (name === 'NotAllowedError') {
        // Autoplay blocked: show it paused, the user presses play
        this.pendingAutoPlay = false;
        this.playbackIntent = false;
        this.transitioning = false;
        this.releaseOutgoing();
        usePlayer.setState({ status: 'paused' });
      } else if (name !== 'AbortError') {
        this.transitioning = false;
        this.releaseOutgoing();
        usePlayer.setState({ status: 'error', error: t('The track could not be played') });
      }
    }
  }

  /** Replace the queue and start playing. */
  async playQueue(tracks: AudioTrack[], startIndex = 0): Promise<void> {
    this.store.replaceQueue(tracks, startIndex);
    const track = currentTrack(this.store);
    if (track) await this.load(track);
  }

  /** Add to the queue; starts playing when the player was empty. */
  enqueue(tracks: AudioTrack[], position: 'next' | 'end'): void {
    const wasEmpty = this.store.queue.length === 0;
    this.store.enqueue(tracks, position);
    this.invalidatePrepared();
    if (wasEmpty) {
      const track = currentTrack(this.store);
      if (track) void this.load(track);
    } else {
      toast.success(
        position === 'next'
          ? t('{count} tracks will play next', { count: tracks.length })
          : t('{count} tracks added to the queue', { count: tracks.length })
      );
    }
  }

  async playAt(index: number, options: PlayOptions = {}): Promise<void> {
    const s = this.store;
    if (index < 0 || index >= s.queue.length) return;
    s.setIndex(index);
    await this.load(s.queue[index], options);
  }

  play(): void {
    const track = currentTrack(this.store);
    if (!track) return;
    // Restored session (nothing loaded yet) or a different track than the loaded one
    if (!this.audio.getAttribute('src') || this.loadedQid !== track.qid) {
      void this.load(track, { startAt: this.store.savedPosition || this.store.position || 0 });
      return;
    }
    if (this.store.status === 'error') {
      void this.load(track, { startAt: this.audio.currentTime });
      return;
    }
    this.pendingAutoPlay = true;
    this.playbackIntent = true;
    this.startHealthMonitor();
    this.audio.play().catch(() => undefined);
  }

  pause(): void {
    this.pendingAutoPlay = false;
    this.playbackIntent = false;
    this.transitioning = false;
    this.stopHealthMonitor();
    this.releaseOutgoing();
    this.audio.pause();
  }

  toggle(): void {
    const status = this.store.status;
    if (status === 'playing' || status === 'buffering' || (status === 'loading' && this.playbackIntent)) this.pause();
    else this.play();
  }

  get isPlaying(): boolean {
    return !this.audio.paused && !this.audio.ended;
  }

  async next(options: PlayOptions = {}): Promise<void> {
    const s = this.store;
    if (s.queue.length === 0) return;
    const i = nextIndex(s, !!options.auto);
    if (i < 0) {
      // End of the queue: stop on the last track, at its start
      this.transitioning = false;
      this.pause();
      this.audio.currentTime = 0;
      usePlayer.setState({ position: 0, savedPosition: 0, status: 'paused' });
      return;
    }
    if (i === s.index && options.auto) {
      this.seek(0);
      this.play();
      return;
    }
    await this.playAt(i, { auto: options.auto, crossfadeSeconds: options.crossfadeSeconds });
  }

  /** Back to the start of the track, or to the previous one in the first 3 seconds. */
  async previous(): Promise<void> {
    const s = this.store;
    if (s.queue.length === 0) return;
    if (this.audio.currentTime > 3) {
      this.seek(0);
      return;
    }
    const i = previousIndex(s);
    if (i < 0) this.seek(0);
    else await this.playAt(i);
  }

  seek(position: number): void {
    if (!Number.isFinite(position)) return;
    const duration = this.audio.duration;
    if (!this.audio.getAttribute('src') || !Number.isFinite(duration)) {
      // Restored but not loaded yet: remember where to start
      const p = Math.max(0, position);
      usePlayer.setState({ position: p, savedPosition: p });
      return;
    }
    this.seekSuppressUntil = Date.now() + 2000;
    if (this.outgoing) this.releaseOutgoing();
    const p = Math.max(0, Math.min(position, duration));
    this.audio.currentTime = p;
    usePlayer.setState({ position: p });
    this.updatePositionState();
  }

  seekBy(delta: number): void {
    const base = this.audio.getAttribute('src') ? this.audio.currentTime : this.store.position;
    this.seek(base + delta);
  }

  private async handleTrackEnd(): Promise<void> {
    const s = this.store;
    if (s.sleepAtEndOfTrack) {
      this.finishSleep();
      return;
    }
    if (s.repeat === 'one') {
      this.seek(0);
      this.pendingAutoPlay = true;
      await this.audio.play().catch(() => undefined);
      return;
    }
    await this.next({ auto: true });
  }

  /** Remove a queue entry; if it was playing, the following track takes over. */
  remove(qid: string): void {
    const wasPlaying = this.playbackIntent;
    const removedCurrent = this.store.remove(qid);
    this.invalidatePrepared();
    if (!removedCurrent) return;
    const track = currentTrack(this.store);
    if (!track) this.stop();
    else void this.load(track, { paused: !wasPlaying });
  }

  move(from: number, to: number): void {
    this.store.move(from, to);
    this.invalidatePrepared();
  }

  toggleShuffle(): void {
    this.store.toggleShuffle();
    this.invalidatePrepared();
  }

  cycleRepeat(): void {
    this.store.cycleRepeat();
    this.invalidatePrepared();
  }

  /** The queue changed: the standby element may hold the wrong track. */
  private invalidatePrepared(): void {
    const next = this.upcoming();
    if (this.prepared && this.prepared !== next?.qid && !this.outgoing) {
      this.releaseElement(this.standby);
      this.prepared = null;
    }
    if (this.preparingFor !== next?.qid) this.preparingFor = null;
  }

  /** Stop and empty the queue (closes the player). */
  clearQueue(): void {
    this.stop();
    this.store.clear();
  }

  stop(): void {
    this.releaseOutgoing();
    for (const el of this.elements) this.releaseElement(el);
    this.pendingAutoPlay = false;
    this.playbackIntent = false;
    this.transitioning = false;
    this.prepared = null;
    this.preparingFor = null;
    this.loadedQid = null;
    this.lastTimeupdatePos = 0;
    this.lastBufferedKey = '';
    this.stopHealthMonitor();
    usePlayer.setState({ status: this.store.queue.length ? 'paused' : 'idle', buffered: [], position: 0, savedPosition: 0 });
    if (this.mediaSession) navigator.mediaSession.playbackState = 'none';
  }

  // ---------------------------------------------------------------- sleep timer

  /** Pause after `minutes` (fading out), or at the end of the current track. */
  setSleepTimer(minutes: number | 'track'): void {
    if (minutes === 'track') this.store.setSleep(null, true);
    else this.store.setSleep(Date.now() + minutes * 60_000, false);
  }

  cancelSleepTimer(): void {
    this.store.setSleep(null, false);
    this.setLevel(this.audio, 1);
  }

  private handleSleepTimer(): void {
    const { sleepEndsAt } = this.store;
    if (!sleepEndsAt) return;
    const remaining = (sleepEndsAt - Date.now()) / 1000;
    if (remaining <= 0) this.finishSleep();
    else if (remaining <= SLEEP_FADE_SECONDS && !this.outgoing) this.setLevel(this.audio, remaining / SLEEP_FADE_SECONDS);
  }

  private finishSleep(): void {
    this.store.setSleep(null, false);
    this.pause();
    this.audio.loop = false;
    this.setLevel(this.audio, 1);
    toast(t('Sleep timer: playback paused'));
  }

  // ---------------------------------------------------------------- health / persistence

  private startHealthMonitor(): void {
    if (this.healthTimer !== null || typeof window === 'undefined') return;
    this.healthTimer = setInterval(() => {
      if (!this.playbackIntent) {
        this.stopHealthMonitor();
        return;
      }
      // The sleep timer must fire even when timeupdate is throttled
      this.handleSleepTimer();
      const el = this.audio;
      if (el.paused && this.pendingAutoPlay && el.getAttribute('src')) el.play().catch(() => undefined);
    }, 3000);
  }

  private stopHealthMonitor(): void {
    if (this.healthTimer !== null) {
      clearInterval(this.healthTimer);
      this.healthTimer = null;
    }
  }

  private persistPosition(force: boolean): void {
    const now = Date.now();
    if (!force && now - this.lastPersistAt < PERSIST_EVERY_MS) return;
    this.lastPersistAt = now;
    if (!this.audio.getAttribute('src')) return;
    const p = this.audio.currentTime;
    if (Number.isFinite(p)) usePlayer.setState({ savedPosition: p });
  }

  /** Tests only: detach every listener. */
  dispose(): void {
    this.stop();
    this.unsubscribe();
    this.cleanups.splice(0).forEach((c) => c());
  }
}

let engine: AudioEngine | null = null;

/** The app-wide engine (created on first use). */
export function getEngine(): AudioEngine {
  engine ??= new AudioEngine();
  return engine;
}
