// Playlists: /api/v1/playlists. The controller returns the storage models directly
// (mirrors Models/PlaylistModel.cs).
import type { PagedQuery } from '../http';

/** A playlist with all of its tracks, in order. */
export interface PlaylistModel {
  /** MongoDB ObjectId. */
  id: string;
  name: string;
  description: string | null;
  tracks: PlaylistTrackModel[];
  /** ISO date. */
  dateCreated: string;
  /** ISO date. */
  dateModified: string;
  /** Computed on the server (`tracks.length`). */
  trackCount: number;
}

/** One track of a playlist. */
export interface PlaylistTrackModel {
  fileId: string;
  channelId: string;
  channelName: string;
  fileName: string;
  filePath: string;
  fileType: string;
  fileSize: number;
  order: number;
  /** Direct URL for local files. When set, the track is a local file and this URL should be used directly. */
  directUrl: string | null;
  /** ISO date. */
  dateAdded: string;
  /** Computed on the server: true if this is a local file (has `directUrl` set). */
  isLocalFile: boolean;
}

/**
 * Body of `POST /api/v1/playlists` and `PUT /api/v1/playlists/{id}` (a `PlaylistModel`; the
 * server fills the id, the dates and the computed fields).
 */
export interface PlaylistWriteRequest {
  name: string;
  description?: string | null;
  tracks?: PlaylistTrackWriteRequest[];
}

/**
 * Body of `POST /api/v1/playlists/{id}/tracks` (a `PlaylistTrackModel` without the server side
 * fields). Either `fileId` (channel file) or `directUrl` (local file) is required; `order` and
 * `dateAdded` are set by the server.
 */
export interface PlaylistTrackWriteRequest {
  fileId?: string;
  channelId?: string;
  channelName?: string;
  fileName?: string;
  filePath?: string;
  fileType?: string;
  fileSize?: number;
  order?: number;
  directUrl?: string | null;
}

/** `sortBy` values of `GET /api/v1/playlists`: `name` (default), `date`, `tracks`. */
export type PlaylistSortBy = 'name' | 'date' | 'tracks';

/** Query of `GET /api/v1/playlists`. */
export interface PlaylistListQuery extends PagedQuery {
  sortBy?: PlaylistSortBy;
}

/** Query of `POST /api/v1/playlists/{id}/download`. */
export interface PlaylistDownloadQuery {
  /** Folder relative to the local root. */
  destinationFolder?: string;
}
