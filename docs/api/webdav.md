# WebDAV

TelegramFileManager exposes every channel as a **native, read/write WebDAV
share**. It is served directly by the app (ASP.NET/Kestrel) — there is no bridge
or helper process to start or stop, and it is always available while the app is
running.

The main use case is a **Synology Hyper Backup** destination: Hyper Backup
encrypts the backup client-side (AES-256) and writes it over WebDAV, so the data
stored in Telegram is only ever encrypted blobs. Any other WebDAV client works
too (rclone, davfs2, Cyberduck, Windows Explorer, …).

> Replaces the old Python WebDAV proxy. The `POST /api/v1/config/webdav/start|stop`
> endpoints and the `webDav` config block no longer exist.

---

## URL layout

```
http(s)://<host>:<port>/webdav/{channelId}/{path}
```

- `{channelId}` — the numeric Telegram channel id (the same id used everywhere in
  the API). Each channel is an independent WebDAV tree.
- `{path}` — folder/file path inside that channel.

A file maps to the channel index by its full path; a directory lists its
children. The channel root (`/webdav/{channelId}/`) is always a collection.

## Authentication

HTTP **Basic** auth. Set the credentials on the **Config** page
(`WebDAV User` / `WebDAV Password`); they are stored in MongoDB and take effect
immediately, with a fallback to `webdav_user` / `webdav_password` in
`Configuration/config.json`. If no user is configured the endpoint is **open**
(development only).

> **Serve over HTTPS.** Basic auth sends the credentials base64-encoded, not
> encrypted. Put the endpoint behind TLS (a reverse proxy, or your NAS) before
> using it across a network.

## Supported methods

| Method | Purpose |
| --- | --- |
| `OPTIONS` | Capability discovery. Advertises `DAV: 1,2`. |
| `PROPFIND` | List a directory (`Depth: 0` self, `Depth: 1` self + children) or read a file's properties. |
| `HEAD` | File metadata (size, type). |
| `GET` | Download a file. No `Range` → `200` + whole file; with `Range` → `206`. |
| `PUT` | Upload a file (whole-resource). |
| `MKCOL` | Create a folder. |
| `DELETE` | Delete a file or folder (recursive). |
| `MOVE` | Rename / move within the same channel. |
| `LOCK` / `UNLOCK` | Advisory class-2 locking (see below). |

---

## Examples (`curl`)

Assume the app on `http://localhost:5257`, channel `1430000229`, credentials
`admin` / `admin`.

```bash
BASE=http://localhost:5257/webdav/1430000229
A='-u admin:admin'
```

**List the channel root**

```bash
curl $A -X PROPFIND -H "Depth: 1" "$BASE/"
```

**Create a folder**

```bash
curl $A -X MKCOL "$BASE/backups/"          # 201 Created
```

**Upload a file**

```bash
curl $A -T backup.hbk "$BASE/backups/backup.hbk"   # 201 Created
```

**Download a file (whole file, 200)**

```bash
curl $A -o restored.hbk "$BASE/backups/backup.hbk"
```

**Download a byte range (206)**

```bash
curl $A -r 0-1048575 -o head.bin "$BASE/backups/backup.hbk"
# -> 206 Partial Content, Content-Range: bytes 0-1048575/<total>
```

**Rename / move (same channel)**

```bash
curl $A -X MOVE -H "Destination: $BASE/backups/backup-2.hbk" \
        "$BASE/backups/backup.hbk"          # 201 (or 204 if it overwrote)
```

**Delete**

```bash
curl $A -X DELETE "$BASE/backups/backup-2.hbk"   # 204 No Content
curl $A -X DELETE "$BASE/backups/"               # 204 (recursive)
```

**Lock / unlock**

```bash
# acquire
curl $A -X LOCK -H "Timeout: Second-600" \
  --data '<?xml version="1.0"?><D:lockinfo xmlns:D="DAV:"><D:lockscope><D:exclusive/></D:lockscope><D:locktype><D:write/></D:locktype><D:owner>hyperbackup</D:owner></D:lockinfo>' \
  "$BASE/backups/backup.hbk" -i
# -> 200/201 with a "Lock-Token: <opaquelocktoken:...>" header

# release
curl $A -X UNLOCK -H "Lock-Token: <opaquelocktoken:...>" "$BASE/backups/backup.hbk"   # 204
```

---

## Synology Hyper Backup setup

1. In Hyper Backup, create a task with destination **File Server (WebDAV)**.
2. Server address: your TFM host (ideally `https://…` behind a reverse proxy).
3. Path / shared folder: `/webdav/{channelId}/` (create a subfolder for the task
   if you like).
4. Username / password: the WebDAV credentials from the Config page.
5. Enable **client-side encryption** in Hyper Backup for encrypted backups.

Then run the task, and — importantly — verify a **restore** and a **version
rotation** (retention), not just that the first backup finishes.

---

## Behaviour & limits

- **One upload at a time.** Uploads are serialized through the normal transfer
  queue, and a configurable delay (`TimeSleepBetweenTransactions`, default 2 s)
  is applied between transactions to avoid Telegram rate-limiting (`FLOOD_WAIT`).
  A first full backup with many small chunks is therefore paced roughly at
  `max(delay, upload time)` per file — larger backup chunks mean fewer
  transactions and a faster first run.
- **Automatic splitting.** Files above the per-message limit are split
  transparently into multiple Telegram messages: **2 GB** normally, **4 GB** on a
  Premium account. Reassembled on download.
- **Download-once cache.** A `GET` fills a local disk cache once and streams from
  it, so repeated reads and range requests (e.g. Hyper Backup's integrity check)
  don't re-download from Telegram.
- **Empty files.** Telegram can't store a 0-byte message, so empty files are kept
  as index-only entries (no Telegram upload) and served back as `200` with
  `Content-Length: 0`.
- **Locking is advisory.** `LOCK` grants/refreshes/releases tokens and returns
  `423 Locked` on a conflicting lock, but writes are not gated on presenting the
  token (sufficient for a single-writer backup target).
- **Same-channel MOVE only.** `MOVE` is index-only (no re-upload). Moving between
  different channels is not supported.
- **Partial `PUT`** (`Content-Range`) is not supported (Telegram storage is
  append-only): `501`.

Next: [reference.md](reference.md).
