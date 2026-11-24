// base64url (RFC 4648 §5, no padding) <-> bytes, for the WebAuthn JSON forms.

/** Encodes an ArrayBuffer or a view (Uint8Array, DataView...) as base64url without padding. */
export function bufferToBase64url(data: ArrayBuffer | ArrayBufferView): string {
  // ArrayBuffer.isView also works across realms (password-manager extensions)
  const bytes = ArrayBuffer.isView(data) ? new Uint8Array(data.buffer, data.byteOffset, data.byteLength) : new Uint8Array(data);
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Decodes base64url (padding optional; plain base64 is accepted too). Throws on invalid input. */
export function base64urlToBuffer(value: string): ArrayBuffer {
  const normalized = value.trim().replace(/-/g, '+').replace(/_/g, '/');
  if (!/^[A-Za-z0-9+/]*={0,2}$/.test(normalized) || normalized.replace(/=+$/, '').length % 4 === 1) {
    throw new TypeError('Invalid base64url value');
  }
  const unpadded = normalized.replace(/=+$/, '');
  const padded = unpadded + '='.repeat((4 - (unpadded.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}
