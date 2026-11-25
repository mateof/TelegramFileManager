// Telegram session lifecycle: /api/v1/auth (mirrors Models/Api/AuthDtos.cs)

/** Step of the Telegram login state machine the server is currently waiting for. */
export const AuthStep = {
  /** Server needs a phone number. */
  Phone: 'phone',
  /** Server needs the verification code sent by Telegram. */
  VerificationCode: 'vc',
  /** Server needs the two-factor password. */
  Password: 'pass',
  /** Session is authenticated. */
  Authenticated: 'ok',
  /** The application has not been configured yet (see /api/v1/system/setup). */
  SetupRequired: 'setup_required'
} as const;

export type AuthStep = (typeof AuthStep)[keyof typeof AuthStep];

/** Current authentication state of the Telegram session. */
export interface AuthStatusDto {
  /** One of the values in {@link AuthStep}. */
  step: AuthStep;
  /** True when the session is fully authenticated. */
  isAuthenticated: boolean;
  /** True when API id/hash and MongoDB are configured. */
  isConfigured: boolean;
  /** Signed-in Telegram user, when authenticated. */
  user: TelegramUserDto | null;
}

/** Signed-in Telegram user. */
export interface TelegramUserDto {
  id: number;
  username: string | null;
  firstName: string | null;
  lastName: string | null;
  phone: string | null;
  isPremium: boolean;
}

/** Body of `POST /api/v1/auth/login`. */
export interface LoginStepRequest {
  /** Value for the current step: the phone number, the verification code or the two-factor password. */
  value: string;
  /** True when `value` is a phone number, so the server starts a new login instead of continuing the pending one. */
  isPhone?: boolean;
}

/** Status values of {@link QrLoginDto.status}. */
export const QrLoginStatus = {
  Waiting: 'waiting',
  PasswordRequired: 'password_required',
  Authenticated: 'authenticated',
  Cancelled: 'cancelled',
  Error: 'error'
} as const;

export type QrLoginStatus = (typeof QrLoginStatus)[keyof typeof QrLoginStatus];

/** QR login session created by `POST /api/v1/auth/qr`. */
export interface QrLoginDto {
  /** Identifier used to poll or cancel the QR session. */
  sessionId: string;
  /** The `tg://login?token=...` URL to render as a QR code. */
  loginUrl: string | null;
  /** PNG QR image, base64 encoded, ready to be shown as-is. */
  qrImageBase64: string | null;
  /** `waiting`, `password_required`, `authenticated`, `cancelled` or `error`. */
  status: QrLoginStatus | (string & {});
  /** Error detail when `status` is `error`. */
  error: string | null;
}

/** Body of `POST /api/v1/auth/qr/{sessionId}/password`. */
export interface QrPasswordRequest {
  password: string;
}

/** Query of `POST /api/v1/auth/qr`. */
export interface StartQrQuery {
  /** Terminate any existing session before starting. */
  logoutFirst?: boolean;
}
