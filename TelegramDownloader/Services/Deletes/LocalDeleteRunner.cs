using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Services.Deletes
{
    /// <summary>Outcome of a local delete.</summary>
    public sealed class LocalDeleteResult
    {
        /// <summary>Client paths deleted completely.</summary>
        public List<string> Deleted { get; } = new();

        /// <summary>Client paths rejected, missing or not (completely) deleted.</summary>
        public List<string> Skipped { get; } = new();
    }

    /// <summary>
    /// Deletes files and folders of the local storage with progress: counts
    /// first (one walk), then deletes file by file and the folders bottom-up.
    ///
    /// Safety: every client path goes through <see cref="LocalPathGuard.TryResolve"/>;
    /// the root itself is refused, and so is a path reached through a link
    /// (symlink / junction) that points outside the root. Links found while
    /// walking are deleted as links and never followed.
    /// </summary>
    public sealed class LocalDeleteRunner
    {
        private sealed record Plan(string ClientPath, string Absolute, bool IsDirectory, List<(string Path, long Size)> Files, List<string> Directories);

        private readonly string _root;
        private readonly ILogger? _logger;

        public LocalDeleteRunner(string root, ILogger? logger = null)
        {
            _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            _logger = logger;
        }

        private static bool IsLink(FileSystemInfo info) => info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget != null;

        /// <summary>
        /// Resolves a client path to a deletable absolute path, or explains why not:
        /// outside the root, the root itself, missing, or reached through a link
        /// that leaves the root.
        /// </summary>
        public bool TryResolveTarget(string? clientPath, out string absolute, out string? error)
        {
            if (!LocalPathGuard.TryResolve(_root, clientPath, out absolute, out var relative, out error))
                return false;
            if (string.IsNullOrEmpty(relative))
            {
                error = "The local root cannot be deleted";
                return false;
            }

            // Every ancestor between the root and the target must be a real folder,
            // or a link whose final target is still inside the root.
            var current = _root;
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                current = Path.Combine(current, segments[i]);
                var dir = new DirectoryInfo(current);
                if (!dir.Exists) break;
                if (IsLink(dir))
                {
                    FileSystemInfo? target = null;
                    try { target = dir.ResolveLinkTarget(returnFinalTarget: true); } catch { }
                    if (target == null || !LocalPathGuard.IsSameOrInside(_root, Path.GetFullPath(target.FullName)))
                    {
                        error = "The path goes through a link outside the local root";
                        return false;
                    }
                }
            }

            if (!File.Exists(absolute) && !Directory.Exists(absolute) && !IsDanglingLink(absolute))
            {
                error = "Not found";
                return false;
            }
            return true;
        }

        private static bool IsDanglingLink(string path)
        {
            try
            {
                return new FileInfo(path).LinkTarget != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Deletes the client paths. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
        public Task<LocalDeleteResult> RunAsync(IReadOnlyList<string> clientPaths, DeleteOperation progress, CancellationToken cancellationToken = default) =>
            Task.Run(() => Run(clientPaths, progress, cancellationToken), CancellationToken.None);

        private LocalDeleteResult Run(IReadOnlyList<string> clientPaths, DeleteOperation progress, CancellationToken cancellationToken)
        {
            var result = new LocalDeleteResult();

            // ---- counting ----
            var plans = new List<Plan>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var clientPath in clientPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryResolveTarget(clientPath, out var absolute, out var error))
                {
                    progress.AddFailure(clientPath ?? string.Empty, error ?? "Invalid path");
                    result.Skipped.Add(clientPath ?? string.Empty);
                    continue;
                }
                if (!seen.Add(absolute)) continue;
                // A path inside another selected folder is deleted with it.
                if (clientPaths.Any(other => other != clientPath && LocalPathGuard.TryResolve(_root, other, out var o, out var rel, out _)
                        && rel.Length > 0 && !string.Equals(o, absolute, StringComparison.OrdinalIgnoreCase) && LocalPathGuard.IsSameOrInside(o, absolute)))
                {
                    result.Deleted.Add(clientPath!);
                    continue;
                }
                progress.SetCurrent(Relative(absolute));
                plans.Add(Walk(clientPath!, absolute, cancellationToken));
            }

            progress.SetTotals(
                plans.Sum(p => p.Files.Count),
                plans.Sum(p => p.Directories.Count),
                plans.Sum(p => p.Files.Sum(f => f.Size)));

            // ---- deleting ----
            foreach (var plan in plans)
            {
                bool complete = true;
                foreach (var (path, size) in plan.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.SetCurrent(Relative(path));
                    try
                    {
                        DeleteFile(path);
                        progress.AddProcessed(1, 0, size);
                    }
                    catch (Exception ex)
                    {
                        complete = false;
                        _logger?.LogWarning(ex, "Could not delete local file {Path}", path);
                        progress.AddFailure(Relative(path), ex.Message);
                    }
                }
                foreach (var dir in plan.Directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.SetCurrent(Relative(dir));
                    try
                    {
                        // Not recursive: links were planned as leaves and a folder whose
                        // content failed stays (with the reason reported for the content).
                        DeleteDirectory(dir);
                        progress.AddProcessed(0, 1, 0);
                    }
                    catch (Exception ex)
                    {
                        complete = false;
                        _logger?.LogWarning(ex, "Could not delete local folder {Path}", dir);
                        progress.AddFailure(Relative(dir), ex.Message);
                    }
                }
                (complete ? result.Deleted : result.Skipped).Add(plan.ClientPath);
            }
            progress.SetCurrent(null);
            return result;
        }

        private string Relative(string absolute)
        {
            var rel = Path.GetRelativePath(_root, absolute).Replace('\\', '/');
            return rel == "." ? string.Empty : rel;
        }

        private static void DeleteDirectory(string path)
        {
            var info = new DirectoryInfo(path);
            // A link to a folder: Windows removes it as a directory, Unix unlinks it.
            if (IsLink(info) && !OperatingSystem.IsWindows()) File.Delete(path);
            else Directory.Delete(path, recursive: false);
        }

        private static void DeleteFile(string path)
        {
            var info = new FileInfo(path);
            if (info.LinkTarget == null && info.Exists && info.IsReadOnly) info.IsReadOnly = false;
            info.Delete();
        }

        /// <summary>
        /// The files and the folders (deepest first) of one selected path,
        /// without following links: a link to a folder is a leaf folder entry.
        /// </summary>
        private Plan Walk(string clientPath, string absolute, CancellationToken cancellationToken)
        {
            var files = new List<(string, long)>();
            var dirs = new List<string>();
            var top = new DirectoryInfo(absolute);
            if (!top.Exists || IsLink(top))
            {
                if (top.Exists) dirs.Add(absolute); // a link to a folder: removed as a link
                else files.Add((absolute, SafeLength(new FileInfo(absolute))));
                return new Plan(clientPath, absolute, top.Exists, files, dirs);
            }

            var stack = new Stack<DirectoryInfo>();
            stack.Push(top);
            var order = new List<DirectoryInfo>();
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                order.Add(dir);
                IEnumerable<FileSystemInfo> children;
                try
                {
                    children = dir.EnumerateFileSystemInfos("*", new EnumerationOptions
                    {
                        RecurseSubdirectories = false,
                        IgnoreInaccessible = true,
                        AttributesToSkip = 0,
                        ReturnSpecialDirectories = false
                    }).ToList();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not list {Path}", dir.FullName);
                    continue;
                }
                foreach (var child in children)
                {
                    if (child is DirectoryInfo sub)
                    {
                        if (IsLink(sub)) order.Add(sub);
                        else stack.Push(sub);
                    }
                    else if (child is FileInfo file)
                    {
                        files.Add((file.FullName, SafeLength(file)));
                    }
                }
            }
            // Deepest first: a folder is removed after everything below it.
            dirs.AddRange(order
                .OrderByDescending(d => d.FullName.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .Select(d => d.FullName));
            return new Plan(clientPath, absolute, true, files, dirs);
        }

        private static long SafeLength(FileInfo file)
        {
            try
            {
                return file.LinkTarget != null ? 0 : file.Length;
            }
            catch
            {
                return 0;
            }
        }
    }
}
