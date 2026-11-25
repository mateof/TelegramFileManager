// Shared collections and STRM export: /api/v1/shares (mirrors the share classes of
// Models/Api/SystemDtos.cs and Models/ShareFilesModel.cs)
import type { PagedQuery } from '../http';
import type { InvitationInfo } from './channels';
import type { BsonFileManagerModel } from './files';

/** A shared file collection published by another user. */
export interface SharedCollectionDto {
  id: string;
  name: string | null;
  description: string | null;
  channelId: string | null;
  collectionId: string | null;
  /** ISO date. */
  dateCreated: string;
  /** ISO date. */
  dateModified: string;
}

/**
 * Share payload (mirrors `Models.ShareFilesModel`): returned by `GET /api/v1/shares/export` and
 * posted back, as `share`, to `POST /api/v1/shares/import` on another instance.
 */
export interface ShareFilesModel {
  /** Channel id. */
  id: string | null;
  name: string | null;
  chatName: string | null;
  fileName: string | null;
  description: string | null;
  invitation: InvitationInfo | null;
  files: BsonFileManagerModel[] | null;
}

/** Body of `POST /api/v1/shares/import`. */
export interface ImportSharedRequest {
  /** Share payload, normally obtained from `GET /api/v1/shares/export`. */
  share: ShareFilesModel;
}

/** Body of `POST /api/v1/shares/strm` (named after `POST /api/v1/channels/{id}/strm` on the server). */
export interface CreateStrmRequest {
  /** Channel folder to export, e.g. `/movies/`. */
  path?: string;
  /** Base URL written inside the .strm files. Defaults to the request host. */
  host?: string | null;
  /**
   * When set, .strm files are written to this folder under the server local root instead of
   * being returned as a zip download link.
   */
  destinationFolder?: string | null;
}

/** Query of `GET /api/v1/shares`. */
export interface SharesListQuery extends PagedQuery {
  /** Name filter. */
  filter?: string;
}

/** Query of `GET /api/v1/shares/export`. */
export interface ShareExportQuery {
  /** Channel that holds the files. */
  channelId: string;
  /** Folder to share. Omit to share the whole channel. */
  folderId?: string;
  /** Label for the share. */
  name?: string;
}
