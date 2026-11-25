/**
 * Labels of the default UI. English by default; apps pass translations through
 * the `strings` prop of <FileManager/>. `{name}` placeholders are interpolated.
 */
export interface FileManagerStrings {
  back: string;
  forward: string;
  up: string;
  refresh: string;
  search: string;
  searchPlaceholder: string;
  clearSearch: string;
  viewMode: string;
  viewList: string;
  viewGrid: string;
  viewDetails: string;
  sortBy: string;
  sortName: string;
  sortSize: string;
  sortModified: string;
  sortCreated: string;
  sortType: string;
  ascending: string;
  descending: string;
  filter: string;
  filterAll: string;
  kindAudio: string;
  kindVideo: string;
  kindImage: string;
  kindDocument: string;
  kindArchive: string;
  kindText: string;
  kindOther: string;
  newFolder: string;
  newFolderName: string;
  create: string;
  cancel: string;
  upload: string;
  dropToUpload: string;
  dropToUploadHint: string;
  open: string;
  rename: string;
  cut: string;
  copy: string;
  paste: string;
  delete: string;
  download: string;
  details: string;
  selectAll: string;
  clearSelection: string;
  selectionMode: string;
  more: string;
  actions: string;
  name: string;
  size: string;
  modified: string;
  created: string;
  type: string;
  location: string;
  contents: string;
  folder: string;
  file: string;
  /** `{count}` */
  items: string;
  /** `{count}` */
  selected: string;
  /** `{count}` */
  searchResults: string;
  emptyFolder: string;
  emptyFolderHint: string;
  noMatches: string;
  noMatchesHint: string;
  loadError: string;
  retry: string;
  loadingMore: string;
  uploads: string;
  uploadQueued: string;
  uploadUploading: string;
  uploadDone: string;
  uploadError: string;
  uploadCanceled: string;
  remove: string;
  clearFinished: string;
  cancelAll: string;
  collapse: string;
  expand: string;
  folders: string;
  toggleTree: string;
  toggleDetails: string;
  noSelection: string;
  noSelectionHint: string;
  totalSize: string;
  /** `{count}` entries on the clipboard. */
  clipboardCut: string;
  clipboardCopied: string;
  items_one: string;
}

export const defaultStrings: FileManagerStrings = {
  back: 'Back',
  forward: 'Forward',
  up: 'Up one level',
  refresh: 'Refresh',
  search: 'Search',
  searchPlaceholder: 'Search files...',
  clearSearch: 'Clear search',
  viewMode: 'View',
  viewList: 'List',
  viewGrid: 'Grid',
  viewDetails: 'Details',
  sortBy: 'Sort by',
  sortName: 'Name',
  sortSize: 'Size',
  sortModified: 'Date modified',
  sortCreated: 'Date created',
  sortType: 'Type',
  ascending: 'Ascending',
  descending: 'Descending',
  filter: 'Filter',
  filterAll: 'All files',
  kindAudio: 'Audio',
  kindVideo: 'Video',
  kindImage: 'Images',
  kindDocument: 'Documents',
  kindArchive: 'Archives',
  kindText: 'Text',
  kindOther: 'Other',
  newFolder: 'New folder',
  newFolderName: 'Folder name',
  create: 'Create',
  cancel: 'Cancel',
  upload: 'Upload',
  dropToUpload: 'Drop files to upload',
  dropToUploadHint: 'They will be uploaded to this folder',
  open: 'Open',
  rename: 'Rename',
  cut: 'Cut',
  copy: 'Copy',
  paste: 'Paste',
  delete: 'Delete',
  download: 'Download',
  details: 'Details',
  selectAll: 'Select all',
  clearSelection: 'Clear selection',
  selectionMode: 'Select',
  more: 'More',
  actions: 'Actions',
  name: 'Name',
  size: 'Size',
  modified: 'Modified',
  created: 'Created',
  type: 'Type',
  location: 'Location',
  contents: 'Contents',
  folder: 'Folder',
  file: 'File',
  items: '{count} items',
  items_one: '1 item',
  selected: '{count} selected',
  searchResults: '{count} results',
  emptyFolder: 'This folder is empty',
  emptyFolderHint: 'Upload files or create a folder to get started.',
  noMatches: 'Nothing matches',
  noMatchesHint: 'Try another search or filter.',
  loadError: 'Could not load this folder',
  retry: 'Retry',
  loadingMore: 'Loading more...',
  uploads: 'Uploads',
  uploadQueued: 'Waiting',
  uploadUploading: 'Uploading',
  uploadDone: 'Uploaded',
  uploadError: 'Failed',
  uploadCanceled: 'Canceled',
  remove: 'Remove',
  clearFinished: 'Clear finished',
  cancelAll: 'Cancel all',
  collapse: 'Collapse',
  expand: 'Expand',
  folders: 'Folders',
  toggleTree: 'Folder tree',
  toggleDetails: 'Details panel',
  noSelection: 'Nothing selected',
  noSelectionHint: 'Select a file to see its details.',
  totalSize: 'Total size',
  clipboardCut: '{count} cut, paste them in another folder',
  clipboardCopied: '{count} copied, paste them in another folder'
};

/** Replaces `{name}` placeholders. */
export function fmt(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (m, k: string) => (k in vars ? String(vars[k]) : m));
}
