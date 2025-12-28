using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Syncfusion.Blazor.FileManager;
using System.Timers;

namespace TelegramDownloader.Shared.MobileFileManager
{
    public partial class MobileFileManager : ComponentBase
    {
        #region Parameters

        [Parameter]
        public string Id { get; set; } = string.Empty;

        [Parameter]
        public bool IsShared { get; set; } = false;

        [Parameter]
        public bool CanCopy { get; set; } = true;

        [Parameter]
        public bool CanCut { get; set; } = true;

        [Parameter]
        public bool CanDelete { get; set; } = true;

        [Parameter]
        public bool CanRename { get; set; } = true;

        [Parameter]
        public bool CanCreate { get; set; } = true;

        [Parameter]
        public bool CanUpload { get; set; } = true;

        [Parameter]
        public bool CanDownloadToLocal { get; set; } = false;

        [Parameter]
        public bool CanShareFile { get; set; } = false;

        [Parameter]
        public bool CanShowInApp { get; set; } = false;

        [Parameter]
        public bool CanShowUrlMedia { get; set; } = false;

        [Parameter]
        public bool CanUploadToTelegram { get; set; } = false;

        [Parameter]
        public bool CanStrm { get; set; } = false;

        [Parameter]
        public bool CanPreload { get; set; } = false;

        [Parameter]
        public bool CanAddToPlaylist { get; set; } = false;

        [Parameter]
        public bool CanSaveToPlaylist { get; set; } = false;

        [Parameter]
        public string RootFolderName { get; set; } = "Root";

        #endregion

        #region Events

        [Parameter]
        public EventCallback<MfmReadEventArgs> OnRead { get; set; }

        [Parameter]
        public EventCallback<MfmDeleteEventArgs> OnItemsDeleting { get; set; }

        [Parameter]
        public EventCallback<MfmMoveEventArgs> OnItemsMoving { get; set; }

        [Parameter]
        public EventCallback<MfmRenameEventArgs> OnItemRenaming { get; set; }

        [Parameter]
        public EventCallback<MfmFolderCreateEventArgs> OnFolderCreating { get; set; }

        [Parameter]
        public EventCallback<MfmSearchEventArgs> OnSearching { get; set; }

        [Parameter]
        public EventCallback<MfmFileOpenEventArgs> OnFileOpen { get; set; }

        [Parameter]
        public EventCallback<MfmDownloadEventArgs> OnBeforeDownload { get; set; }

        [Parameter]
        public EventCallback<string[]> OnSelectedItemsChanged { get; set; }

        [Parameter]
        public EventCallback<MfmDownloadToLocalEventArgs> OnDownloadToLocal { get; set; }

        [Parameter]
        public EventCallback<MfmShareFileEventArgs> OnShareFile { get; set; }

        [Parameter]
        public EventCallback<MfmShowInAppEventArgs> OnShowInApp { get; set; }

        [Parameter]
        public EventCallback<MfmUrlMediaEventArgs> OnUrlMedia { get; set; }

        [Parameter]
        public EventCallback<MfmUploadToTelegramEventArgs> OnUploadToTelegram { get; set; }

        [Parameter]
        public EventCallback<MfmUploadToLocalEventArgs> OnUploadToLocal { get; set; }

        [Parameter]
        public EventCallback<MfmStrmEventArgs> OnStrm { get; set; }

        [Parameter]
        public EventCallback<MfmPreloadFilesEventArgs> OnPreloadFiles { get; set; }

        [Parameter]
        public EventCallback<MfmAddToPlaylistEventArgs> OnAddToPlaylist { get; set; }

        [Parameter]
        public EventCallback<MfmSaveToPlaylistEventArgs> OnSaveToPlaylist { get; set; }

        [Parameter]
        public EventCallback<string> OnPathChanged { get; set; }

        [Parameter]
        public EventCallback<MfmFilterChangedEventArgs> OnFilterChanged { get; set; }

        // Initial values from URL
        [Parameter]
        public string InitialSearch { get; set; } = string.Empty;

        [Parameter]
        public HashSet<string> InitialFilters { get; set; } = new();

        #endregion

        #region State

        public string CurrentPath { get; set; } = "/";
        private FileManagerDirectoryContent? CurrentFolder { get; set; } = null; // Tracks current folder with Id for paste operations
        private string ViewMode { get; set; } = "list";
        private bool IsLoading { get; set; } = false;
        private bool ShowSearch { get; set; } = false;
        private string SearchText { get; set; } = string.Empty;
        private string SortBy { get; set; } = "Name";
        private bool SortAscending { get; set; } = true;

        private List<FileManagerDirectoryContent> Files { get; set; } = new();

