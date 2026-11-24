// Web login (cookie session) of the browser UI: /api/v1/web-auth

/** `GET /api/v1/web-auth/status`. */
export interface WebAuthStatusDto {
  /** A web password has been defined (false on a fresh install: show the setup screen). */
  passwordSet: boolean;
  /** The current request carries a valid session (or is allowed by the LAN bypass). */
  authenticated: boolean;
  /** The "no password on the local network" option is switched on. */
  lanBypassEnabled: boolean;
  /** The bypass applies to this request (the option is on and it is genuinely safe). */
  lanBypassActive: boolean;
  /** Registered passkeys (0 while no password is set). Missing on older servers. */
  passkeyCount?: number;
  /** The login page may offer "Sign in with a passkey" (password set, passkeys registered, host usable as RP ID). */
  passkeysAvailable?: boolean;
}

/** Body of `POST /api/v1/web-auth/setup`. */
export interface WebAuthSetupRequest {
  password: string;
}

/** Body of `POST /api/v1/web-auth/login`. */
export interface WebAuthLoginRequest {
  password: string;
  /** Persistent cookie instead of a browser-session one. */
  remember: boolean;
}

/** Body of `POST /api/v1/web-auth/password`. */
export interface WebAuthChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/** Body of `PUT /api/v1/web-auth/settings`. */
export interface WebAuthSettingsRequest {
  lanBypassEnabled: boolean;
}

/** Body of `DELETE /api/v1/web-auth/password`. */
export interface WebAuthRemovePasswordRequest {
  currentPassword: string;
}

// Passkeys (WebAuthn): /api/v1/web-auth/passkeys

/** One registered passkey (`WebPasskeyDto`). */
export interface WebPasskeyDto {
  /** Credential id (base64url), used by rename/delete. */
  id: string;
  name: string;
  createdUtc: string;
  lastUsedUtc?: string | null;
  /** BE flag: synced / multi-device passkey (password manager, iCloud, Google...). */
  backupEligible: boolean;
  /** BS flag at the last use. */
  backedUp: boolean;
  transports: string[];
  aaGuid: string;
  /** Domain (RP ID) it was created for: it only works there. */
  rpId?: string | null;
}

/** `GET /api/v1/web-auth/passkeys`. */
export interface WebPasskeyListDto {
  items: WebPasskeyDto[];
  /** RP ID used for this request. */
  rpId: string;
  /** Origins accepted for this request. */
  origins: string[];
  /** False when the host cannot be an RP ID (IP address). */
  supported: boolean;
  rpIdOverride?: string | null;
  originsOverride: string[];
}

/** WebAuthn options in their JSON form (binary values base64url). */
export type PasskeyOptionsJson = Record<string, unknown>;

/** Result of `register/begin` and `login/begin`. */
export interface WebPasskeyBeginDto {
  /** Opaque id of the pending ceremony (5 minutes, single use). */
  requestId: string;
  options: PasskeyOptionsJson;
}

/** Body of `POST /api/v1/web-auth/passkeys/register/finish`. */
export interface WebPasskeyRegisterFinishRequest {
  requestId: string;
  name: string;
  /** `PublicKeyCredential.toJSON()`. */
  credential: unknown;
}

/** Body of `POST /api/v1/web-auth/passkeys/login/finish`. */
export interface WebPasskeyLoginFinishRequest {
  requestId: string;
  credential: unknown;
  remember: boolean;
}

/** Body of `PUT /api/v1/web-auth/passkeys/settings`. */
export interface WebPasskeySettingsRequest {
  /** Domain; null/empty = the request host. */
  rpId?: string | null;
  /** `https://host[:port]` on that domain; empty = the request origin. */
  origins: string[];
}
