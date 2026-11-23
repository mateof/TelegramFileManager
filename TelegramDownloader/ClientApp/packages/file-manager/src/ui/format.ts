/** 1536 → `1.5 KB`. */
export function formatBytes(bytes: number | undefined, decimals = 1): string {
  if (bytes === undefined || !Number.isFinite(bytes) || bytes <= 0) return bytes === 0 ? '0 B' : '';
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
  const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  return `${(bytes / 1024 ** i).toFixed(i === 0 ? 0 : decimals)} ${units[i]}`;
}

const dateFormats = new Map<string, Intl.DateTimeFormat>();

/** Short localized date + time; empty for missing/invalid dates. */
export function formatDate(iso: string | undefined, locale?: string): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime()) || d.getFullYear() < 1971) return '';
  const key = locale ?? '';
  let f = dateFormats.get(key);
  if (!f) {
    f = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' });
    dateFormats.set(key, f);
  }
  return f.format(d);
}
