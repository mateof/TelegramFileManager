import { afterEach, describe, expect, it, vi } from 'vitest';
import { fakeCredential, installWebAuthn } from '@/test/webauthnMock';
import { base64urlToBuffer, bufferToBase64url } from './base64url';
import {
  conditionalMediationAvailable,
  createPasskey,
  credentialToJSON,
  getPasskey,
  passkeyErrorKind,
  passkeySupport,
  toCreationOptions,
  toRequestOptions
} from './webauthn';

const text = (b: ArrayBuffer | BufferSource | undefined) => new TextDecoder().decode(b as ArrayBuffer);

const creationJson = {
  rp: { id: 'tfm.example.com', name: 'Telegram File Manager' },
  user: { id: bufferToBase64url(new TextEncoder().encode('user-handle')), name: 'TFM', displayName: 'Telegram File Manager' },
  challenge: bufferToBase64url(new TextEncoder().encode('challenge-1')),
  pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
  authenticatorSelection: { residentKey: 'required', requireResidentKey: true, userVerification: 'preferred' },
  attestation: 'none',
  excludeCredentials: [{ type: 'public-key', id: bufferToBase64url(new TextEncoder().encode('old')) }]
};

let mock: ReturnType<typeof installWebAuthn> | null = null;
afterEach(() => {
  mock?.restore();
  mock = null;
});

describe('webauthn support', () => {
  it('needs a secure context and the API', async () => {
    mock = installWebAuthn({ secure: false });
    expect(passkeySupport()).toBe('insecure');
    expect(await conditionalMediationAvailable()).toBe(false);
    mock.restore();

    mock = installWebAuthn({ present: false });
    expect(passkeySupport()).toBe('unsupported');
    mock.restore();

    mock = installWebAuthn({ conditional: true });
    expect(passkeySupport()).toBe('ok');
    expect(await conditionalMediationAvailable()).toBe(true);
  });

  it('treats a failing availability check as unavailable', async () => {
    mock = installWebAuthn();
    mock.isConditionalMediationAvailable.mockRejectedValue(new Error('boom'));
    expect(await conditionalMediationAvailable()).toBe(false);
  });
});

describe('options conversion', () => {
  it('decodes creation options manually when parseCreationOptionsFromJSON is missing', () => {
    mock = installWebAuthn();
    const o = toCreationOptions(creationJson);
    expect(text(o.challenge)).toBe('challenge-1');
    expect(text(o.user.id)).toBe('user-handle');
    expect(text(o.excludeCredentials![0].id)).toBe('old');
    expect(o.rp.id).toBe('tfm.example.com');
    expect(o.authenticatorSelection?.residentKey).toBe('required');
  });

  it('uses the native parser when present, and falls back when it throws', () => {
    mock = installWebAuthn();
    const native = vi.fn(() => ({ native: true }) as unknown as PublicKeyCredentialCreationOptions);
    (window.PublicKeyCredential as unknown as Record<string, unknown>).parseCreationOptionsFromJSON = native;
    expect(toCreationOptions(creationJson)).toEqual({ native: true });
    native.mockImplementation(() => {
      throw new TypeError('bad');
    });
    expect(text(toCreationOptions(creationJson).challenge)).toBe('challenge-1');
  });

  it('decodes request options (usernameless: no allowCredentials)', () => {
    mock = installWebAuthn();
    const o = toRequestOptions({ challenge: bufferToBase64url(new TextEncoder().encode('c2')), rpId: 'tfm.example.com', userVerification: 'preferred' });
    expect(text(o.challenge)).toBe('c2');
    expect(o.allowCredentials).toEqual([]);
    expect(o.rpId).toBe('tfm.example.com');
  });
});

describe('credential serialisation', () => {
  it('serialises an attestation without toJSON', () => {
    const json = credentialToJSON(fakeCredential('create') as unknown as Credential);
    expect(json).toMatchObject({ id: 'cred-1', type: 'public-key', authenticatorAttachment: 'cross-platform', clientExtensionResults: {} });
    expect(text(base64urlToBuffer(json.rawId as string))).toBe('cred-1');
    const response = json.response as Record<string, unknown>;
    expect(text(base64urlToBuffer(response.attestationObject as string))).toBe('att');
    expect(response.transports).toEqual(['hybrid', 'internal']);
  });

  it('serialises an assertion without toJSON', () => {
    const json = credentialToJSON(fakeCredential('get') as unknown as Credential);
    const response = json.response as Record<string, unknown>;
    expect(text(base64urlToBuffer(response.signature as string))).toBe('sig');
    expect(text(base64urlToBuffer(response.authenticatorData as string))).toBe('auth');
    expect(text(base64urlToBuffer(response.userHandle as string))).toBe('user');
  });

  it('prefers a well-formed toJSON and ignores a broken one', () => {
    const good = { id: 'x', rawId: 'eA', type: 'public-key', response: { clientDataJSON: 'e30' } };
    expect(credentialToJSON({ ...fakeCredential('get'), toJSON: () => good } as unknown as Credential)).toBe(good);
    const broken = credentialToJSON({ ...fakeCredential('get'), toJSON: () => ({ nope: true }) } as unknown as Credential);
    expect(broken.rawId).toBe(bufferToBase64url(new TextEncoder().encode('cred-1')));
  });
});

describe('ceremonies', () => {
  it('create and get pass the decoded options, signal and mediation', async () => {
    mock = installWebAuthn();
    mock.create.mockResolvedValue(fakeCredential('create'));
    mock.get.mockResolvedValue(fakeCredential('get'));
    const signal = new AbortController().signal;

    const created = await createPasskey(creationJson, signal);
    expect(created.id).toBe('cred-1');
    const createArg = mock.create.mock.calls[0][0];
    expect(createArg.signal).toBe(signal);
    expect(text(createArg.publicKey!.challenge)).toBe('challenge-1');

    await getPasskey({ challenge: 'YQ', rpId: 'tfm.example.com' }, { conditional: true, signal });
    expect(mock.get.mock.calls[0][0].mediation).toBe('conditional');
    await getPasskey({ challenge: 'YQ', rpId: 'tfm.example.com' });
    expect(mock.get.mock.calls[1][0].mediation).toBeUndefined();
  });

  it('a null credential is a cancellation', async () => {
    mock = installWebAuthn();
    mock.get.mockResolvedValue(null);
    await expect(getPasskey({ challenge: 'YQ' })).rejects.toMatchObject({ name: 'NotAllowedError' });
  });

  it('classifies browser errors', () => {
    expect(passkeyErrorKind(new DOMException('x', 'NotAllowedError'))).toBe('cancelled');
    expect(passkeyErrorKind(new DOMException('x', 'InvalidStateError'))).toBe('exists');
    expect(passkeyErrorKind(new DOMException('x', 'AbortError'))).toBe('aborted');
    expect(passkeyErrorKind(new DOMException('x', 'SecurityError'))).toBe('security');
    expect(passkeyErrorKind(new DOMException('x', 'NotSupportedError'))).toBe('unsupported');
    expect(passkeyErrorKind(new Error('x'))).toBe('other');
    expect(passkeyErrorKind(null)).toBe('other');
  });
});
