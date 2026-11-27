# Player data (favourites, history, resume)

Favourite tracks, play history and "continue where I left off" positions,
stored on the server so every device of the (single) user sees the same: the
web player and the audio app. Everything lives under `/api/v1/player`, behind
the usual authentication (API key or web session); no Telegram session is
needed. Advertised as **`player.sync`** in the `features` of
`GET /api/mobile/capabilities`.

Every answer with a body is the usual envelope `{ success, data, error, message, page }`;
the shapes below are the `data`. Errors: `400 invalid_request` (see
[Validation](#validation)), `500 internal_error`.

## Track keys

A track is identified by a **key**:

| Track | Key |
| --- | --- |
| Indexed channel file | `channel:{channelId}:{fileId}` (e.g. `channel:-1001234567890:6650f0c2a1b2c3d4e5f60718`) |
| Local file | `local:{path}`: path relative to the local root, `/` separators, no leading slash (e.g. `local:music/rock/song.flac`) |

The server computes the key from the track sent (`channelId` + `fileId`, or
`filePath` for local files); a `key` sent by the client is ignored when the ids
or path are there, and used only when they are not. Local paths are
normalised the same way everywhere (`\` becomes `/`, blanks and leading slashes
are trimmed; the case is kept), so `local:/music\a.mp3` and `local:music/a.mp3`
are the same key. `?key=` parameters are normalised too.

```ts
interface ApiPlayerTrackDto {
  key: string;               // always set in answers
  channelId: string | null;  // channel tracks
  fileId: string | null;     // channel tracks
  isLocalFile: boolean;
  filePath: string | null;   // local files: relative path; channel tracks: as sent (optional)
  fileName: string;          // derived from filePath when not sent
  channelName: string | null;
  title: string | null;
  artist: string | null;
  album: string | null;
  duration: number | null;   // seconds
  fileSize: number | null;   // bytes
}
```

Which form is used: `isLocalFile: true` (or a `filePath` without both channel
ids) makes it a local file; otherwise `channelId` + `fileId` are required. A body
with only a valid `key` is accepted too. Everything but the ids/path is a
snapshot for display (lists render without looking files up); writing the same
track again refreshes it.

## Favourites

```
GET    /api/v1/player/favorites?page=1&pageSize=100   → 200 ApiPlayerFavoritesPageDto (newest first)
GET    /api/v1/player/favorites/keys                  → 200 string[]
PUT    /api/v1/player/favorites        body: track    → 200 ApiPlayerFavoriteDto
DELETE /api/v1/player/favorites?key=…                 → 204 (also when it was not a favourite)
```

```ts
interface ApiPlayerFavoriteDto extends ApiPlayerTrackDto { addedAt: string; } // ISO UTC
interface ApiPlayerFavoritesPageDto { items: ApiPlayerFavoriteDto[]; total: number; page: number; pageSize: number; }
```

`PUT` is idempotent: adding a favourite again updates its snapshot and keeps
its first `addedAt`. `favorites/keys` returns every key, for quick "is it a
favourite" checks.

## History

```
POST   /api/v1/player/history          body: ApiPlayerHistoryWriteRequest → 201 ApiPlayerHistoryEntryDto
GET    /api/v1/player/history?limit=100&before=2026-06-01T12:00:00Z       → 200 ApiPlayerHistoryPageDto (newest first)
GET    /api/v1/player/history/top?limit=50&days=30                        → 200 ApiPlayerTopTrackDto[]
DELETE /api/v1/player/history                                             → 204 (clears it)
```

```ts
interface ApiPlayerHistoryWriteRequest {
  track: ApiPlayerTrackDto;   // key optional
  playedAt?: string;          // ISO 8601, default now; send it when syncing plays made offline
  positionSeconds?: number;   // >= 0
  completed?: boolean;        // default false
}
interface ApiPlayerHistoryEntryDto { id: string; track: ApiPlayerTrackDto; playedAt: string; positionSeconds: number | null; completed: boolean; }
interface ApiPlayerHistoryPageDto { items: ApiPlayerHistoryEntryDto[]; hasMore: boolean; }
interface ApiPlayerTopTrackDto { track: ApiPlayerTrackDto; playCount: number; lastPlayedAt: string; }
```

- At most **2000** plays are kept; the oldest (by `playedAt`) are deleted.
- Page with `before` = the `playedAt` of the last item (plays strictly before it).
- `history/top` groups the plays by key: sorted by `playCount`, then by
  `lastPlayedAt`; `track` is the snapshot of the latest play. `days` (1 or
  more) only counts the plays of the last N days; all when omitted.
- What counts as a play is up to the client. The web player records one per
  play after 30 seconds listened or at the end of the track.

## Resume positions

"Continue where I left off", meant for long audio (audiobooks, podcasts, mixes).

```
PUT    /api/v1/player/resume          body: ApiPlayerResumeWriteRequest → 200 ApiPlayerResumeDto
GET    /api/v1/player/resume?key=…                                      → 200 ApiPlayerResumeDto | 404 not_found
GET    /api/v1/player/resume/list?limit=50                              → 200 ApiPlayerResumeDto[] (most recently updated first)
DELETE /api/v1/player/resume?key=…                                      → 204 (also when there was none)
```

```ts
interface ApiPlayerResumeWriteRequest { track: ApiPlayerTrackDto; positionSeconds: number; durationSeconds: number; } // both >= 0
interface ApiPlayerResumeDto {
  track: ApiPlayerTrackDto;
  positionSeconds: number;
  durationSeconds: number;
  updatedAt: string;   // ISO UTC
  finished: boolean;   // only true in a PUT answer: the entry was deleted (see below)
}
```

When `positionSeconds` is in the **last 2%** or the **last 15 seconds** of
`durationSeconds`, the track counts as finished: its entry is **deleted**
instead of saved and the `PUT` answers `200` with `finished: true` (the other
fields echo the request). A `durationSeconds` of `0` (unknown) never counts as
finished.

## Validation

`400 invalid_request` for:

- a track without a resolvable key (no `channelId` + `fileId`, no local
  `filePath`, no valid `key`), or a missing body;
- a `?key=` that is not `channel:{channelId}:{fileId}` or `local:{path}`;
- negative `positionSeconds`, `durationSeconds`, `duration` or `fileSize`;
- `limit` / `pageSize` outside 1-500, `page` below 1, `days` below 1.

A body that is not valid JSON is refused by ASP.NET itself with a
`400` problem details answer (not the envelope).

## Live updates

After every write the server sends `PlayerDataChanged` on
[`/hubs/events`](signalr.md#application-events-hub-hubsevents), so other
devices can refresh:

```ts
interface PlayerDataChangedEventDto {
  kind: "favorites" | "history" | "resume";
  key: string | null;   // the track that changed; null when the history was cleared
}
```

The device that made the change receives it too.

## Storage

MongoDB, in the configuration database (`TCCONFIG`, next to the playlists):
`player_favorites` and `player_resume` use the track key as `_id`;
`player_history` is indexed by `Key` and `PlayedAt`.
