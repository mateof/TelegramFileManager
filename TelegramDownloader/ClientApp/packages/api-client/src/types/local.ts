// Server local storage: /api/v1/local. Entries, listings and request bodies are the shared ones of
// ./files (ApiFileDto, ApiFolderContentsDto, BrowseQuery, Local*Request); this file holds the
// non-Api models the controller returns directly (Models/FileModel.cs).

/** Size of a local folder tree (mirrors `Models.DirectorySizeModel`, `GET /api/v1/local/size`). */
export interface DirectorySizeModel {
  sizeBytes: number;
  sizeWithSuffix: string | null;
  totalElements: number;
  filesByType: FileTypeInfo[];
}

/** Per extension breakdown of a {@link DirectorySizeModel} (mirrors `Models.FileTypeInfo`). */
export interface FileTypeInfo {
  extension: string | null;
  category: string | null;
  icon: string | null;
  count: number;
  sizeBytes: number;
  sizeWithSuffix: string | null;
}

/** Query of `GET /api/v1/local/info` and `/download`. */
export interface LocalPathQuery {
  /** Path relative to the local root. */
  path: string;
}

/** Query of `GET /api/v1/local/size`. */
export interface LocalSizeQuery {
  /** Folder relative to the local root. Omit for the whole root. */
  path?: string;
}
