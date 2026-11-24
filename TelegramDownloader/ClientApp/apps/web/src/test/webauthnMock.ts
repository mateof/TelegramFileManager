// Test double of the browser's WebAuthn API (jsdom has none).
import { vi } from 'vitest';

const enc = (s: string) => new TextEncoder().encode(s).buffer as ArrayBuffer;

/** A credential as `navigator.credentials.create/get` returns it, without `toJSON` (like some extensions). */
export function fakeCredential(kind: 'create' | 'get', id = 'cred-1') {
  const response =
    kind === 'create'
      ? { clientDataJSON: enc('{"type":"webauthn.create"}'), attestationObject: enc('att'), getTransports: () => ['hybrid', 'internal'] }
      : { clientDataJSON: enc('{"type":"webauthn.get"}'), authenticatorData: enc('auth'), signature: enc('sig'), userHandle: enc('user') };
  return { id, rawId: enc(id), type: 'public-key', authenticatorAttachment: 'cross-platform', response, getClientExtensionResults: () => ({}) };
}

export function installWebAuthn({ secure = true, conditional = false, present = true }: { secure?: boolean; conditional?: boolean; present?: boolean } = {}) {
  const create = vi.fn<(options: CredentialCreationOptions) => Promise<unknown>>();
  const get = vi.fn<(options: CredentialRequestOptions) => Promise<unknown>>();
  const isConditionalMediationAvailable = vi.fn(async () => conditional);
  Object.defineProperty(window, 'isSecureContext', { value: secure, configurable: true });
  if (present) {
    const FakePublicKeyCredential = function PublicKeyCredential() {} as unknown as Record<string, unknown>;
    FakePublicKeyCredential.isConditionalMediationAvailable = isConditionalMediationAvailable;
    Object.defineProperty(window, 'PublicKeyCredential', { value: FakePublicKeyCredential, configurable: true, writable: true });
    Object.defineProperty(navigator, 'credentials', { value: { create, get }, configurable: true });
  }
  return {
    create,
    get,
    isConditionalMediationAvailable,
    restore() {
      delete (window as unknown as Record<string, unknown>).isSecureContext;
      delete (window as unknown as Record<string, unknown>).PublicKeyCredential;
      delete (navigator as unknown as Record<string, unknown>).credentials;
    }
  };
}

/** A promise that rejects with AbortError when the signal aborts (a pending conditional request). */
export function pendingUntilAborted(signal?: AbortSignal | null): Promise<never> {
  return new Promise((_, reject) => {
    signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
  });
}
