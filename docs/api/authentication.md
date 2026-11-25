# Authentication

There are three separate things called "authentication" in this app; keep them
distinct:

- **API key** — the shared secret apps send with every request. Covered in
  [getting-started.md](getting-started.md#authentication-the-api-key).
- **Web session** — the optional password of the web UI (cookie session) and
  its "no password on the local network" option. See [Web session](#web-session)
  at the end of this page.
- **Telegram session** — the signed-in Telegram account. Most of this page is
  about that.

The Telegram session is **shared and server-side**. There is one session per
server instance, used by the web UI and every API client at once. Signing in
through the API signs in the web UI too; signing out ends it for everyone. The
session is persisted to disk, so it survives restarts and you normally sign in
only once.

Endpoints that need an account (channels, files, transfers, shares) return
`401 not_logged_in` when there is no session, or `503 setup_required` when the
app has not been configured. Read state (`/api/v1/transfers`, `/api/v1/system/*`,
`/api/v1/config`, `/api/v1/local/*`, `/api/v1/playlists`) does **not** require a
session.

---

## Check the current state

```
GET /api/v1/auth/status
```

```json
{
  "success": true,
  "data": {
    "step": "phone",
    "isAuthenticated": false,
    "isConfigured": true,
    "user": null
  }
}
```

`step` is the state machine that drives login:

| `step` | Meaning | What to send next |
| --- | --- | --- |
| `phone` | No session. | The phone number (`POST /auth/login`, `isPhone:true`). |
| `vc` | Waiting for the code Telegram sent. | The verification code. |
| `pass` | Account has 2FA; waiting for the password. | The 2FA password. |
| `ok` | Signed in. | Nothing — you're done. |
| `setup_required` | App not configured. | Finish setup; see [system-and-config.md](system-and-config.md). |

When `step` is `ok`, `data.user` holds the signed-in account:

```json
{
  "id": 123456789,
  "username": "alice",
  "firstName": "Alice",
  "lastName": null,
  "phone": "34600000000",
  "isPremium": true
}
```

`isPremium` matters because it raises the per-file upload limit from 2 GB to
4 GB.

---

## Phone login

A short state machine. Post one value at a time and follow `step`.

### 1. Phone number

```
POST /api/v1/auth/login
Content-Type: application/json

{ "value": "+34600000000", "isPhone": true }
```

`isPhone: true` tells the server this is a phone number, so it starts a fresh
login and asks Telegram to send a code. Response `step` becomes `vc`.

### 2. Verification code

```
POST /api/v1/auth/login
{ "value": "12345" }
```

Response `step` becomes either `ok` (no 2FA) or `pass` (2FA enabled).

### 3. Two-factor password (only if `step` == `pass`)

```
POST /api/v1/auth/login
{ "value": "my-2fa-password" }
```

Response `step` becomes `ok` and `data.user` is populated.

A rejected value (wrong code, wrong password) comes back as
`400 invalid_request` with the Telegram error in `error.detail`; the `step` does
not advance, so simply prompt again.

---

## QR login

Lets the user sign in by scanning a code with the Telegram app on their phone —
no phone number typed on the client. This is the smoothest flow for a mobile
app, because the QR can be scanned from another device or, if the app *is* the
phone, deep-linked.

The Telegram QR flow is long-lived and callback-based (Telegram rotates the
token roughly every 30 seconds and, for 2FA accounts, asks for the password
*after* the phone accepts). The server holds that flow in a **QR session** you
poll.

### 1. Start a session

```
POST /api/v1/auth/qr
```

Optional `?logoutFirst=true` ends any existing session first.

```json
{
  "success": true,
  "data": {
    "sessionId": "9f2c…",
    "loginUrl": "tg://login?token=BASE64",
    "qrImageBase64": "iVBORw0KGgo…",
    "status": "waiting",
    "error": null
  }
}
```

Render `qrImageBase64` directly (`<img src="data:image/png;base64,…">`), or
encode `loginUrl` into a QR yourself for full control over styling.

### 2. Poll the session

```
GET /api/v1/auth/qr/{sessionId}
```

Poll every ~2 seconds. `status` transitions through:

| `status` | Meaning | Action |
| --- | --- | --- |
| `waiting` | Not scanned yet. Telegram may have rotated the token — repaint from the fresh `loginUrl`/`qrImageBase64`. | Keep polling. |
| `password_required` | Scanned, but the account has 2FA. | Prompt for the password and `POST …/password`. |
| `authenticated` | Signed in. | Stop polling. |
| `cancelled` | The session was cancelled or expired. | Start a new one. |
| `error` | Failed; see `error`. | Start a new one. |

Sessions with no polling for 10 minutes are discarded.

### 3. 2FA password (only if `status` == `password_required`)

```
POST /api/v1/auth/qr/{sessionId}/password
{ "password": "my-2fa-password" }
```

Then keep polling until `authenticated`.

### 4. Cancel (optional)

```
DELETE /api/v1/auth/qr/{sessionId}
```

Call this if the user backs out of the QR screen, to free the pending login.

---

## Who am I

```
GET /api/v1/auth/me
```

Returns the signed-in user, or `401 not_logged_in` when there is no session.
Useful right after login to show the account, and as a cheap session check.

---

## Sign out

```
POST /api/v1/auth/logout
```

Terminates the shared session. The web UI and every other client will need to
authenticate again. There is normally no reason to call this from a mobile app
unless the user explicitly wants to disconnect the account from the server.

---

## Client recipe

```ts
async function ensureSignedIn(api) {
  const { step, user } = await api.get("/api/v1/auth/status");
  if (step === "ok") return user;
  if (step === "setup_required") throw new Error("Server needs setup");

  // Prefer QR on mobile:
  const qr = await api.post("/api/v1/auth/qr");
  showQr(qr.qrImageBase64);
  for (;;) {
    await sleep(2000);
    const s = await api.get(`/api/v1/auth/qr/${qr.sessionId}`);
    repaintQrIfChanged(s.qrImageBase64);
    if (s.status === "authenticated") break;
    if (s.status === "password_required") {
      const pw = await promptPassword();
      await api.post(`/api/v1/auth/qr/${qr.sessionId}/password`, { password: pw });
    }
    if (s.status === "cancelled" || s.status === "error") throw new Error(s.error);
  }
  return (await api.get("/api/v1/auth/me"));
}
```

Next: [channels.md](channels.md).

---

## Web session

The web UI (the Blazor UI at `/` and the web app at `/app`) can be protected
with a **password**. It is optional and off by default.

### Who gets in

| | No password (default) | Password set |
|---|---|---|
| Blazor UI (`/`, `/_blazor`) | open | cookie, or LAN bypass. A browser navigation without a session is redirected to `/app/login?returnUrl=…` |
| `/api/v1/**`, `/api/mobile/**`, `/hubs/**` | API key when one is configured, otherwise open. A web session cookie also works | cookie, API key or LAN bypass |
| Legacy media/file endpoints (`/api/file/*`, `/api/temp/*`, `/api/video/*`, `/api/localvideo/*`, `/api/channel/image/*`, `/api/Log/Download`, `/api/app/*`), `/local/**`, `/img/telegram/**` | open | cookie, API key or LAN bypass |
| Swagger (`/api-docs`, `/swagger`) | open | cookie, API key or LAN bypass |
| `/api/v1/web-auth/**` | always reachable | always reachable (the endpoint checks what it needs) |
| `/api/v1/system/ping` | like the rest of `/api/v1` | reachable without credentials; a **wrong** API key still gets `401` |
| `/webdav/**` | its own HTTP Basic auth (unchanged) | its own HTTP Basic auth (unchanged) |
| `/app/**`, `wwwroot` assets (`/css`, `/js`, `/_framework`, …) | public | public |

"API key" means the same key as always, sent as `X-Api-Key`, `?apiKey=`,
`?access_token=` or `Authorization: Bearer`. While a password is set, the API
key is the **only** way for the apps in; if no key is configured, apps cannot
connect until you set one (Config → API & WebDAV Access).

Media elements, `<img>` and downloads of the web app send the cookie
automatically (same origin). Apps keep appending `?apiKey=` to stream/download
URLs. Exported `.strm` files include `?apiKey=` when a key is configured, so
media servers (Emby, Kodi…) keep playing them; regenerate older `.strm` files
after setting a password.

### LAN bypass ("no password on the local network")

When enabled (and only while a password is set), a request gets in without
the password if **all** of these hold:

1. The TCP peer address — the socket address, captured *before* forwarded
   headers are applied — is loopback or private: `127.0.0.0/8`, `10.0.0.0/8`,
   `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `::1`, `fc00::/7`,
   `fe80::/10`.
2. The request carries none of `X-Forwarded-For`, `X-Forwarded-Host`,
   `X-Forwarded-Proto`, `X-Real-IP`, `Forwarded`.
3. It is addressed to a local host name: an IP literal, `localhost`, a single
   label (`nas`) or `*.local`, `*.lan`, `*.home`, `*.internal`, `*.home.arpa`.
4. It is not a cross-site browser request: `Sec-Fetch-Site`, when present, is
   `same-origin` or `none`, and `Origin`, when present, is this server.

Why: a reverse proxy normally runs on the same host or LAN, so everything it
relays from the internet arrives from a private address. Rule 2 is what keeps
the internet out: through a proxy the password is **always** required (adding
those headers can only remove the bypass, never grant it). Rule 3 defeats DNS
rebinding (a public domain made to resolve to the LAN address) and rule 4
stops other web sites opened in a LAN browser from driving the API through
the bypass (the CORS policy is open for the apps).

### CSRF rule

State-changing requests (anything but GET/HEAD/OPTIONS) to `/api/**` and
`/hubs/**` that rely on the cookie or on the LAN bypass, and every
state-changing `/api/v1/web-auth/*` call, must send:

```
X-Requested-With: tfm
```

otherwise they get `403 csrf_header_missing`. Requests with the API key are
exempt. With the `SameSite=Strict` cookie this is a sufficient defence: the
cookie never travels on cross-site requests, and a page cannot add a custom
header to a cross-origin request without a CORS preflight. The SignalR JS
client must pass the header too:
`new HubConnectionBuilder().withUrl("/hubs/events", { headers: { "X-Requested-With": "tfm" } })`.

`/api/file/*` and `/api/temp/*` are exempt from the header (the Blazor UI posts
to them with plain forms and Dropzone); they rely on SameSite=Strict and the
LAN rule above.

### The cookie

`tfm_session`: HttpOnly, `SameSite=Strict`, `Secure` when the request is HTTPS
(behind a trusted proxy, from `X-Forwarded-Proto`), sliding expiration.
"Remember me" makes it persistent for 30 days; otherwise it is a browser
session cookie with a 12 h idle timeout. Setting, changing or removing the
password signs out every session.

Without a password, `GET /api/v1/web-auth/status` from a same-origin browser
issues a session cookie too, so the web app can call the API even when an API
key is configured (the open web UI gives the same access anyway). Those
cookies stop working as soon as a password is set.

### Endpoints

All answer with the usual envelope; the payload of every one is the status
object below. None needs the API key nor a Telegram session.

```
GET /api/v1/web-auth/status
```

```json
{
  "success": true,
  "data": {
    "passwordSet": true,
    "authenticated": false,
    "lanBypassEnabled": true,
    "lanBypassActive": false,
    "passkeyCount": 1,
    "passkeysAvailable": true
  }
}
```

- `authenticated`: the request carries a valid session cookie.
- `lanBypassActive`: this very request is let in by the LAN rule.
- `passkeyCount`: registered passkeys (always 0 without a password).
- `passkeysAvailable`: the login page may offer "Sign in with a passkey" (a
  password is set, at least one passkey exists and this request's host can be
  a WebAuthn RP ID, i.e. it is not an IP address).

The web app shows the login page when `passwordSet && !authenticated && !lanBypassActive`.

| Method | Path | Body | Needs | Errors |
|---|---|---|---|---|
| `POST` | `/api/v1/web-auth/setup` | `{ "password": "…" }` | no password set yet | `400 weak_password` (under 8 chars), `409 password_already_set` |
| `POST` | `/api/v1/web-auth/login` | `{ "password": "…", "remember": true }` | — | `401 invalid_credentials`, `409 password_not_set`, `429 too_many_attempts` (+ `Retry-After` seconds) |
| `POST` | `/api/v1/web-auth/logout` | — | — | — |
| `POST` | `/api/v1/web-auth/password` | `{ "currentPassword": "…", "newPassword": "…" }` | authenticated | `401 unauthorized` / `401 invalid_credentials`, `400 weak_password`, `409 password_not_set`, `429 too_many_attempts` |
| `PUT` | `/api/v1/web-auth/settings` | `{ "lanBypassEnabled": true }` | authenticated | `401 unauthorized` |
| `DELETE` | `/api/v1/web-auth/password` | `{ "currentPassword": "…" }` | authenticated | `401 unauthorized` / `401 invalid_credentials`, `409 password_not_set`, `429 too_many_attempts` |

`setup` and `login` set the cookie; `password` re-issues the caller's cookie
under the new password; `logout` clears it. "Authenticated" means cookie, API
key or LAN bypass while a password is set.

Wrong passwords are limited per client IP: 5 per minute are tolerated, the next
one locks the IP out for 1 minute, doubling on every further lockout up to 30
minutes. Every failure is logged with the IP.

The password is stored as a PBKDF2 hash (ASP.NET Core Identity `PasswordHasher`,
HMAC-SHA512, random salt, 100 000 iterations) in the config database
(`TCCONFIG.webauth`) and in `datauser/web-auth.json`, so the protection stays on
if MongoDB is unreachable at startup. **Forgot it?** Start the server once with
the environment variable `TFM_RESET_WEB_PASSWORD=true`, then remove the
variable and set a new password.

### Passkeys

While a web password is set, passkeys (WebAuthn) can be added as an extra way to
sign in; the password keeps working as the fallback. Any authenticator works:
password managers with synced passkeys (Bitwarden, 1Password, iCloud Keychain,
Google Password Manager), the platform authenticator (Windows Hello, Touch ID,
Android) and security keys. Capability string: `webAuth.passkeys`.

| Method | Path | Body | Needs | Errors |
|---|---|---|---|---|
| `POST` | `/api/v1/web-auth/passkeys/register/begin` | `{ "currentPassword": "…" }` | browser session | `403 session_required`, `401 invalid_credentials`, `400 passkey_unsupported`, `409 password_not_set`, `429 too_many_attempts` |
| `POST` | `/api/v1/web-auth/passkeys/register/finish` | `{ "requestId": "…", "name": "Bitwarden", "credential": { … } }` | browser session | `403 session_required`, `400 passkey_challenge_invalid`, `400 passkey_invalid`, `409 passkey_exists` |
| `POST` | `/api/v1/web-auth/passkeys/login/begin` | — | — | `400 passkey_unsupported`, `409 password_not_set`, `429 too_many_attempts` |
| `POST` | `/api/v1/web-auth/passkeys/login/finish` | `{ "requestId": "…", "credential": { … }, "remember": true }` | — | `400 passkey_challenge_invalid`, `401 passkey_invalid`, `429 too_many_attempts` |
| `GET` | `/api/v1/web-auth/passkeys` | — | browser session | `403 session_required` |
| `PUT` | `/api/v1/web-auth/passkeys/{id}` | `{ "name": "…" }` | browser session | `403 session_required`, `404 not_found` |
| `DELETE` | `/api/v1/web-auth/passkeys/{id}` | — | browser session | `403 session_required`, `404 not_found` |
| `PUT` | `/api/v1/web-auth/passkeys/settings` | `{ "rpId": "example.com", "origins": ["https://tfm.example.com"] }` | browser session | `403 session_required`, `400 invalid_request` |

"Browser session" means a session cookie obtained with the password (or a
passkey): the API key, the LAN bypass and the server-side token are **not**
enough, so neither an app nor a LAN neighbour can add a way in. Registration
also re-checks the current password (wrong ones count for the rate limiter).
All are state-changing web-auth calls, so the CSRF header is required.

**Ceremonies.** `begin` returns `{ requestId, options }`; `options` is the JSON
form of the WebAuthn options (binary values base64url), for
`PublicKeyCredential.parseCreationOptionsFromJSON()` /
`parseRequestOptionsFromJSON()`. Send `credential.toJSON()` and the `requestId`
to `finish` within 5 minutes. Challenges are kept in memory, are single use and
bound to the ceremony (a registration challenge cannot finish a login). A
successful `login/finish` sets exactly the same cookie as the password login;
failed ones count for the same per-IP rate limiter.

**Options.** Discoverable credentials (`residentKey: required`),
`userVerification: preferred`, no authenticator attachment restriction,
attestation `none` (any attestation format is accepted, no trust-chain or
metadata checks), algorithms ES256, EdDSA and RS256, `excludeCredentials` with
the registered ids. Login is usernameless (empty `allowCredentials`) and
supports conditional mediation (autofill). One fixed random user handle
(`TFM` / `Telegram File Manager`) is used for every passkey.

**Counters.** Synced passkeys always report a signature counter of 0, which is
accepted. A login is rejected (possible clone) only when both the stored and
the received counters are non-zero and the received one did not grow. The
backup flags (BE/BS) are accepted and stored (shown as "synced" in the UI).

**RP ID and origin.** The RP ID is the host name of the request and the
expected origin is its scheme + host (+ port); behind a proxy listed in
`TrustedProxies` they come from `X-Forwarded-Host`/`-Proto`. Both the
`clientDataJSON.origin` and the RP ID hash are verified on registration and
login. A passkey only works on the domain it was created for, and browsers
refuse IP addresses as RP IDs: open the web UI by a host name (`localhost` is
fine) and over HTTPS (browsers only allow WebAuthn on `http://localhost`).
For proxies that cannot forward the host, `PUT passkeys/settings` sets an RP ID
override (a domain) and the list of accepted origins (each on that domain);
empty values go back to the request host/origin. Changing the RP ID makes
existing passkeys unusable.

**Storage.** Passkeys live with the password hash (`TCCONFIG.webauth` and
`datauser/web-auth.json`): credential id, COSE public key, counter, user
handle, AAGUID, transports, BE/BS, name, creation and last use. Removing the
password removes every passkey, and so does `TFM_RESET_WEB_PASSWORD=true`
(logged). Changing the password keeps them.

### Reverse proxies (`TrustedProxies`)

Only proxies listed in `TrustedProxies` may rewrite the client address, scheme
and host through `X-Forwarded-For`/`-Proto`/`-Host`. Default: loopback and the
private ranges above, which covers proxies on the same host, in a Docker network
or on the LAN. To restrict it, set the environment variable
`TrustedProxies=172.18.0.0/16,10.0.0.5` (or the `TrustedProxies` array in
appsettings, or `trusted_proxies` in `Configuration/config.json`). Earlier
versions trusted any peer, so anyone could forge `X-Forwarded-For`.
