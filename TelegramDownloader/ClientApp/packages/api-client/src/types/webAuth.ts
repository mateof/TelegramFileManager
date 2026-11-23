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
