import { ApiErrorCodes, isApiError } from '@tfm/api-client';
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { conditionalMediationAvailable, getPasskey, passkeyErrorKind, passkeySupport } from '@/lib/webauthn';
import { passkeyErrorMessage } from './passkeyMessages';

/**
 * Passkey sign-in of the login page:
 * - on mount, when the browser supports it, a conditional request (autofill:
 *   the passkeys show up in the suggestions of the password field) waits in
 *   the background, and is restarted when its challenge expires;
 * - `signIn()` aborts it and opens the browser's passkey dialog.
 * Both end with the same session cookie as the password login.
 */
export function usePasskeyLogin({ available, remember }: { available: boolean; remember: boolean }) {
  const qc = useQueryClient();
  const supported = available && passkeySupport() === 'ok';
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [round, setRound] = useState(0);
  const rememberRef = useRef(remember);
  const conditional = useRef<AbortController | null>(null);

  useEffect(() => {
    rememberRef.current = remember;
  }, [remember]);

  const complete = useCallback(
    async (requestId: string, credential: unknown) => {
      await api.webAuth.passkeys.loginFinish({ requestId, credential, remember: rememberRef.current });
      await qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus });
    },
    [qc]
  );

  // Conditional mediation (autofill)
  useEffect(() => {
    if (!supported) return;
    let stopped = false;
    const run = async () => {
      if (!(await conditionalMediationAvailable())) return;
      while (!stopped) {
        const controller = new AbortController();
        conditional.current = controller;
        try {
          const begin = await api.webAuth.passkeys.loginBegin();
          if (stopped || controller.signal.aborted) return;
          const credential = await getPasskey(begin.options, { conditional: true, signal: controller.signal });
          if (stopped || controller.signal.aborted) return;
          setBusy(true);
          setError(null);
          await complete(begin.requestId, credential);
          return;
        } catch (e) {
          if (stopped || controller.signal.aborted) return;
          setBusy(false);
          // An autofill request outlives its 5-minute challenge: start a new one
          if (isApiError(e) && e.code === ApiErrorCodes.PasskeyChallengeInvalid) continue;
          const kind = passkeyErrorKind(e);
          if (kind !== 'aborted' && kind !== 'cancelled') setError(passkeyErrorMessage(e, 'login'));
          return;
        }
      }
    };
    void run();
    return () => {
      stopped = true;
      conditional.current?.abort();
      conditional.current = null;
    };
  }, [supported, complete, round]);

  const signIn = useCallback(async () => {
    // Only one WebAuthn request at a time: stop the autofill one first
    conditional.current?.abort();
    conditional.current = null;
    setBusy(true);
    setError(null);
    let ok = false;
    try {
      const begin = await api.webAuth.passkeys.loginBegin();
      const credential = await getPasskey(begin.options);
      await complete(begin.requestId, credential);
      ok = true;
    } catch (e) {
      setError(passkeyErrorMessage(e, 'login'));
    } finally {
      setBusy(false);
      // Back to autofill when the dialog did not sign in
      if (!ok) setRound((n) => n + 1);
    }
  }, [complete]);

  return { supported, busy, error, signIn };
}
