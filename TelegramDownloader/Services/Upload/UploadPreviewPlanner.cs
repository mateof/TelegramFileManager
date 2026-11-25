using TelegramDownloader.Models;
using TelegramDownloader.Models.FileSystem;

namespace TelegramDownloader.Services.Upload
{
    /// <summary>Action labels used in <see cref="UploadPreviewItem.Action"/>.</summary>
    public static class UploadPreviewActions
    {
        /// <summary>File not in the destination: will be uploaded.</summary>
        public const string New = "new";
        /// <summary>Older copy indexed at the same path: will be uploaded and the old entry deleted.</summary>
        public const string Replace = "replace";
        /// <summary>Empty file: skipped.</summary>
        public const string SkipEmpty = "skip-empty";
        /// <summary>The indexed copy is newer: skipped.</summary>
        public const string SkipNewer = "skip-newer";
        /// <summary>Folder not indexed yet: will be created.</summary>
        public const string CreateFolder = "create-folder";
        /// <summary>Folder already indexed: reused.</summary>
        public const string ExistingFolder = "existing-folder";

        /// <summary>Label of a file decision.</summary>
        public static string Of(UploadFileAction action) => action switch
        {
            UploadFileAction.New => New,
            UploadFileAction.Replace => Replace,
            UploadFileAction.SkipEmpty => SkipEmpty,
            UploadFileAction.SkipNewerInChannel => SkipNewer,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    /// <summary>One local file or folder of an upload preview.</summary>
    public sealed class UploadPreviewItem
    {
        /// <summary>Path relative to the server local root (folders end with <c>/</c>).</summary>
        public string LocalPath { get; set; } = string.Empty;
        /// <summary>Index path the item gets in the channel (<c>FilePath</c>).</summary>
        public string TargetPath { get; set; } = string.Empty;
        /// <summary>File or folder.</summary>
        public bool IsFile { get; set; }
        /// <summary>Bytes of the local file (0 for folders).</summary>
        public long Size { get; set; }
        /// <summary>One of <see cref="UploadPreviewActions"/>.</summary>
        public string Action { get; set; } = string.Empty;
        /// <summary>Modification date of the indexed copy, for <c>replace</c> and <c>skip-newer</c>.</summary>
        public DateTime? ExistingDateModified { get; set; }
    }

    /// <summary>A channel file with the same content as a local file.</summary>
    public sealed class UploadPreviewDuplicateMatch
    {
        /// <summary>Index id of the channel file.</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Path of the channel file.</summary>
        public string FilePath { get; set; } = string.Empty;
        /// <summary>Size of the channel file.</summary>
        public long Size { get; set; }
    }

    /// <summary>A local file that will be uploaded although its content is already in the channel.</summary>
    public sealed class UploadPreviewDuplicate
    {
        /// <summary>Path relative to the server local root.</summary>
        public string LocalPath { get; set; } = string.Empty;
        /// <summary>Where the upload would index it.</summary>
        public string TargetPath { get; set; } = string.Empty;
        /// <summary>Size of the local file.</summary>
        public long Size { get; set; }
        /// <summary>Stored-format XXHash shared with the matches.</summary>
        public string XxHash { get; set; } = string.Empty;
        /// <summary>Channel files with the same hash and size, at other paths.</summary>
        public List<UploadPreviewDuplicateMatch> Matches { get; set; } = new();
    }

    /// <summary>
    /// Result of an upload dry run: what <c>POST /api/v1/transfers/uploads</c>
    /// would do with the same body, computed without sending anything.
    /// </summary>
    public sealed class UploadPreviewResult
    {
        /// <summary>Destination folder in the channel.</summary>
        public string TargetPath { get; set; } = string.Empty;
        /// <summary>
        /// Whether the destination folder is indexed. When it is not, the upload
        /// attaches the new entries to the channel root folder (as it always did).
        /// </summary>
        public bool TargetFolderExists { get; set; }

        /// <summary>Files not in the destination.</summary>
        public int NewFiles { get; set; }
        /// <summary>Bytes of the new files.</summary>
        public long NewBytes { get; set; }
        /// <summary>Files replacing an older indexed copy.</summary>
        public int ReplacedFiles { get; set; }
        /// <summary>Bytes of the replacing files.</summary>
        public long ReplacedBytes { get; set; }
        /// <summary>Files that will be uploaded (<c>NewFiles + ReplacedFiles</c>).</summary>
        public int FilesToUpload => NewFiles + ReplacedFiles;
        /// <summary>Bytes that will be sent to Telegram.</summary>
        public long BytesToUpload => NewBytes + ReplacedBytes;

        /// <summary>Files skipped (empty, or a newer copy is indexed).</summary>
        public int SkippedFiles => SkippedEmpty + SkippedNewerInChannel;
        /// <summary>Empty files skipped.</summary>
        public int SkippedEmpty { get; set; }
        /// <summary>Files skipped because the indexed copy is newer.</summary>
        public int SkippedNewerInChannel { get; set; }
        /// <summary>Bytes of the skipped files.</summary>
        public long SkippedBytes { get; set; }

        /// <summary>Folders that will be created in the index.</summary>
        public int FoldersToCreate { get; set; }
        /// <summary>Folders that already exist and are reused.</summary>
        public int ExistingFolders { get; set; }

        /// <summary>Whether duplicate detection by content ran.</summary>
        public bool DuplicateCheck { get; set; }
        /// <summary>Local files read to compute their hash (only sizes that some hashed channel file has).</summary>
        public int HashedFiles { get; set; }
        /// <summary>Bytes read to compute those hashes.</summary>
        public long HashedBytes { get; set; }
        /// <summary>Files to upload whose content is already in the channel at another path.</summary>
        public List<UploadPreviewDuplicate> Duplicates { get; set; } = new();

        /// <summary>Per item decisions, capped at the requested maximum.</summary>
        public List<UploadPreviewItem> Items { get; set; } = new();
        /// <summary>True when <see cref="Items"/> was cut at the maximum.</summary>
        public bool ItemsTruncated { get; set; }
        /// <summary>Requested local paths that do not exist under the local root.</summary>
        public List<string> MissingLocalPaths { get; set; } = new();
    }

    /// <summary>I/O the planner needs, injected so it stays testable.</summary>
    public sealed class UploadPlanSource
    {
        /// <summary>Indexed direct children of a destination folder path (as the upload reads them).</summary>
        public required Func<string, CancellationToken, Task<IEnumerable<BsonFileManagerModel>>> LoadDestinationChildren { get; init; }

        /// <summary>Local content of a folder, relative path, as the upload lists it.</summary>
        public required Func<string, List<FsEntry>> ListLocalFolder { get; init; }

        /// <summary>Real length of a local file (relative path), null when it is gone.</summary>
        public Func<string, long?>? LocalFileLength { get; init; }
    }

    /// <summary>
    /// Walks an upload exactly like <c>FileService.UploadFileFromServer</c>
    /// does (same traversal, same destination snapshot per folder, same
    /// <see cref="FolderUploadPlanning.DecideFile"/> rule and path keys) but
    /// only records the decisions, so preview and upload cannot disagree.
    /// </summary>
    public static class UploadPreviewPlanner
    {
        /// <summary>Default cap of <see cref="UploadPreviewResult.Items"/>.</summary>
        public const int DefaultMaxItems = 500;

        /// <summary>Plans the upload of <paramref name="files"/> into <paramref name="currentPath"/>.</summary>
        /// <param name="currentPath">Destination folder in the channel (e.g. <c>/backup/</c>).</param>
        /// <param name="files">Selected local items, as given to the upload.</param>
        /// <param name="source">Index and local file system access.</param>
        /// <param name="maxItems">Cap of <see cref="UploadPreviewResult.Items"/>; counters always cover everything.</param>
        /// <param name="filesToUpload">Optional sink receiving every file that would be sent (uncapped).</param>
        /// <param name="cancellationToken">Stops the walk.</param>
        public static async Task<UploadPreviewResult> PlanAsync(
            string currentPath,
            IReadOnlyList<FsEntry> files,
            UploadPlanSource source,
            int maxItems = DefaultMaxItems,
            List<UploadPreviewItem>? filesToUpload = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(currentPath);
            var result = new UploadPreviewResult { TargetPath = currentPath };
            var cache = new UploadDestinationCache();
            await PlanFolder(currentPath, files, source, cache, result, Math.Max(0, maxItems), filesToUpload, cancellationToken);
            return result;
        }

        private static async Task PlanFolder(
            string currentPath,
            IReadOnlyList<FsEntry> files,
            UploadPlanSource source,
            UploadDestinationCache cache,
            UploadPreviewResult result,
            int maxItems,
            List<UploadPreviewItem>? filesToUpload,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (files.Count > 0 && !cache.IsFolderLoaded(currentPath))
                cache.LoadFolder(currentPath, await source.LoadDestinationChildren(currentPath, ct));

            foreach (var file in files)
            {
                var localPath = FolderUploadPlanning.LocalRelativePath(file.FilterPath, file.Name, file.IsFile);
                var targetPath = FolderUploadPlanning.EntryPath(currentPath, file.Name);
                var existing = cache.Get(targetPath);

                if (file.IsFile)
                {
                    var action = FolderUploadPlanning.DecideFile(file.Size, file.DateModified, existing);
                    // The upload sends the file as it is on disk now.
                    long length = source.LocalFileLength?.Invoke(localPath) ?? file.Size;
                    switch (action)
                    {
                        case UploadFileAction.New:
                            result.NewFiles++;
                            result.NewBytes += length;
                            break;
                        case UploadFileAction.Replace:
                            result.ReplacedFiles++;
                            result.ReplacedBytes += length;
                            break;
                        case UploadFileAction.SkipEmpty:
                            result.SkippedEmpty++;
                            result.SkippedBytes += file.Size;
                            break;
                        default:
                            result.SkippedNewerInChannel++;
                            result.SkippedBytes += file.Size;
                            break;
                    }
                    var item = new UploadPreviewItem
                    {
                        LocalPath = localPath,
                        TargetPath = targetPath,
                        IsFile = true,
                        Size = FolderUploadPlanning.IsSkip(action) ? file.Size : length,
                        Action = UploadPreviewActions.Of(action),
                        ExistingDateModified = existing?.DateModified
                    };
                    AddItem(result, maxItems, item);
                    if (!FolderUploadPlanning.IsSkip(action))
                    {
                        filesToUpload?.Add(item);
                        // The upload indexes what it sends under this key.
                        cache.Set(targetPath, Placeholder(targetPath, true));
                    }
                }
                else
                {
                    bool create = FolderUploadPlanning.FolderNeedsCreate(existing);
                    if (create) result.FoldersToCreate++;
                    else result.ExistingFolders++;
                    AddItem(result, maxItems, new UploadPreviewItem
                    {
                        LocalPath = localPath,
                        TargetPath = targetPath,
                        IsFile = false,
                        Action = create ? UploadPreviewActions.CreateFolder : UploadPreviewActions.ExistingFolder
                    });
                    if (create)
                        cache.Set(targetPath, Placeholder(targetPath, false));

                    var children = source.ListLocalFolder(localPath);
                    await PlanFolder(FolderUploadPlanning.ChildFolderPath(currentPath, file.Name), children, source, cache, result, maxItems, filesToUpload, ct);
                }
            }
        }

        private static void AddItem(UploadPreviewResult result, int maxItems, UploadPreviewItem item)
        {
            if (result.Items.Count < maxItems)
                result.Items.Add(item);
            else
                result.ItemsTruncated = true;
        }

        private static BsonFileManagerModel Placeholder(string path, bool isFile)
            => new() { FilePath = path, IsFile = isFile, DateModified = DateTime.MaxValue };
    }
}