        // Cached display files - invalidated when data/filters change
        private List<FileManagerDirectoryContent>? _cachedDisplayFiles;
        private bool _displayFilesDirty = true;
        private List<FileManagerDirectoryContent> DisplayFiles
        {
            get
            {
                if (_displayFilesDirty || _cachedDisplayFiles == null)
                {
                    _cachedDisplayFiles = GetDisplayFiles();
                    _displayFilesDirty = false;
                }
                return _cachedDisplayFiles;
            }
        }

        private List<FileManagerDirectoryContent> PagedFiles => GetPagedFiles();
        private List<FileManagerDirectoryContent> SelectedItems { get; set; } = new();
        private HashSet<string> _selectedIds = new(); // O(1) lookup for selection state
        private List<FileManagerDirectoryContent> ClipboardItems { get; set; } = new();
        private bool IsCutOperation { get; set; } = false;

        // Pagination
        private int CurrentPage { get; set; } = 1;
        private int PageSize { get; set; } = 50;
        private int TotalPages => (int)Math.Ceiling((double)DisplayFiles.Count / PageSize);
        private int TotalItems => DisplayFiles.Count;

        private bool ShowContextMenu { get; set; } = false;
        private FileManagerDirectoryContent? ContextMenuItem { get; set; }

        private bool ShowRenameDialog { get; set; } = false;
        private string RenameText { get; set; } = string.Empty;
        private FileManagerDirectoryContent? RenameItem { get; set; }
        private ElementReference renameInput;

        private bool ShowNewFolderDialog { get; set; } = false;
        private string NewFolderName { get; set; } = string.Empty;
        private ElementReference newFolderInput;

        private ElementReference searchInput;

        private bool ShowDetailsPanel { get; set; } = false;
        private FileManagerDirectoryContent? DetailsItem { get; set; }

        private bool ShowMoreMenu { get; set; } = false;
        private bool ShowFabMenu { get; set; } = false;
        private bool IsFullscreen { get; set; } = false;

        private bool ShowDeleteConfirmDialog { get; set; } = false;
        private FileManagerDirectoryContent[] ItemsToDelete { get; set; } = Array.Empty<FileManagerDirectoryContent>();

        // File type filter (multiple selection)
        private bool ShowFilterDialog { get; set; } = false;
        private HashSet<string> SelectedTypeFilters { get; set; } = new();
        private List<string> AvailableFileTypes => GetAvailableFileTypes();

        private System.Timers.Timer? searchTimer;

        // Track previous Id to detect changes
        private string _previousId = string.Empty;

        #endregion

        #region Lifecycle

        protected override async Task OnInitializedAsync()
        {
            _previousId = Id;

            // Initialize from URL parameters
            if (!string.IsNullOrEmpty(InitialSearch))
            {
                SearchText = InitialSearch;
                ShowSearch = true;
            }

            if (InitialFilters.Count > 0)
            {
                SelectedTypeFilters = new HashSet<string>(InitialFilters);
            }

            await LoadFiles();
        }

        protected override async Task OnParametersSetAsync()
        {
            // Reload if Id changes
            if (_previousId != Id)
            {
                _previousId = Id;
                CurrentPath = "/";
                ResetPagination();
                ClearSelection();
                ClipboardItems.Clear();
                SelectedTypeFilters.Clear(); // Clear filters when Id changes
                Files.Clear();
                await LoadFiles();
            }
        }

        #endregion

        #region File Operations

        private async Task LoadFiles()
        {
            IsLoading = true;
            StateHasChanged();

            try
            {
                CurrentPath = NormalizePath(CurrentPath);

                var args = new MfmReadEventArgs
                {
                    Path = CurrentPath
                };

                await OnRead.InvokeAsync(args);

                // Always update Files list and invalidate cache, even if response is null/empty
                // This prevents showing stale data from previous folder
                if (args.Response?.Files != null)
                {
                    Files = args.Response.Files.Select(f =>
                    {
                        f.FilterPath = NormalizePath(f.FilterPath);
                        return f;
                    }).ToList();
                }
                else
                {
                    // Clear files if response is null to avoid showing stale data
                    Files = new List<FileManagerDirectoryContent>();
                }
                InvalidateDisplayFilesCache();

                if (args.Response?.CWD != null)
                {
                    CurrentFolder = args.Response.CWD;
                    if (CurrentFolder.FilterPath != null)
                    {
                        CurrentFolder.FilterPath = NormalizePath(CurrentFolder.FilterPath);
                    }
                }
                else
                {
                    // Clear CurrentFolder if not in response
                    CurrentFolder = null;
                }
            }
            finally
            {
                IsLoading = false;
                StateHasChanged();
            }
        }

        public async Task RefreshFilesAsync()
        {
            await LoadFiles();
        }

        private void InvalidateDisplayFilesCache()
        {
            _displayFilesDirty = true;
        }

        private bool IsItemSelected(FileManagerDirectoryContent file)
        {
            return _selectedIds.Contains(GetFileUniqueId(file));
        }

