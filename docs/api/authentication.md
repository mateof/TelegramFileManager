# Authentication

There are two separate things called "authentication" in this app; keep them
distinct:

- **API key** — the shared secret every request must carry. Covered in
  [getting-started.md](getting-started.md#authentication-the-api-key).
- **Telegram session** — the signed-in Telegram account. This page is about
  that.

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
