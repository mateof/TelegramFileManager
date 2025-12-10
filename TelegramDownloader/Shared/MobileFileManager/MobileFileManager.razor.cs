using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
        private List<FileManagerDirectoryContent> DisplayFiles => GetDisplayFiles();
        private List<FileManagerDirectoryContent> PagedFiles => GetPagedFiles();
        private List<FileManagerDirectoryContent> SelectedItems { get; set; } = new();
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

        private bool ShowDetailsPanel { get; set; } = false;
        private FileManagerDirectoryContent? DetailsItem { get; set; }

        private bool ShowMoreMenu { get; set; } = false;
        private bool ShowFabMenu { get; set; } = false;

        private bool ShowDeleteConfirmDialog { get; set; } = false;
        private FileManagerDirectoryContent[] ItemsToDelete { get; set; } = Array.Empty<FileManagerDirectoryContent>();

        private System.Timers.Timer? searchTimer;

        // Track previous Id to detect changes
        private string _previousId = string.Empty;

        #endregion

        #region Lifecycle

        protected override async Task OnInitializedAsync()
        {
            _previousId = Id;
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
                // Normalize CurrentPath before sending
                CurrentPath = NormalizePath(CurrentPath);

                var args = new MfmReadEventArgs
                {
                    Path = CurrentPath
                };

                await OnRead.InvokeAsync(args);

                if (args.Response?.Files != null)
                {
                    // Normalize FilterPath in all returned files
                    Files = args.Response.Files.Select(f =>
                    {
                        f.FilterPath = NormalizePath(f.FilterPath);
                        return f;
                    }).ToList();
                }

                // Save current working directory (folder) for paste operations
                if (args.Response?.CWD != null)
                {
                    CurrentFolder = args.Response.CWD;
                    if (CurrentFolder.FilterPath != null)
                    {
                        CurrentFolder.FilterPath = NormalizePath(CurrentFolder.FilterPath);
                    }
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

        private List<FileManagerDirectoryContent> GetDisplayFiles()
        {
            var files = Files ?? new List<FileManagerDirectoryContent>();

            // Apply search filter
            if (!string.IsNullOrEmpty(SearchText))
            {
                files = files.Where(f => f.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
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
            segments.Add(new PathSegment { Name = "Root", Path = "/" });

            if (CurrentPath != "/")
            {
                var parts = CurrentPath.Trim('/').Split('/');
                var currentPath = "/";
                foreach (var part in parts)
                {
                    if (!string.IsNullOrEmpty(part))
                    {
                        currentPath += part + "/";
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
        }

        private async Task GoBack()
        {
            if (CurrentPath == "/") return;

            var parts = CurrentPath.Trim('/').Split('/');
            if (parts.Length <= 1)
            {
                CurrentPath = "/";
            }
            else
            {
                CurrentPath = "/" + string.Join("/", parts.Take(parts.Length - 1)) + "/";
            }

            ClearSelection();
            ResetPagination();
            await LoadFiles(); // LoadFiles will update CurrentFolder from CWD response
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
            var filterPath = NormalizePath(folder.FilterPath);
            CurrentPath = filterPath + folder.Name + "/";
            ClearSelection();
            ResetPagination();
            await LoadFiles(); // LoadFiles will update CurrentFolder from CWD response
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
            if (SelectedItems.Contains(file))
            {
                SelectedItems.Remove(file);
            }
            else
            {
                SelectedItems.Add(file);
            }

            OnSelectedItemsChanged.InvokeAsync(SelectedItems.Select(f => f.Id).ToArray());
            StateHasChanged();
        }

        private void ClearSelection()
        {
            SelectedItems.Clear();
            OnSelectedItemsChanged.InvokeAsync(Array.Empty<string>());
            StateHasChanged();
        }

        private void SelectAll()
        {
            SelectedItems = new List<FileManagerDirectoryContent>(DisplayFiles);
            ShowMoreMenu = false;
            OnSelectedItemsChanged.InvokeAsync(SelectedItems.Select(f => f.Id).ToArray());
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
                    FilterPath = NormalizePath(CurrentFolder.FilterPath ?? ""),
                    FilterId = CurrentFolder.FilterId,
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
            return path.Replace("\\", "/");
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

        private void RenameSelected()
        {
            if (SelectedItems.Count != 1) return;
            RenameItem = SelectedItems.First();
            RenameText = RenameItem.Name;
            ShowRenameDialog = true;
            ClearSelection();
        }

        private void StartRenameItem(FileManagerDirectoryContent item)
        {
            RenameItem = item;
            RenameText = item.Name;
            ShowRenameDialog = true;
            CloseContextMenu();
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

        private void CreateNewFolder()
        {
            ShowFabMenu = false;
            NewFolderName = "New Folder";
            ShowNewFolderDialog = true;
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

        private async Task ShareFileItem(FileManagerDirectoryContent item)
        {
            var args = new MfmShareFileEventArgs
            {
                File = item
            };

            await OnShareFile.InvokeAsync(args);
            CloseContextMenu();
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

        #endregion

        #region Search

        private void ToggleSearch()
        {
            ShowSearch = !ShowSearch;
            ShowMoreMenu = false;
            if (!ShowSearch)
            {
                SearchText = string.Empty;
            }
            StateHasChanged();
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

                        if (searchArgs.Response?.Files != null)
                        {
                            // Normalize FilterPath in search results
                            Files = searchArgs.Response.Files.Select(f =>
                            {
                                f.FilterPath = NormalizePath(f.FilterPath);
                                return f;
                            }).ToList();
                        }
                    }
                    else
                    {
                        await LoadFiles();
                    }
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
        }

        private async Task CloseSearch()
        {
            ShowSearch = false;
            SearchText = string.Empty;
            ResetPagination();
            await LoadFiles();
        }

        #endregion

        #region UI Helpers

        private void ToggleViewMode()
        {
            ViewMode = ViewMode == "grid" ? "list" : "grid";
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
            ResetPagination();
            StateHasChanged();
        }

        private void ToggleSortDirection()
        {
            SortAscending = !SortAscending;
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

        #endregion

        private class PathSegment
        {
            public string Name { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
        }
    }
}
