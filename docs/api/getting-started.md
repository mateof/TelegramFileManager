# Getting started

## Base URL

Every endpoint in this document is relative to the server root. If the app runs
on `http://192.168.1.50:5257`, then `/api/v1/system/ping` is
`http://192.168.1.50:5257/api/v1/system/ping`.

The app also listens on HTTPS (port `7224` by default). Behind a reverse proxy
(nginx, Traefik…) the app honours `X-Forwarded-Proto`/`X-Forwarded-Host`, so the
absolute `streamUrl`/`downloadUrl` values it builds use the public scheme and
host the client actually reached.

## Authentication: the API key

Two independent layers guard the API:

1. **API key** — a static shared secret that gates *every* `/api/v1`, `/api/mobile`
   and `/hubs` request. It answers "is this client allowed to talk to the server
   at all?".
2. **Telegram session** — the signed-in Telegram account. It answers "is there
   an account whose channels and files we can act on?". See
   [authentication.md](authentication.md).

### Configuring the API key

The key is `mobile_api_key` in `Configuration/config.json` (or the
`mobile_api_key` environment variable / setup wizard):

```json
{
  "api_id": "…",
  "hash_id": "…",
  "mongo_connection_string": "…",
  "mobile_api_key": "choose-a-long-random-secret"
}
```

- **If the key is empty or missing, authentication is disabled** and every
  request is allowed. This is convenient for local development but must not be
  used on a reachable network. `GET /api/v1/system/info` reports
  `requiresApiKey` so a client can tell.
- The key is compared with an ordinal (exact, case-sensitive) match.

### Sending the API key

| Transport | How |
| --- | --- |
| HTTP header (preferred) | `X-Api-Key: your-secret` |
| Query string | `?apiKey=your-secret` |
| SignalR / media `<video>`/`<audio>` src | `?access_token=your-secret` |

The query-string forms exist because browsers and native media elements cannot
attach custom headers to a `<video src>` or a WebSocket handshake.

A missing or wrong key yields `401`:

```json
{
  "success": false,
  "error": {
    "code": "unauthorized",
    "message": "API key required",
    "detail": "Provide your API key in the X-Api-Key header, or in the apiKey/access_token query parameter"
  }
}
```

## Response envelope

Every JSON response — success or failure — has the same shape:

```jsonc
{
  "success": true,          // boolean, always present
  "data": { … },            // payload on success, null on failure
  "error": {                // null on success
    "code": "channel_not_found",
    "message": "Channel not found",
    "detail": "optional extra context"
  },
  "message": "Channel created", // optional human-readable note
  "page": {                 // only on paged list endpoints
    "page": 1,
    "pageSize": 50,
    "totalItems": 1234,
    "totalPages": 25,
    "hasNext": true,
    "hasPrevious": false
  }
}
```

Recommended client handling:

```ts
const res = await fetch(url, { headers: { "X-Api-Key": key } });
const body = await res.json();
if (!body.success) {
  throw new ApiError(body.error.code, body.error.message, res.status);
}
return body.data;
```

## Error codes

`error.code` is stable and safe to branch on. HTTP status still follows REST
conventions (`404`, `409`, `503`…), but the code is the source of truth.

| Code | Typical status | Meaning |
| --- | --- | --- |
| `unauthorized` | 401 | Missing or invalid API key. |
| `not_logged_in` | 401 | No Telegram session; sign in via `/api/v1/auth`. |
| `setup_required` | 503 | The app has not finished first-run setup. |
| `invalid_request` | 400 | Malformed body or query. |
| `not_found` | 404 | Generic missing resource. |
| `channel_not_found` | 404 | Unknown channel id. |
| `file_not_found` | 404 | Unknown file/folder id. |
| `task_not_found` | 404 | Unknown transfer id. |
| `playlist_not_found` | 404 | Unknown playlist id. |
| `conflict` | 409 | Name already exists, etc. |
| `already_running` | 409 | e.g. a channel refresh is already in progress. |
| `forbidden` | 403 | Operation not allowed for this account (e.g. deleting a channel you don't own). |
| `not_supported` | 400 | Operation does not apply to this resource. |
| `service_unavailable` | 503 | A dependency (MongoDB, log store) is not available. |
| `internal_error` | 500 | Unexpected server error; `error.detail` carries the exception message. |

## Paging and sorting

List endpoints accept:

| Query param | Default | Notes |
| --- | --- | --- |
| `page` | `1` | 1-based. |
| `pageSize` | `50` | Clamped to 1–500. |
| `sortBy` | endpoint-specific | e.g. `name`, `date`, `size`, `type`. |
| `sortDescending` | `false` | |

The `page` block in the envelope tells you how to page forward. Folders are
always returned before files regardless of `sortBy`.

## HTTP verbs and status codes

| Verb | Use |
| --- | --- |
| `GET` | Read. Never changes state. |
| `POST` | Create, or start an action (a transfer, a refresh). |
| `PUT` | Full replace (rename, reorder). |
| `PATCH` | Partial update (configuration). |
| `DELETE` | Remove. |

| Status | Meaning in this API |
| --- | --- |
| `200 OK` | Success with payload. |
| `201 Created` | A resource was created; `data` is that resource. |
| `202 Accepted` | Work was queued; watch the SignalR hub for progress. |
| `4xx` / `5xx` | See the error table above. |

## A first end-to-end walkthrough

```bash
KEY="your-secret"
BASE="http://192.168.1.50:5257"
H=(-H "X-Api-Key: $KEY" -H "Content-Type: application/json")

# 1. Health + readiness
curl "${H[@]}" "$BASE/api/v1/system/ping"
curl "${H[@]}" "$BASE/api/v1/system/info"

# 2. Sign in (skip if already authenticated)
curl "${H[@]}" -X POST "$BASE/api/v1/auth/login" -d '{"value":"+34600000000","isPhone":true}'
curl "${H[@]}" -X POST "$BASE/api/v1/auth/login" -d '{"value":"12345"}'      # SMS/app code
# curl … -d '{"value":"my-2fa-password"}'                                    # only if prompted

# 3. Pick a channel that already has a local index
curl "${H[@]}" "$BASE/api/v1/channels?onlySaved=true&pageSize=100"

# 4. Browse it
curl "${H[@]}" "$BASE/api/v1/channels/1290586824/files?path=/&filter=audio"

# 5. Download two files to the server's local storage
curl "${H[@]}" -X POST "$BASE/api/v1/transfers/downloads" \
  -d '{"channelId":"1290586824","fileIds":["694a…","694b…"],"targetPath":"downloads/edm"}'

# 6. Poll progress (or, better, use the SignalR hub — see signalr.md)
curl "${H[@]}" "$BASE/api/v1/transfers"
```

Next: [authentication.md](authentication.md).
