# Media library

The **library** turns the video files of the indexed channels into a catalogue
of **movies and series** the way Plex or Emby do: file names (and their folders)
are parsed, the titles are looked up at one or more **metadata providers**, and
the result — title, year, overview, genres, rating, poster, backdrop, seasons
and episodes — is stored in MongoDB (database `TFM-LIBRARY`) and served from
there. Posters are cached on the server, so clients only ever talk to this API.

The library also keeps **playback progress** per file ("continue watching",
watched marks, next episode of a series) and lets a client **fix a wrong
identification** by hand.

Base path: `/api/v1/library`. Nothing here needs a Telegram session: the scan
only reads the MongoDB indexes and the providers. Playing a file does need one,
as usual.

---

## Setup

1. Enable the library and add at least one provider key in the **Config** page
   of the web UI (section *Media library*), or through
   [`PATCH /api/v1/config`](system-and-config.md#configuration):

   ```
   PATCH /api/v1/config
   {
     "libraryEnabled": true,
     "libraryLanguage": "es-ES",
     "libraryProviders": [
       { "id": "tmdb", "enabled": true, "apiKey": "…", "priority": 1 },
       { "id": "omdb", "enabled": true, "apiKey": "…", "priority": 2 }
     ]
   }
   ```

   | Provider | Id | Key | What it brings |
   | --- | --- | --- | --- |
   | The Movie Database | `tmdb` | free, [themoviedb.org/settings/api](https://www.themoviedb.org/settings/api) (v3 key or v4 read token) | Titles and overviews in the configured language, posters, backdrops, seasons, episodes with stills. **Recommended primary.** |
   | OMDb | `omdb` | free tier (1000 requests/day), [omdbapi.com/apikey.aspx](https://www.omdbapi.com/apikey.aspx) | IMDb data: rating, votes, English plot, poster, episode list. Its ids are IMDb ids. |

   Providers are asked in **priority order**; the first one that identifies a
   title with confidence wins, and the others **enrich** the item (IMDb rating
   from OMDb, images from TMDB…). Keys are returned masked by `GET /config`;
   send `apiKey` only to change it (empty string clears it).

2. Start a scan: `POST /api/v1/library/scan`. With `libraryAutoScan` on
   (default) every channel refresh also scans that channel's new files.

Until the library is enabled the catalogue endpoints answer
`503 library_disabled`. The watch-state endpoints always work.

Other settings: `libraryAutoScan`, `libraryIncludedChannels` (ids to scan;
empty means every indexed channel), `libraryExcludedChannels` (ids never
scanned, even when included), `libraryWatchedThreshold` (fraction of the
duration after which a file counts as watched, default `0.92`). The Config page
offers both channel lists as searchable pickers.

## Folder rules

A channel often mixes content: `Series/` next to `Cine/`, an `Extras/` folder
with trailers. **Folder rules** tell the scan what to expect under a folder:

```
PATCH /api/v1/config
{ "libraryFolderRules": [
    { "channelId": 1290586824, "path": "/Series/", "kind": "series" },
    { "channelId": 1290586824, "path": "/Cine/",   "kind": "movie" },
    { "channelId": 1290586824, "path": "/Extras/", "kind": "ignore" },
    { "channelId": 1417000000, "path": "/",        "kind": "series" }
] }
```

- A rule covers every subfolder; when several apply, the **longest path wins**.
  `/` is the whole channel, which is how you say "this channel is only series".
- `series`: every file is looked up as a series; one without a season/episode
  number lands in the review queue instead of becoming a movie.
- `movie`: every file is a movie, even if the name has something like `1x02`.
- `ignore`: the files stay out of the library, no provider call is made.
- Folders whose name is `Series`, `Serie`, `TV`, `Shows`, `Películas`,
  `Peliculas`, `Pelis`, `Cine`, `Movies` or `Films` are recognised **without a
  rule** (deepest folder name wins); an explicit rule overrides that.
- Changing or removing a rule makes the next scan re-decide the files under it,
  no `force` needed. Files identified by hand are never touched.

The body replaces the whole list. The Config page manages the same list with a
single-choice channel selector and a folder picker that lists the first three
levels of the channel (deeper folders are reached by searching).

## How files are identified

For each video file the parser extracts a title, a year and, for series, season
and episode from the file name and its folder (`Serie/Temporada 2/`,
`Title (2019)/`). It understands `S01E02`, `1x02`, the Spanish `Cap.102`
(= season 1, episode 02; `Cap.1012` = 10×12), `Temporada 1 Episodio 3`, and cuts
the title at quality/language tags (`1080p`, `BluRay`, `Castellano`, `Dual`…).
Files with names like `video_2023.mp4` fall back to the folder name.

Each distinct title is searched **once** (search results are cached for a
week) and the candidates are scored by title similarity and year:

| Score | `status` | Meaning |
| --- | --- | --- |
| ≥ 0.85 | `matched` | Confident. Appears in the library. |
| 0.60 – 0.85 | `review` | Best guess kept, but listed in the review queue. Also used when a series file has no episode number or the episode does not exist at the provider. |
| < 0.60 | `unmatched` | Not in the library. Retried on every scan (a new provider may know it). |
| – | `ignored` | Excluded by hand (trailers, extras). |

A **scan is incremental**: files already identified are not searched again
unless `force` is set, files that disappeared from the index are dropped, and
anything the user identified by hand (`locked`) is never touched. The same
title found in several channels is **one item** with several files.

---

## Catalogue

### List items

```
GET /api/v1/library/items
```

| Query | Default | Notes |
| --- | --- | --- |
| `kind` | – | `movie` or `series`. |
| `search` | – | Substring on title / original title (accents ignored). |
| `genre` | – | Exact genre name (see `/genres`). |
| `status` | – | `review` keeps only items with a file to review. |
| `includeHidden` | `false` | Items whose files all live in hidden channels are excluded unless this (or the server's *show hidden channels* setting) is on. |
| `sortBy` | `title` | `title`, `year`, `added`, `rating`, `lastPlayed`. |
| `sortDescending` | `false` | |
| `page`, `pageSize` | 1, 50 | |

```json
{
  "success": true,
  "data": [
    {
      "id": "66f1…", "kind": "series", "title": "Breaking Bad", "originalTitle": "Breaking Bad", "year": 2008,
      "overview": "…", "genres": ["Drama", "Crimen"], "rating": 8.9, "voteCount": 14000, "runtime": 45,
      "posterUrl": "http://host/api/v1/library/images/aHR0…?size=w342",
      "backdropUrl": "http://host/api/v1/library/images/aHR0…?size=w1280",
      "provider": "tmdb", "providerId": "1396", "imdbId": "tt0903747", "externalIds": { "tmdb": "1396", "imdb": "tt0903747" },
      "locked": false, "fileCount": 62, "channelIds": [1290586824], "seasonCount": 5, "addedAt": "2026-09-10T10:00:00Z",
      "watch": { "completed": false, "inProgress": true, "positionMs": 1250000, "durationMs": 2800000, "progress": 0.4464,
                 "episodesTotal": 62, "episodesWatched": 13, "lastPlayedAt": "2026-09-10T21:00:00Z" }
    }
  ],
  "page": { "page": 1, "pageSize": 50, "totalItems": 1, "totalPages": 1, "hasNext": false, "hasPrevious": false }
}
```

`posterUrl` / `backdropUrl` point at this server, which downloads and caches the
provider image on first request (`?size=` accepts `w92 w154 w185 w300 w342 w500
w780 w1280 original`; non-TMDB sources ignore it).

### Item detail

```
GET /api/v1/library/items/{id}
```

Everything above plus `tagline`, `status`, and:

- movies: `files[]` — the versions of the movie (each an `ApiLibraryFileDto`,
  see below, with its `watch` state), and `resume` (the file in progress, if any);
- series: `seasons[]` — only seasons that have files — each with `episodes[]`
  (`season`, `number`, `title`, `overview`, `stillUrl`, `airDate`, `runtime`,
  `rating`, `files[]`, `watch`, `completed`) and `episodesWatched`; `nextUp` —
  the episode in progress or the first unwatched one after the last watched;
  `resume` — its file when it is in progress.

An `ApiLibraryFileDto` carries the library view of a file (`channelId`,
`fileId`, `fileName`, `folderPath`, `status`, `confidence`, `matchSource`
(`auto`/`manual`), `locked`, `parsed`, `season`, `episode`) **and** the normal
[`ApiFileDto`](files.md) under `file`, with its `streamUrl`. Play it exactly
like any other channel file.

### Continue watching, recent, genres, stats

```
GET /api/v1/library/continue?limit=50        -> files in progress, most recent first (with their item when identified)
GET /api/v1/library/recent?kind=movie&limit=30
GET /api/v1/library/genres?kind=series       -> [{ name, count }]
GET /api/v1/library/stats                    -> settings summary, providers (masked keys), counts by status, last scan
```

`continue` and `stats` work even when the library is disabled: progress is
recorded for every video played, identified or not.

### Files and the review queue

```
GET /api/v1/library/files?status=review&channelId=&search=&page=&pageSize=
GET /api/v1/library/files/{channelId}/{fileId}
```

`status`: `matched`, `review`, `unmatched`, `ignored`. Each file comes with its
`parsed` guess (`title`, `year`, `season`, `episode`, `isSeries`, `source`) and
the `item` it currently points at, which is what a client needs to offer a fix.

---

## Scanning

```
POST /api/v1/library/scan          { "channelId": "1290586824", "force": false }   -> 202
GET  /api/v1/library/scan          -> state of the current / last scan (persisted across restarts)
DELETE /api/v1/library/scan        -> cancel
```

Omit `channelId` to scan the configured channels (`libraryIncludedChannels`, or
every indexed channel when empty, minus `libraryExcludedChannels`).
`409 already_running` while a scan is in progress.
The state carries `running`, `startedAt`, `finishedAt`, `channelsTotal`,
`channelsScanned`, `currentChannel`, `filesSeen`, `filesNew`, `filesRemoved`,
`matched`, `review`, `unmatched`, `failed`, `cancelled` and `error` (set, for
example, when a provider rejects its key; the scan stops there).

---

## Fixing an identification

```
GET /api/v1/library/providers/search?q=Dune&kind=movie&year=1984
GET /api/v1/library/providers/search?imdbId=tt0087182
GET /api/v1/library/providers
```

Searches every enabled provider (or one, with `provider=tmdb`) and returns
candidates: `{ provider, providerId, kind, title, originalTitle, year, overview,
posterUrl, imdbId }`.

```
PUT    /api/v1/library/files/{channelId}/{fileId}/match   { "provider": "tmdb", "providerId": "841", "kind": "movie" }
PUT    /api/v1/library/files/{channelId}/{fileId}/match   { "provider": "tmdb", "providerId": "1396", "kind": "series", "season": 2, "episode": 5 }
DELETE /api/v1/library/files/{channelId}/{fileId}/match   -> back to unmatched (and locked)
POST   /api/v1/library/files/{channelId}/{fileId}/ignore  -> out of the library
DELETE /api/v1/library/files/{channelId}/{fileId}/ignore  -> back in; the next scan identifies it
PUT    /api/v1/library/items/{id}/identify                { "provider": "tmdb", "providerId": "841" }
POST   /api/v1/library/items/{id}/refresh                 -> re-download metadata and episodes
```

`match` assigns **one file**; `identify` moves **every file of an item** to
another title (the fix for "it picked the other movie with the same name"). Both
mark the result `locked` so scans leave it alone. `season` and `episode` are
required when matching a series file. Items that end up without files are
removed automatically.

---

## Playback progress

```
GET    /api/v1/library/watch/{channelId}/{fileId}            -> state or data: null
PUT    /api/v1/library/watch/{channelId}/{fileId}            { "positionMs": 1250000, "durationMs": 2800000, "completed": false }
DELETE /api/v1/library/watch/{channelId}/{fileId}            -> forget it
POST   /api/v1/library/watch/{channelId}/{fileId}/watched    -> mark watched
DELETE /api/v1/library/watch/{channelId}/{fileId}/watched    -> mark not watched, position reset
POST   /api/v1/library/items/{id}/watched                    { "season": 2 }   (body optional)
DELETE /api/v1/library/items/{id}/watched?season=2
GET    /api/v1/library/watch/history?limit=100               -> watched files, most recent first
```

Clients call `PUT` every few seconds while playing and once more when playback
stops. The file becomes `completed` when the position passes
`libraryWatchedThreshold` of the duration, or immediately when `completed: true`
is sent (the player reached the end). The state is **global** (one profile)
and keyed by channel and file, so it survives rescans and re-identifications.

```json
{ "channelId": 1290586824, "fileId": "66f1…", "fileName": "…", "itemId": "66f2…", "season": 2, "episode": 5,
  "positionMs": 1250000, "durationMs": 2800000, "progress": 0.4464, "completed": false, "playCount": 0,
  "firstPlayedAt": "…", "lastPlayedAt": "…" }
```
