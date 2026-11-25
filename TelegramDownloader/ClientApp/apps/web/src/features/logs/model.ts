import type { BadgeTone } from '@tfm/ui';
import type { LogQuery } from '@tfm/api-client';

export const LOG_LEVELS = ['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal'] as const;

export interface LogFilters {
  level: string;
  logger: string;
  version: string;
  /** `datetime-local` value (local time) or ''. */
  from: string;
  to: string;
  search: string;
}

export const EMPTY_FILTERS: LogFilters = { level: '', logger: '', version: '', from: '', to: '', search: '' };

const FILTER_KEYS = Object.keys(EMPTY_FILTERS) as (keyof LogFilters)[];

export function filtersFromParams(params: URLSearchParams): LogFilters {
  const f = { ...EMPTY_FILTERS };
  for (const k of FILTER_KEYS) f[k] = params.get(k) ?? '';
  return f;
}

export function filtersToParams(f: LogFilters): Record<string, string> {
  const out: Record<string, string> = {};
  for (const k of FILTER_KEYS) if (f[k]) out[k] = f[k];
  return out;
}

/** `datetime-local` value → ISO (UTC); '' or invalid → undefined. */
export function localToIso(value: string): string | undefined {
  if (!value) return undefined;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
}

/** Date → `datetime-local` value in local time. */
export function toLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** `datetime-local` value of N minutes ago. */
export function minutesAgoInput(minutes: number): string {
  return toLocalInput(new Date(Date.now() - minutes * 60_000));
}

/** Query of `GET /api/v1/system/logs` for the filters (newest first). */
export function buildLogQuery(f: LogFilters, page: number, pageSize: number): LogQuery {
  const q: LogQuery = { page, pageSize, sortDescending: true };
  if (f.level) q.level = f.level;
  if (f.logger) q.logger = f.logger;
  if (f.version) q.version = f.version;
  if (f.search.trim()) q.search = f.search.trim();
  const from = localToIso(f.from);
  const to = localToIso(f.to);
  if (from) q.fromDate = from;
  if (to) q.toDate = to;
  return q;
}

/** `TelegramDownloader.Data.TelegramService` → `TelegramService`. */
export function shortLogger(logger: string | null | undefined): string {
  if (!logger) return '';
  const parts = logger.split('.');
  return parts[parts.length - 1] ?? logger;
}

export function levelTone(level: string | null | undefined): BadgeTone {
  switch (level) {
    case 'Fatal':
    case 'Error':
      return 'danger';
    case 'Warning':
      return 'warning';
    case 'Information':
      return 'info';
    case 'Debug':
    case 'Verbose':
      return 'neutral';
    default:
      return 'neutral';
  }
}