        private string GetFileUniqueId(FileManagerDirectoryContent file)
        {
            // Use Id if available, otherwise use FilterPath + Name as unique identifier
            return !string.IsNullOrEmpty(file.Id) ? file.Id : $"{file.FilterPath}{file.Name}";
        }

        private List<FileManagerDirectoryContent> GetDisplayFiles()
        {
            var files = Files ?? new List<FileManagerDirectoryContent>();

            // Apply search filter
            if (!string.IsNullOrEmpty(SearchText))
            {
                files = files.Where(f => f.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Apply file type filter (multiple selection)
            if (SelectedTypeFilters.Count > 0)
            {
                files = files.Where(f => SelectedTypeFilters.Contains(GetFileTypeForFilter(f))).ToList();
            }

            // Apply sorting
            files = SortBy switch
            {
                "Name" => SortAscending ? files.OrderBy(f => !f.IsFile).ThenBy(f => f.Name).ToList() : files.OrderBy(f => !f.IsFile).ThenByDescending(f => f.Name).ToList(),
                "Date" => SortAscending ? files.OrderBy(f => !f.IsFile).ThenBy(f => f.DateModified).ToList() : files.OrderBy(f => !f.IsFile).ThenByDescending(f => f.DateModified).ToList(),
                "Size" => SortAscending ? files.OrderBy(f => !f.IsFile).ThenBy(f => f.Size).ToList() : files.OrderBy(f => !f.IsFile).ThenByDescending(f => f.Size).ToList(),
                "Type" => SortAscending ? files.OrderBy(f => !f.IsFile).ThenBy(f => f.Type).ToList() : files.OrderBy(f => !f.IsFile).ThenByDescending(f => f.Type).ToList(),
                _ => files.OrderBy(f => !f.IsFile).ThenBy(f => f.Name).ToList()
            };

            return files;
        }

        private List<string> GetAvailableFileTypes()
        {
            var files = Files ?? new List<FileManagerDirectoryContent>();
            var types = new List<string>();

            // Add "Folder" type if there are folders
            if (files.Any(f => !f.IsFile))
            {
                types.Add("Folder");
            }

            // Get distinct file types from files
            var fileTypes = files
                .Where(f => f.IsFile && !string.IsNullOrEmpty(f.Type))
                .Select(f => f.Type)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            types.AddRange(fileTypes);

            return types;
        }

        private string GetFileTypeForFilter(FileManagerDirectoryContent file)
        {
            if (!file.IsFile)
            {
                return "Folder";
            }
            return file.Type ?? string.Empty;
        }

        private List<FileManagerDirectoryContent> GetPagedFiles()
        {
            var files = DisplayFiles;
            return files.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
        }

        #endregion

        #region Navigation

        private List<PathSegment> GetPathSegments()
        {
            var segments = new List<PathSegment>();

            // Root segment always maps to path "/"
            segments.Add(new PathSegment { Name = RootFolderName, Path = "/" });

            if (CurrentPath != "/")
            {
                var parts = CurrentPath.Trim('/').Split('/');
                var currentPath = "/";
                foreach (var part in parts)
                {
                    if (!string.IsNullOrEmpty(part))
                    {
                        currentPath += part + "/";

                        // Skip adding if this is the RootFolderName (already added as first segment)
                        if (part == RootFolderName && segments.Count == 1)
                        {
                            continue;
                        }

                        segments.Add(new PathSegment { Name = part, Path = currentPath });
                    }
                }
            }

            return segments;
        }

        private async Task NavigateToPath(string path)
        {
            CurrentPath = path;
            ClearSelection();
            ResetPagination();
            await LoadFiles();
            await OnPathChanged.InvokeAsync(CurrentPath);
        }

        private async Task GoBack()
        {
            if (CurrentPath == "/") return;

            var parts = CurrentPath.Trim('/').Split('/');
            if (parts.Length <= 1)
            {
                // Only one part (e.g., "Files/") - go to root
                CurrentPath = "/";
            }
            else
            {
                // Remove last part
                var newParts = parts.Take(parts.Length - 1).ToArray();

                // If only RootFolderName remains (e.g., "Files"), go to root "/"
                if (newParts.Length == 1 && newParts[0] == RootFolderName)
                {
                    CurrentPath = "/";
                }
                else
                {
                    // Keep the leading "/" to match the format used in NavigateToFolder
                    CurrentPath = "/" + string.Join("/", newParts) + "/";
                }
            }

            ClearSelection();
            ResetPagination();
            await LoadFiles();
            await OnPathChanged.InvokeAsync(CurrentPath);
        }

        private async Task OnFileClick(FileManagerDirectoryContent file)
        {
            if (SelectedItems.Count > 0)
            {
                ToggleSelection(file);
                return;
            }

            if (file.IsFile)
            {
                await OpenFile(file);
            }
            else
            {
                await NavigateToFolder(file);
            }
        }

        private async Task NavigateToFolder(FileManagerDirectoryContent folder)
        {
            string newPath;

            // If folder has FilterPath (e.g., from search results), use it to build correct path
            // FilterPath contains the parent path where the folder is located
            if (!string.IsNullOrEmpty(folder.FilterPath) && folder.FilterPath != "/")
            {
                // FilterPath is the parent path, so we append the folder name
                var parentPath = NormalizePath(folder.FilterPath);
                if (!parentPath.EndsWith("/"))
                {
                    parentPath += "/";
                }
                newPath = parentPath + folder.Name + "/";
            }
            else
            {
                // Normal navigation from current folder
                var basePath = CurrentPath;
                if (!basePath.EndsWith("/"))
                {
                    basePath += "/";
                }
                newPath = basePath + folder.Name + "/";
            }

            if (newPath == CurrentPath)
            {
                await LoadFiles();
                return;
            }

            // Clear search when navigating to a folder
            if (ShowSearch)
            {
                ShowSearch = false;
                SearchText = string.Empty;
            }

            CurrentPath = newPath;
            ClearSelection();
            ResetPagination();
            await LoadFiles();
            await OnPathChanged.InvokeAsync(CurrentPath);
        }

        private async Task OpenFile(FileManagerDirectoryContent file)
        {
            var args = new MfmFileOpenEventArgs
            {
                FileDetails = file
            };
            await OnFileOpen.InvokeAsync(args);
            CloseContextMenu();
        }

        #endregion

        #region Selection

        private void OnFileLongPress(FileManagerDirectoryContent file)
        {
            ContextMenuItem = file;
            ShowContextMenu = true;
        }

        private void ToggleSelection(FileManagerDirectoryContent file)
        {
            var fileId = GetFileUniqueId(file);
            if (_selectedIds.Contains(fileId))
            {
                SelectedItems.Remove(file);
                _selectedIds.Remove(fileId);
            }
            else
            {
                SelectedItems.Add(file);
                _selectedIds.Add(fileId);
            }

            OnSelectedItemsChanged.InvokeAsync(_selectedIds.ToArray());
            StateHasChanged();
        }

        private void ClearSelection()
        {
            SelectedItems.Clear();
            _selectedIds.Clear();
            OnSelectedItemsChanged.InvokeAsync(Array.Empty<string>());
            StateHasChanged();
        }

        private void SelectAll()
        {
            SelectedItems = new List<FileManagerDirectoryContent>(DisplayFiles);
            _selectedIds = new HashSet<string>(SelectedItems.Select(f => GetFileUniqueId(f)));
            ShowMoreMenu = false;
            OnSelectedItemsChanged.InvokeAsync(_selectedIds.ToArray());
            StateHasChanged();
        }

        #endregion

        #region Clipboard Operations

        private void CopySelected()
        {
            ClipboardItems = new List<FileManagerDirectoryContent>(SelectedItems);
            IsCutOperation = false;
            ClearSelection();
        }

        private void CopyItem(FileManagerDirectoryContent item)
        {
            ClipboardItems = new List<FileManagerDirectoryContent> { item };
            IsCutOperation = false;
            CloseContextMenu();
        }

        private void CutSelected()
        {
            ClipboardItems = new List<FileManagerDirectoryContent>(SelectedItems);
            IsCutOperation = true;
            ClearSelection();
        }

        private void CutItem(FileManagerDirectoryContent item)
        {
            ClipboardItems = new List<FileManagerDirectoryContent> { item };
            IsCutOperation = true;
            CloseContextMenu();
        }

        private async Task PasteItems()
        {
            if (ClipboardItems.Count == 0) return;

            // Normalize paths to use forward slashes
            var normalizedTargetPath = NormalizePath(CurrentPath);
            var normalizedSourcePath = NormalizePath(ClipboardItems.First().FilterPath);

            // Normalize FilterPath in clipboard items before sending
            var normalizedClipboardItems = ClipboardItems.Select(f =>
            {
                // Clone to avoid modifying original
                return new FileManagerDirectoryContent
                {
                    Id = f.Id,
                    Name = f.Name,
                    FilterPath = NormalizePath(f.FilterPath),
                    FilterId = f.FilterId,
                    IsFile = f.IsFile,
                    Size = f.Size,
                    DateCreated = f.DateCreated,
                    DateModified = f.DateModified,
                    Type = f.Type,
                    HasChild = f.HasChild,
                    ParentId = f.ParentId
                };
            }).ToArray();

            // Use CurrentFolder from LoadFiles response which has the correct Id
            // This is crucial for the database copy operation to set correct ParentId
            FileManagerDirectoryContent targetFolder;
            if (CurrentFolder != null)
            {
                targetFolder = new FileManagerDirectoryContent
                {
                    Id = CurrentFolder.Id,
                    Name = CurrentFolder.Name,
                    // Preserve empty FilterPath for root folder - don't normalize it to "/"
                    FilterPath = string.IsNullOrEmpty(CurrentFolder.FilterPath) ? "" : NormalizePath(CurrentFolder.FilterPath),
                    FilterId = CurrentFolder.FilterId ?? "",
                    IsFile = false
                };
            }
            else
            {
                // Fallback for root or if CWD wasn't available
                targetFolder = new FileManagerDirectoryContent
                {
                    FilterPath = normalizedTargetPath,
                    IsFile = false,
                    Name = normalizedTargetPath.TrimEnd('/').Split('/').LastOrDefault() ?? ""
                };
            }

            var args = new MfmMoveEventArgs
            {
                Files = normalizedClipboardItems,
                SourcePath = normalizedSourcePath,
                TargetPath = normalizedTargetPath,
                TargetData = targetFolder,
                IsCopy = !IsCutOperation
            };

            await OnItemsMoving.InvokeAsync(args);

            ClipboardItems.Clear();

            await LoadFiles();
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";

            // Replace backslashes with forward slashes
            var normalized = path.Replace("\\", "/");

            // Remove duplicate slashes
            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }

            // Only root path should start with "/", other paths should not have leading "/"
            // Server expects: "/" for root, "Files/Folder/" for subfolders
            // Don't add leading "/" here - let the caller decide the format

            return normalized;
        }

        #endregion

        #region Delete

        private void DeleteSelected()
        {
            if (SelectedItems.Count == 0) return;

            ItemsToDelete = SelectedItems.ToArray();
            ShowDeleteConfirmDialog = true;
        }

        private void DeleteItem(FileManagerDirectoryContent item)
        {
            CloseContextMenu();
            ItemsToDelete = new[] { item };
            ShowDeleteConfirmDialog = true;
        }

        private async Task ConfirmDelete()
        {
            if (ItemsToDelete.Length == 0) return;

            var args = new MfmDeleteEventArgs
            {
                Files = ItemsToDelete,
                Path = CurrentPath
            };

            await OnItemsDeleting.InvokeAsync(args);

            CloseDeleteConfirmDialog();
            ClearSelection();
            await LoadFiles();
        }

        private void CloseDeleteConfirmDialog()
        {
            ShowDeleteConfirmDialog = false;
            ItemsToDelete = Array.Empty<FileManagerDirectoryContent>();
        }

        #endregion

        #region Rename

        private async Task RenameSelected()
        {
            if (SelectedItems.Count != 1) return;
            RenameItem = SelectedItems.First();
            RenameText = RenameItem.Name;
            ShowRenameDialog = true;
            ClearSelection();
            await FocusAndSelectRenameInput();
        }

        private async Task StartRenameItem(FileManagerDirectoryContent item)
        {
            RenameItem = item;
            RenameText = item.Name;
            ShowRenameDialog = true;
            CloseContextMenu();
            await FocusAndSelectRenameInput();
        }

        private async Task FocusAndSelectRenameInput()
        {
            StateHasChanged();
            await Task.Delay(50);
            try
            {
                await renameInput.FocusAsync();
                await JSRuntime.InvokeVoidAsync("eval", "document.activeElement.select()");
            }
            catch { }
        }

        private async Task OnRenameKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                await ConfirmRename();
            }
            else if (e.Key == "Escape")
            {
                CloseRenameDialog();
            }
        }

