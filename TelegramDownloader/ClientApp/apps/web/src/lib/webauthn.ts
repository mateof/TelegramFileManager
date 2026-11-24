// Thin WebAuthn layer for passkeys: feature detection, JSON <-> binary options,
// credential serialisation and error classification. No dependency: uses the
// browser's parse*FromJSON / toJSON when present, with a manual fallback
// (older browsers, and password-manager extensions such as Bitwarden that
// replace navigator.credentials with their own objects).
import { base64urlToBuffer, bufferToBase64url } from './base64url';

type Json = Record<string, unknown>;

export type PasskeySupport = 'ok' | 'insecure' | 'unsupported';

/** `insecure`: not HTTPS nor localhost; `unsupported`: the browser has no WebAuthn. */
export function passkeySupport(): PasskeySupport {
  if (typeof window === 'undefined') return 'unsupported';
  if (!window.isSecureContext) return 'insecure';
  if (typeof window.PublicKeyCredential !== 'function' || !navigator.credentials) return 'unsupported';
  return 'ok';
}

interface PublicKeyCredentialStatics {
  isConditionalMediationAvailable?: () => Promise<boolean>;
  parseCreationOptionsFromJSON?: (json: Json) => PublicKeyCredentialCreationOptions;
  parseRequestOptionsFromJSON?: (json: Json) => PublicKeyCredentialRequestOptions;
}

function statics(): PublicKeyCredentialStatics {
  return (window.PublicKeyCredential ?? {}) as unknown as PublicKeyCredentialStatics;
}

/** Whether passkey autofill (`mediation: 'conditional'`) can be used. */
export async function conditionalMediationAvailable(): Promise<boolean> {
  if (passkeySupport() !== 'ok') return false;
  const check = statics().isConditionalMediationAvailable;
  if (typeof check !== 'function') return false;
  try {
    return (await check.call(window.PublicKeyCredential)) === true;
  } catch {
    return false;
  }
}

function decodeDescriptors(list: unknown): PublicKeyCredentialDescriptor[] | undefined {
  if (!Array.isArray(list)) return undefined;
  return list.map((d: Json) => ({ ...(d as object), id: base64urlToBuffer(String(d.id)) }) as PublicKeyCredentialDescriptor);
}

/** `PublicKeyCredentialCreationOptionsJSON` → options for `navigator.credentials.create`. */
export function toCreationOptions(json: Json): PublicKeyCredentialCreationOptions {
  const parse = statics().parseCreationOptionsFromJSON;
  if (typeof parse === 'function') {
    try {
      return parse.call(window.PublicKeyCredential, json);
    } catch {
      // fall back to the manual conversion
    }
  }
  const user = (json.user ?? {}) as Json;
  return {
    ...(json as object),
    challenge: base64urlToBuffer(String(json.challenge)),
    user: { ...(user as object), id: base64urlToBuffer(String(user.id)) },
    excludeCredentials: decodeDescriptors(json.excludeCredentials) ?? []
  } as PublicKeyCredentialCreationOptions;
}

/** `PublicKeyCredentialRequestOptionsJSON` → options for `navigator.credentials.get`. */
export function toRequestOptions(json: Json): PublicKeyCredentialRequestOptions {
  const parse = statics().parseRequestOptionsFromJSON;
  if (typeof parse === 'function') {
    try {
      return parse.call(window.PublicKeyCredential, json);
    } catch {
      // fall back to the manual conversion
    }
  }
  return {
    ...(json as object),
    challenge: base64urlToBuffer(String(json.challenge)),
    allowCredentials: decodeDescriptors(json.allowCredentials) ?? []
  } as PublicKeyCredentialRequestOptions;
}

interface CredentialLike {
  id: string;
  rawId: ArrayBuffer;
  type: string;
  authenticatorAttachment?: string | null;
  response: {
    clientDataJSON: ArrayBuffer;
    attestationObject?: ArrayBuffer;
    getTransports?: () => string[];
    authenticatorData?: ArrayBuffer;
    signature?: ArrayBuffer;
    userHandle?: ArrayBuffer | null;
  };
  toJSON?: () => unknown;
}

function looksLikeJson(value: unknown): value is Json {
  if (!value || typeof value !== 'object') return false;
  const v = value as Json;
  const response = v.response as Json | undefined;
  return typeof v.rawId === 'string' && !!response && typeof response.clientDataJSON === 'string';
}

/** `PublicKeyCredential` → its JSON form (what the server expects). */
export function credentialToJSON(credential: Credential): Json {
  const c = credential as unknown as CredentialLike;
  if (typeof c.toJSON === 'function') {
    try {
      const json = c.toJSON();
      if (looksLikeJson(json)) return json;
    } catch {
      // fall back to the manual serialisation
    }
  }
  const r = c.response;
  const response: Json = { clientDataJSON: bufferToBase64url(r.clientDataJSON) };
  if (r.attestationObject) {
    response.attestationObject = bufferToBase64url(r.attestationObject);
    let transports: string[] = [];
    try {
      transports = typeof r.getTransports === 'function' ? r.getTransports() : [];
    } catch {
      transports = [];
    }
    response.transports = transports;
  } else {
    response.authenticatorData = bufferToBase64url(r.authenticatorData ?? new ArrayBuffer(0));
    response.signature = bufferToBase64url(r.signature ?? new ArrayBuffer(0));
    response.userHandle = r.userHandle ? bufferToBase64url(r.userHandle) : null;
  }
  return {
    id: c.id,
    rawId: bufferToBase64url(c.rawId),
    type: c.type,
    authenticatorAttachment: c.authenticatorAttachment ?? undefined,
    // The server asks for no extension: never send outputs it would not use
    clientExtensionResults: {},
    response
  };
}

/** Creates a passkey with the server's creation options. */
export async function createPasskey(options: Json, signal?: AbortSignal): Promise<Json> {
  const credential = await navigator.credentials.create({ publicKey: toCreationOptions(options), signal });
  if (!credential) throw new DOMException('No credential', 'NotAllowedError');
  return credentialToJSON(credential);
}

/** Signs in with a passkey; `conditional` = autofill request (pending until the user picks one). */
export async function getPasskey(options: Json, { conditional = false, signal }: { conditional?: boolean; signal?: AbortSignal } = {}): Promise<Json> {
  const credential = await navigator.credentials.get({
    publicKey: toRequestOptions(options),
    signal,
    ...(conditional ? { mediation: 'conditional' as CredentialMediationRequirement } : {})
  });
  if (!credential) throw new DOMException('No credential', 'NotAllowedError');
  return credentialToJSON(credential);
}

export type PasskeyErrorKind = 'cancelled' | 'exists' | 'aborted' | 'security' | 'unsupported' | 'other';

/** Classifies a `navigator.credentials` failure. */
export function passkeyErrorKind(error: unknown): PasskeyErrorKind {
  const name = error && typeof error === 'object' && 'name' in error ? String((error as { name: unknown }).name) : '';
  switch (name) {
    case 'NotAllowedError':
      return 'cancelled';
    case 'InvalidStateError':
      return 'exists';
    case 'AbortError':
      return 'aborted';
    case 'SecurityError':
      return 'security';
    case 'NotSupportedError':
      return 'unsupported';
    default:
      return 'other';
  }
}
