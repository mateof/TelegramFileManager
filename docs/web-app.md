# The web app (`/app`)

Besides the classic interface at `/`, TelegramFileManager serves a new web interface at
**`http://<server>:<port>/app/`**. It is a React app built on the [REST API v1](api/README.md),
made for desktop and mobile, and it works over the same Telegram session and settings as the
classic interface: switch between them with **Classic UI** in the top bar or the **New interface**
button of the classic side menu. Setup, access and the default-interface option are described in
the [README](../README.md#web-interface-app).

What it includes:

- **File manager** for every channel: folders, details / list / grid views with thumbnails,
  multiple selection, folder tree, details panel, new folders, uploads, downloads to your device
  or to the server, rename / copy / move / delete (deletes show their progress and can be
  cancelled).
- **Local files**: the server's storage, with upload to a Telegram channel.
- **Media library**: movies and series identified from the channel indexes (TMDB / OMDb), with
  posters, "continue watching", seasons and episodes, and a review queue for files that could
  not be matched.
- **Music player** with a queue, cover art and tags read from the files, shuffle / repeat, sleep
  timer and **playlists**; it keeps playing while you browse.
- **Viewers** for video (with watch progress for the library), images (gallery of the folder),
  PDF and text.
- **Transfers** in real time over SignalR: downloads, uploads and batch tasks with speed chart,
  pause / stop, the queue and the saved tasks.
- **Global search** across every channel and the local storage.
- Channel **messages**, channel refresh with live progress, **shared collections**, logs, system
  metrics and maintenance.
- **Settings** for the server, plus appearance (themes, accent colour, effects, density) and the
  language: **Español, Galego and English**.
- Command palette (`Ctrl/Cmd+K`), mobile layout with a bottom navigation bar and a drawer, and the
  player always visible.
- **Installable app (PWA)** with an offline shell and an update prompt (needs HTTPS or
  `localhost`), sign-in with the web password or **passkeys**.

> The screenshots below use demo data: the channels, files, titles and artwork are invented and
> were generated for this page.

## Channels

<img src="screenshots/web/channels-overview.webp" alt="Channels overview" />

The channels of the account, with favourites, ownership and whether they are indexed; the
sidebar lists them too (all, mine, favourites, shared, Telegram folders).

## File manager

<img src="screenshots/web/file-manager.webp" alt="File manager in details view with two files selected" />

Details view with a selection: the bar shows the actions that apply to it (play, download, download
to the server, queue…).

<img src="screenshots/web/file-manager-grid.webp" alt="File manager in grid view with photo thumbnails" />

Grid view with thumbnails of a photo folder.

## Media library

<img src="screenshots/web/library.webp" alt="Media library home" />

The library home: the title to resume, "continue watching" and the latest additions.

<img src="screenshots/web/library-grid.webp" alt="All the titles of the library with their posters" />

All the titles, with filters by genre and year and the watch progress of each one.

<img src="screenshots/web/library-item.webp" alt="Detail page of a movie" />

A movie: resume, play from the start or mark it as watched.

## Music player

<img src="screenshots/web/player.webp" alt="Expanded music player with the queue" />

The expanded player with the queue; collapsed, it stays as a bar at the bottom of every page.

## Viewers

<img src="screenshots/web/video-viewer.webp" alt="Video viewer" />

<img src="screenshots/web/image-viewer.webp" alt="Image viewer" />

## Transfers

<img src="screenshots/web/transfers.webp" alt="Transfers page with active downloads and a delete in progress" />

Live downloads, uploads and tasks with the speed chart; bottom left, the progress card of a folder
being deleted.

## Search

<img src="screenshots/web/search.webp" alt="Global search results grouped by channel" />

## Settings and themes

<img src="screenshots/web/settings-appearance.webp" alt="Appearance settings: themes, accent colour, effects and language" />

<img src="screenshots/web/file-manager-light.webp" alt="File manager with the light theme" />

## Mobile

<p>
  <img src="screenshots/web/mobile-file-manager.webp" alt="File manager on a phone with the mini player" width="260" />
  <img src="screenshots/web/mobile-drawer.webp" alt="Navigation drawer with the channels" width="260" />
  <img src="screenshots/web/mobile-player.webp" alt="Player on a phone" width="260" />
</p>