        private async Task ConfirmRename()
        {
            if (RenameItem == null || string.IsNullOrWhiteSpace(RenameText)) return;

            var args = new MfmRenameEventArgs
            {
                File = RenameItem,
                NewName = RenameText,
                Path = RenameItem.FilterPath
            };

            await OnItemRenaming.InvokeAsync(args);

            CloseRenameDialog();
            await LoadFiles();
        }

        private void CloseRenameDialog()
        {
            ShowRenameDialog = false;
            RenameItem = null;
            RenameText = string.Empty;
        }

        #endregion

        #region New Folder

        private async Task CreateNewFolder()
        {
            ShowFabMenu = false;
            NewFolderName = "New Folder";
            ShowNewFolderDialog = true;
            StateHasChanged();

            // Wait for the dialog to render, then focus and select all text
            await Task.Delay(50);
            try
            {
                await newFolderInput.FocusAsync();
                await JSRuntime.InvokeVoidAsync("eval", "document.activeElement.select()");
            }
            catch { }
        }

        private async Task OnNewFolderKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                await ConfirmNewFolder();
            }
            else if (e.Key == "Escape")
            {
                CloseNewFolderDialog();
            }
        }

        private async Task ConfirmNewFolder()
        {
            if (string.IsNullOrWhiteSpace(NewFolderName)) return;

            // Use CurrentFolder which has the correct Id from LoadFiles response
            FileManagerDirectoryContent parentFolder;
            if (CurrentFolder != null)
            {
                parentFolder = new FileManagerDirectoryContent
                {
                    Id = CurrentFolder.Id,
                    Name = CurrentFolder.Name,
                    FilterPath = NormalizePath(CurrentFolder.FilterPath ?? ""),
                    FilterId = CurrentFolder.FilterId,
                    IsFile = false
                };
            }
            else
            {
                // Fallback for root - this shouldn't normally happen as LoadFiles sets CurrentFolder
                parentFolder = new FileManagerDirectoryContent
                {
                    FilterPath = NormalizePath(CurrentPath),
                    IsFile = false
                };
            }

            var args = new MfmFolderCreateEventArgs
            {
                FolderName = NewFolderName,
                Path = NormalizePath(CurrentPath),
                ParentFolder = parentFolder
            };

            await OnFolderCreating.InvokeAsync(args);

            CloseNewFolderDialog();
            await LoadFiles();
        }

        private void CloseNewFolderDialog()
        {
            ShowNewFolderDialog = false;
            NewFolderName = string.Empty;
        }

        #endregion

        #region Download

        private async Task DownloadSelected()
        {
            if (SelectedItems.Count == 0) return;

            var args = new MfmDownloadEventArgs
            {
                Names = SelectedItems.Select(f => f.Name).ToArray(),
                Path = CurrentPath,
                Files = SelectedItems.ToArray()
            };

            await OnBeforeDownload.InvokeAsync(args);
            ClearSelection();
        }

        private async Task DownloadItem(FileManagerDirectoryContent item)
        {
            var args = new MfmDownloadEventArgs
            {
                Names = new[] { item.Name },
                Path = item.FilterPath,
                Files = new[] { item }
            };

            await OnBeforeDownload.InvokeAsync(args);
            CloseContextMenu();
        }

        #endregion

        #region Additional Actions

        private async Task DownloadToLocalSelected()
        {
            if (SelectedItems.Count == 0) return;

            var args = new MfmDownloadToLocalEventArgs
            {
                Files = SelectedItems.ToArray(),
                Path = CurrentPath
            };

            await OnDownloadToLocal.InvokeAsync(args);
            ClearSelection();
        }

        private async Task DownloadToLocalItem(FileManagerDirectoryContent item)
        {
            var args = new MfmDownloadToLocalEventArgs
            {
                Files = new[] { item },
                Path = item.FilterPath
            };

            await OnDownloadToLocal.InvokeAsync(args);
            CloseContextMenu();
        }

        private async Task ShareFolderItem(FileManagerDirectoryContent item)
        {
            if (item.IsFile) return; // Only folders can be shared

            var args = new MfmShareFileEventArgs
            {
                File = item
            };

            await OnShareFile.InvokeAsync(args);
            CloseContextMenu();
        }

        private async Task ShareSelectedFolder()
        {
            // Only share if exactly one folder is selected
            var folder = SelectedItems.FirstOrDefault(x => !x.IsFile);
            if (folder == null || SelectedItems.Count != 1) return;

            var args = new MfmShareFileEventArgs
            {
                File = folder
            };

            await OnShareFile.InvokeAsync(args);
            ClearSelection();
        }

        private async Task ShowInAppItem(FileManagerDirectoryContent item)
        {
            var args = new MfmShowInAppEventArgs
            {
                File = item
            };

            await OnShowInApp.InvokeAsync(args);
            CloseContextMenu();
        }

        private async Task UrlMediaItem(FileManagerDirectoryContent item)
        {
            var args = new MfmUrlMediaEventArgs
            {
                File = item
            };

            await OnUrlMedia.InvokeAsync(args);
            CloseContextMenu();
        }

        private async Task UploadToTelegramSelected()
        {
            if (SelectedItems.Count == 0) return;

            var args = new MfmUploadToTelegramEventArgs
            {
                Files = SelectedItems.ToArray(),
                Path = CurrentPath
            };

            await OnUploadToTelegram.InvokeAsync(args);
            ClearSelection();
        }

        private async Task UploadToLocal()
        {
            var args = new MfmUploadToLocalEventArgs
            {
                Path = CurrentPath
            };

            await OnUploadToLocal.InvokeAsync(args);
            ShowFabMenu = false;
        }

        private async Task StrmItem(FileManagerDirectoryContent item)
        {
            if (item.IsFile) return; // STRM only works on folders

            var args = new MfmStrmEventArgs
            {
                Folder = item,
                Path = item.FilterPath + item.Name + "/"
            };

            await OnStrm.InvokeAsync(args);
            CloseContextMenu();
        }

        private async Task StrmSelected()
        {
            // Only works if exactly one folder is selected
            var folder = SelectedItems.FirstOrDefault(x => !x.IsFile);
            if (folder == null) return;

            var args = new MfmStrmEventArgs
            {
                Folder = folder,
                Path = folder.FilterPath + folder.Name + "/"
            };

            await OnStrm.InvokeAsync(args);
            ClearSelection();
        }

        private async Task PreloadSelected()
        {
            if (SelectedItems.Count == 0) return;

            var args = new MfmPreloadFilesEventArgs
            {
                Items = SelectedItems.ToArray(),
                Path = CurrentPath
            };

            await OnPreloadFiles.InvokeAsync(args);
            ClearSelection();
        }

        private async Task AddToPlaylistItem(FileManagerDirectoryContent item)
        {
            var args = new MfmAddToPlaylistEventArgs
            {
                File = item,
                Title = item.Name
            };

            await OnAddToPlaylist.InvokeAsync(args);
            CloseContextMenu();
        }

        private async Task AddSelectedToPlaylist()
        {
            var audioFiles = SelectedItems.Where(f => IsAudioFile(f)).ToArray();
            if (audioFiles.Length == 0) return;

            var args = new MfmAddToPlaylistEventArgs
            {
                Files = audioFiles
            };

            await OnAddToPlaylist.InvokeAsync(args);
            ClearSelection();
        }

        private bool HasSelectedAudioFiles()
        {
            return SelectedItems.Any(f => IsAudioFile(f));
        }

        private bool IsAudioFile(FileManagerDirectoryContent file)
        {
            if (!file.IsFile || string.IsNullOrEmpty(file.Type)) return false;
            var audioTypes = new HashSet<string> { ".mp3", ".ogg", ".flac", ".aac", ".wav", ".m4a" };
            return audioTypes.Contains(file.Type.ToLower());
        }

        private async Task SaveToPlaylistItem(FileManagerDirectoryContent item)
        {
            var args = new MfmSaveToPlaylistEventArgs
            {
                File = item,
                ChannelId = Id
            };

            await OnSaveToPlaylist.InvokeAsync(args);
            CloseContextMenu();
        }

        private async Task SaveSelectedToPlaylist()
        {
            var audioFiles = SelectedItems.Where(f => IsAudioFile(f)).ToArray();
            if (audioFiles.Length == 0) return;

            var args = new MfmSaveToPlaylistEventArgs
            {
                Files = audioFiles,
                ChannelId = Id
            };

            await OnSaveToPlaylist.InvokeAsync(args);
            ClearSelection();
        }

        #endregion

        #region Search

        private async Task ToggleSearch()
        {
            ShowSearch = !ShowSearch;
            ShowMoreMenu = false;
            if (!ShowSearch)
            {
                SearchText = string.Empty;
            }
            StateHasChanged();

            // Focus on search input after showing
            if (ShowSearch)
            {
                await Task.Delay(50); // Small delay to ensure the input is rendered
                try
                {
                    await searchInput.FocusAsync();
                }
                catch { /* Input may not be rendered yet */ }
            }
        }

        private async Task OnSearchKeyUp(KeyboardEventArgs e)
        {
            // Debounce search
            searchTimer?.Stop();
            searchTimer?.Dispose();
            searchTimer = new System.Timers.Timer(500);
            searchTimer.Elapsed += async (sender, args) =>
            {
                searchTimer?.Stop();
                await InvokeAsync(async () =>
                {
                    ResetPagination();
                    if (!string.IsNullOrEmpty(SearchText))
                    {
                        var searchArgs = new MfmSearchEventArgs
                        {
                            Path = CurrentPath,
                            SearchText = SearchText
                        };
                        await OnSearching.InvokeAsync(searchArgs);

                        // Always update Files and invalidate cache to avoid showing stale data
                        if (searchArgs.Response?.Files != null)
                        {
                            // Normalize FilterPath in search results
                            Files = searchArgs.Response.Files.Select(f =>
                            {
                                f.FilterPath = NormalizePath(f.FilterPath);
                                return f;
                            }).ToList();
                        }
                        else
                        {
                            // No results or error - show empty list
                            Files = new List<FileManagerDirectoryContent>();
                        }
                        InvalidateDisplayFilesCache();
                    }
                    else
                    {
                        await LoadFiles();
                    }
                    await NotifyFilterChanged();
                    StateHasChanged();
                });
            };
            searchTimer.Start();
        }

        private async Task ClearSearchText()
        {
            SearchText = string.Empty;
            ResetPagination();
            await LoadFiles();
            await NotifyFilterChanged();
        }

        private async Task CloseSearch()
        {
            ShowSearch = false;
            SearchText = string.Empty;
            ResetPagination();
            await LoadFiles();
            await NotifyFilterChanged();
        }

        #endregion

        #region UI Helpers

        private void ToggleViewMode()
        {
            ViewMode = ViewMode == "grid" ? "list" : "grid";
        }

        private void ToggleFullscreen()
        {
            IsFullscreen = !IsFullscreen;
            StateHasChanged();
        }

        private void ShowMoreOptions()
        {
            ShowMoreMenu = true;
        }

        private async Task RefreshFiles()
        {
            ShowMoreMenu = false;
            await LoadFiles();
        }

        private void SortFiles()
        {
            SortBy = SortBy switch
            {
                "Name" => "Date",
                "Date" => "Size",
                "Size" => "Type",
                "Type" => "Name",
                _ => "Name"
            };
            InvalidateDisplayFilesCache();
            ResetPagination();
            StateHasChanged();
        }

        private void ToggleSortDirection()
        {
            SortAscending = !SortAscending;
            InvalidateDisplayFilesCache();
            ResetPagination();
            StateHasChanged();
        }

        private void ToggleFabMenu()
        {
            ShowFabMenu = !ShowFabMenu;
        }

        private void CloseContextMenu()
        {
            ShowContextMenu = false;
            ContextMenuItem = null;
        }

        private void ShowDetails(FileManagerDirectoryContent item)
        {
            DetailsItem = item;
            ShowDetailsPanel = true;
            CloseContextMenu();
        }

        private void CloseDetailsPanel()
        {
            ShowDetailsPanel = false;
            DetailsItem = null;
        }

        private void OpenFilterDialog()
        {
            ShowMoreMenu = false;
            ShowFilterDialog = true;
        }

        private void CloseFilterDialog()
        {
            ShowFilterDialog = false;
        }

        private void ToggleTypeFilter(string type)
        {
            if (SelectedTypeFilters.Contains(type))
            {
                SelectedTypeFilters.Remove(type);
            }
            else
            {
                SelectedTypeFilters.Add(type);
            }
            InvalidateDisplayFilesCache();
            ResetPagination();
            StateHasChanged();
        }

        private void SelectAllTypeFilters()
        {
            SelectedTypeFilters = new HashSet<string>(AvailableFileTypes);
            InvalidateDisplayFilesCache();
            ResetPagination();
            StateHasChanged();
        }

        private void ClearTypeFilters()
        {
            SelectedTypeFilters.Clear();
            InvalidateDisplayFilesCache();
            ResetPagination();
            StateHasChanged();
        }

        private async Task ApplyFiltersAndClose()
        {
            ShowFilterDialog = false;
            await NotifyFilterChanged();
            StateHasChanged();
        }

        private async Task NotifyFilterChanged()
        {
            var args = new MfmFilterChangedEventArgs
            {
                SearchText = SearchText,
                TypeFilters = new HashSet<string>(SelectedTypeFilters)
            };
            await OnFilterChanged.InvokeAsync(args);
        }

        private void UploadFiles()
        {
            ShowFabMenu = false;
            // TODO: Implement file upload
        }

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        #endregion

        #region Pagination

        private void GoToFirstPage()
        {
            CurrentPage = 1;
            StateHasChanged();
        }

        private void GoToPreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                StateHasChanged();
            }
        }

        private void GoToNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                StateHasChanged();
            }
        }

        private void GoToLastPage()
        {
            CurrentPage = TotalPages;
            StateHasChanged();
        }

        private void GoToPage(int page)
        {
            if (page >= 1 && page <= TotalPages)
            {
                CurrentPage = page;
                StateHasChanged();
            }
        }

        private void ResetPagination()
        {
            CurrentPage = 1;
        }

        private IEnumerable<int> GetVisiblePageNumbers()
        {
            const int maxVisiblePages = 5;
            int startPage = Math.Max(1, CurrentPage - maxVisiblePages / 2);
            int endPage = Math.Min(TotalPages, startPage + maxVisiblePages - 1);

            if (endPage - startPage + 1 < maxVisiblePages)
            {
                startPage = Math.Max(1, endPage - maxVisiblePages + 1);
            }

            return Enumerable.Range(startPage, endPage - startPage + 1);
        }

        #endregion

        #region Public Methods

        public List<FileManagerDirectoryContent> GetSelectedFiles()
        {
            return SelectedItems;
        }

        public string Path
        {
            get => CurrentPath;
            set => CurrentPath = value;
        }

        /// <summary>
        /// Navigates to the specified path without triggering OnPathChanged event.
        /// Useful when initializing from URL to avoid infinite loops.
        /// </summary>
        public async Task NavigateToPathSilent(string path)
        {
            if (string.IsNullOrEmpty(path) || path == CurrentPath)
                return;

            CurrentPath = NormalizePath(path);
            ClearSelection();
            ResetPagination();
            await LoadFiles();
        }

        #endregion

        private class PathSegment
        {
            public string Name { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
        }
    }
}
