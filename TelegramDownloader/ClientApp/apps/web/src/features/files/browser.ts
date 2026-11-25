import { ApiError, ApiErrorCodes, type ApiEnvelope } from '@tfm/api-client';
import type { UploadOptions } from '@tfm/file-manager';

/**
 * Multipart upload with progress (fetch has no upload progress, so this uses
 * XMLHttpRequest). Same conventions as the API client: same-origin cookie, the
 * `X-Requested-With: tfm` CSRF header, envelope errors as ApiError.
 */
export function xhrUpload(url: string, form: FormData, file: File, options: UploadOptions = {}): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', url);
    xhr.withCredentials = true;
    xhr.setRequestHeader('X-Requested-With', 'tfm');
    xhr.setRequestHeader('Accept', 'application/json');
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) options.onProgress?.({ file, loaded: Math.min(e.loaded, file.size), total: file.size });
    };
    xhr.onload = () => {
      let envelope: ApiEnvelope<unknown> | null = null;
      try {
        envelope = xhr.responseText ? (JSON.parse(xhr.responseText) as ApiEnvelope<unknown>) : null;
      } catch {
        envelope = null;
      }
      if (xhr.status >= 200 && xhr.status < 300 && (envelope?.success ?? true)) {
        resolve(envelope?.data);
        return;
      }
      const e = envelope?.error;
      reject(new ApiError(e?.code ?? ApiErrorCodes.InternalError, e?.message || envelope?.message || xhr.statusText || `HTTP ${xhr.status}`, xhr.status, e?.detail));
    };
    xhr.onerror = () => reject(new ApiError(ApiErrorCodes.NetworkError, 'Network error', 0));
    xhr.onabort = () => reject(new ApiError(ApiErrorCodes.Aborted, 'The request was aborted', 0));
    if (options.signal) {
      if (options.signal.aborted) {
        reject(new ApiError(ApiErrorCodes.Aborted, 'The request was aborted', 0));
        return;
      }
      options.signal.addEventListener('abort', () => xhr.abort(), { once: true });
    }
    xhr.send(form);
  });
}

/** Multipart body with the `file` part and the optional `path` field. */
export function uploadBody(file: File, path?: string): FormData {
  const form = new FormData();
  form.append('file', file, file.name);
  if (path) form.append('path', path);
  return form;
}

/** Starts browser downloads of URLs (spaced a little so browsers do not drop them). */
export async function downloadUrls(items: { url: string; name: string }[]): Promise<void> {
  for (const [i, item] of items.entries()) {
    if (i > 0) await new Promise((r) => setTimeout(r, 350));
    const a = document.createElement('a');
    a.href = item.url;
    a.download = item.name;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    a.remove();
  }
}

/** Saves a Blob as a file. */
export function saveBlob(blob: Blob, name: string): void {
  const url = URL.createObjectURL(blob);
  void downloadUrls([{ url, name }]).finally(() => setTimeout(() => URL.revokeObjectURL(url), 10_000));
}

/** Copies text, with a fallback for non-secure origins (plain http on the LAN). */
export async function copyText(text: string): Promise<void> {
  if (navigator.clipboard && window.isSecureContext) {
    await navigator.clipboard.writeText(text);
    return;
  }
  const area = document.createElement('textarea');
  area.value = text;
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.appendChild(area);
  area.select();
  const ok = document.execCommand('copy');
  area.remove();
  if (!ok) throw new Error('copy failed');
}

/** Absolute URL (the API sometimes answers relative ones). */
export function absoluteUrl(url: string): string {
  try {
    return new URL(url, window.location.origin).toString();
  } catch {
    return url;
  }
}
