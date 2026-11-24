import { ApiErrorCodes, isApiError } from '@tfm/api-client';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';
import { passkeyErrorKind } from '@/lib/webauthn';

/** User-facing message for a failed passkey sign-in or registration (browser or server error). */
export function passkeyErrorMessage(error: unknown, ceremony: 'login' | 'register'): string {
  if (isApiError(error)) {
    switch (error.code) {
      case ApiErrorCodes.InvalidCredentials:
        return t('The current password is wrong');
      case ApiErrorCodes.TooManyAttempts:
        return t('Too many attempts. Wait a moment and try again.');
      case ApiErrorCodes.PasskeyChallengeInvalid:
        return t('The passkey request expired. Try again.');
      case ApiErrorCodes.PasskeyExists:
        return t('This passkey is already registered.');
      case ApiErrorCodes.PasskeyUnsupported:
        return t('Passkeys do not work when the server is opened by IP address. Open it by its host name (localhost works).');
      case ApiErrorCodes.SessionRequired:
        return t('Sign in with the password in this browser to manage passkeys.');
      case ApiErrorCodes.PasskeyInvalid:
        return ceremony === 'login'
          ? t('This passkey was not accepted. It may have been removed from this server, or belong to another one.')
          : t('The passkey could not be verified.');
    }
    return errorMessage(error);
  }
  switch (passkeyErrorKind(error)) {
    case 'cancelled':
      return t('The passkey request was cancelled or timed out.');
    case 'exists':
      return t('This authenticator already has a passkey for this server.');
    case 'security':
      return t('The browser refused the request for this domain. Passkeys need HTTPS (or localhost) and the domain they were created on.');
    case 'unsupported':
      return t('This browser or authenticator does not support the requested passkey type.');
  }
  return errorMessage(error);
}
