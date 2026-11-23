#nullable disable
using TelegramDownloader.Models;
using TelegramDownloader.Models.Persistence;

namespace TelegramDownloader.Services.Transfers
{
    /// <summary>Who asked for the resume.</summary>
    public enum ResumeTrigger
    {
        /// <summary>Automatic resume after the Telegram session is ready on startup.</summary>
        Startup,
        /// <summary>"Resume"/"Retry" from the web UI or the API.</summary>
        Manual
    }

    public enum ResumeActionKind
    {
        /// <summary>Leave the record as it is (already running, or not resumable automatically).</summary>
        Skip,
        /// <summary>Mark the record as failed with <see cref="ResumePlan.Reason"/>.</summary>
        Fail,
        /// <summary>Queue a single-file download again.</summary>
        ResumeDownload,
        /// <summary>Queue a split (multi-part) download from <see cref="ResumePlan.StartPart"/>.</summary>
        ResumeSplitDownload,
        /// <summary>Queue a batch upload again with the source items that still exist.</summary>
        ResumeBatchUpload
    }

    public sealed class ResumePlan
    {
        public ResumeActionKind Kind { get; init; }
        public string Reason { get; init; }

        // Single-file download: first byte that will be requested (a hint for
        // logs and the UI; the download recomputes it from the file on disk).
        public long ResumeOffset { get; init; }

        // Split download
        public int StartPart { get; init; }
        public long CompletedBytes { get; init; }
        public long TransmittedInPart { get; init; }
        public bool RestartFromScratch { get; init; }

        // Batch upload
        public List<PersistedSourceItem> Items { get; init; } = new List<PersistedSourceItem>();
        public List<PersistedSourceItem> MissingItems { get; init; } = new List<PersistedSourceItem>();

        public static ResumePlan Skip(string reason) => new ResumePlan { Kind = ResumeActionKind.Skip, Reason = reason };
        public static ResumePlan Fail(string reason) => new ResumePlan { Kind = ResumeActionKind.Fail, Reason = reason };
    }

    /// <summary>File system access used by the planner (replaced in tests).</summary>
    public interface IResumeFileSystem
    {
        bool FileExists(string path);
        long FileLength(string path);
        bool DirectoryExists(string path);
    }

    public sealed class PhysicalResumeFileSystem : IResumeFileSystem
    {
        public static readonly PhysicalResumeFileSystem Instance = new PhysicalResumeFileSystem();
        public bool FileExists(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);
        public long FileLength(string path) => new FileInfo(path).Length;
        public bool DirectoryExists(string path) => !string.IsNullOrEmpty(path) && Directory.Exists(path);
    }

    /// <summary>
    /// Pure rules that decide what happens to a persisted transfer when it is
    /// resumed (on startup or by hand). Kept free of Telegram and MongoDB so
    /// they can be unit tested; <see cref="TaskResumeCoordinator"/> executes
    /// the plans.
    /// </summary>
    public static class TransferResumePlanner
    {
        /// <summary>
        /// Telegram serves file chunks at offsets that are multiples of 4 KB and
        /// a request must not cross a 1 MB boundary; the downloader asks for
        /// 512 KB chunks, so a resume offset is aligned down to 512 KB.
        /// </summary>
        public const long ReadAlignment = 512 * 1024;

        public const string LegacyUploadMessage =
            "This upload was saved by an older version as a single Telegram upload without its destination folder. " +
            "Resuming it would send an unindexed copy to the channel, so it was not resumed: upload the file again.";

        public const string LegacyBatchMessage =
            "This batch upload was saved by an older version that did not record the selected files, so it cannot be rebuilt. " +
            "Start the upload again: files already in the destination are skipped.";

        /// <summary>States picked up by the automatic resume on startup.</summary>
        public static bool IsAutoResumable(StateTask state)
            => state == StateTask.Pending || state == StateTask.Working || state == StateTask.Paused;

        /// <summary>States a user can resume or retry by hand (failures included).</summary>
        public static bool IsManuallyResumable(StateTask state)
            => IsAutoResumable(state) || state == StateTask.Error;

        /// <summary>
        /// Records the startup resume looks at, oldest first, so the queues keep
        /// their original order. Downloads come before batch uploads only
        /// because they share no queue; the order inside each type is kept.
        /// </summary>
        public static List<PersistedTaskModel> SelectForStartup(IEnumerable<PersistedTaskModel> tasks, Func<string, bool> isLive)
        {
            return (tasks ?? Enumerable.Empty<PersistedTaskModel>())
                .Where(t => t != null && IsAutoResumable(t.State) && !(isLive?.Invoke(t.InternalId) ?? false))
                .OrderBy(t => t.CreationDate)
                .ToList();
        }

        /// <summary>
        /// First byte to request when continuing a download.
        /// <paramref name="bytesOnDisk"/> is what the partial file holds (for a
        /// split part: past the previous parts), <paramref name="confirmedBytes"/>
        /// the progress that was reported. Neither alone is safe: the file can be
        /// longer than the data actually written (multi-connection downloads
        /// preallocate it, parallel requests complete out of order) and the
        /// reported progress can be ahead of what reached the disk. The smaller
        /// one, minus <paramref name="safetyMargin"/> for chunks that may still
        /// have been in flight, aligned down to <paramref name="alignment"/>.
        /// Returns <paramref name="totalSize"/> when the download had finished.
        /// </summary>
        public static long ComputeDownloadOffset(long bytesOnDisk, long confirmedBytes, long totalSize, long safetyMargin, long alignment = ReadAlignment)
        {
            if (bytesOnDisk <= 0 || confirmedBytes <= 0)
                return 0;
            if (totalSize > 0 && bytesOnDisk >= totalSize && confirmedBytes >= totalSize)
                return totalSize;
            long safe = Math.Min(bytesOnDisk, confirmedBytes) - Math.Max(0, safetyMargin);
            if (totalSize > 0)
                safe = Math.Min(safe, totalSize);
            if (safe <= 0)
                return 0;
            if (alignment <= 1)
                return safe;
            return safe / alignment * alignment;
        }

        /// <summary>Absolute path of a batch source item under the local root.</summary>
        public static string ResolveSourcePath(string localRoot, PersistedSourceItem item)
        {
            string relative = ((item.FilterPath ?? "/").Replace("\\", "/") + item.Name).TrimStart('/');
            return Path.Combine(localRoot, relative).Replace("\\", "/");
        }

        public static ResumePlan Plan(PersistedTaskModel task, ResumeTrigger trigger, bool isLive, IResumeFileSystem fs, string localRoot)
        {
            if (task == null)
                return ResumePlan.Skip("Task not found");
            if (isLive)
                return ResumePlan.Skip("Already queued or running");
            if (trigger == ResumeTrigger.Startup && !IsAutoResumable(task.State))
                return ResumePlan.Skip($"State {task.State} is not resumed automatically");
            if (trigger == ResumeTrigger.Manual && !IsManuallyResumable(task.State))
                return ResumePlan.Skip($"State {task.State} cannot be resumed");

            switch (task.Type)
            {
                case TaskType.Download:
                    return PlanDownload(task, fs);
                case TaskType.Upload:
                    return ResumePlan.Fail(LegacyUploadMessage);
                case TaskType.BatchUpload:
                    return PlanBatchUpload(task, fs, localRoot);
                default:
                    return ResumePlan.Fail($"Tasks of type {task.Type} cannot be resumed");
            }
        }

        private static ResumePlan PlanDownload(PersistedTaskModel task, IResumeFileSystem fs)
        {
            if (string.IsNullOrEmpty(task.ChannelId))
                return ResumePlan.Fail("The task has no channel recorded");
            if (string.IsNullOrEmpty(task.DestinationPath))
                return ResumePlan.Fail("The task has no destination path recorded");

            if (task.IsSplit && task.MessageIds != null && task.MessageIds.Count > 0)
                return PlanSplitDownload(task, fs);

            int messageId = task.MessageId ?? task.MessageIds?.FirstOrDefault() ?? 0;
            if (messageId == 0)
                return ResumePlan.Fail("The task has no Telegram message recorded");

            long onDisk = fs.FileExists(task.DestinationPath) ? fs.FileLength(task.DestinationPath) : 0;
            return new ResumePlan
            {
                Kind = ResumeActionKind.ResumeDownload,
                ResumeOffset = ComputeDownloadOffset(onDisk, task.TransmittedBytes, task.TotalSize, 0)
            };
        }

        private static ResumePlan PlanSplitDownload(PersistedTaskModel task, IResumeFileSystem fs)
        {
            int parts = task.MessageIds.Count;
            List<int> completed = task.CompletedPartIndices ?? new List<int>();
            int completedCount = completed.Count;
            // Parts are downloaded in order, so the completed ones must be 0..n-1.
            bool contiguous = completed.OrderBy(i => i).SequenceEqual(Enumerable.Range(0, completedCount));
            long onDisk = fs.FileExists(task.DestinationPath) ? fs.FileLength(task.DestinationPath) : 0;

            // Records written by older versions describe one part (not the whole
            // file) and only part 1 carries a usable byte count.
            bool legacyPartRecord = task.MessageId.HasValue && task.MessageId.Value != task.MessageIds[0] && completedCount == 0;

            if (legacyPartRecord || !contiguous || completedCount > parts
                || (completedCount > 0 && (task.CompletedBytes <= 0 || onDisk < task.CompletedBytes)))
            {
                string why = legacyPartRecord
                    ? "saved by an older version per part"
                    : !contiguous || completedCount > parts
                        ? "inconsistent part list"
                        : "the partial file is shorter than the parts already downloaded";
                return new ResumePlan
                {
                    Kind = ResumeActionKind.ResumeSplitDownload,
                    StartPart = 0,
                    CompletedBytes = 0,
                    TransmittedInPart = 0,
                    RestartFromScratch = true,
                    Reason = $"Restarting the split file from the first part ({why})"
                };
            }

            long completedBytes = completedCount > 0 ? task.CompletedBytes : 0;
            long inPart = Math.Max(0, task.TransmittedBytes - completedBytes);
            return new ResumePlan
            {
                Kind = ResumeActionKind.ResumeSplitDownload,
                StartPart = completedCount,
                CompletedBytes = completedBytes,
                TransmittedInPart = completedCount >= parts ? 0 : inPart,
                RestartFromScratch = completedCount == 0 && inPart == 0
            };
        }

        private static ResumePlan PlanBatchUpload(PersistedTaskModel task, IResumeFileSystem fs, string localRoot)
        {
            if (task.SourceItems == null || task.SourceItems.Count == 0)
                return ResumePlan.Fail(LegacyBatchMessage);
            if (string.IsNullOrEmpty(task.ChannelId))
                return ResumePlan.Fail("The task has no channel recorded");

            var present = new List<PersistedSourceItem>();
            var missing = new List<PersistedSourceItem>();
            foreach (var item in task.SourceItems)
            {
                string path = ResolveSourcePath(localRoot, item);
                bool exists = item.IsFile ? fs.FileExists(path) : fs.DirectoryExists(path);
                (exists ? present : missing).Add(item);
            }

            if (present.Count == 0)
            {
                string names = string.Join(", ", missing.Take(5).Select(m => (m.FilterPath ?? "/") + m.Name));
                if (missing.Count > 5)
                    names += $" (+{missing.Count - 5})";
                return ResumePlan.Fail($"Source no longer exists: {names}");
            }

            return new ResumePlan
            {
                Kind = ResumeActionKind.ResumeBatchUpload,
                Items = present,
                MissingItems = missing,
                Reason = missing.Count > 0
                    ? $"{missing.Count} selected item(s) no longer exist and were left out: {string.Join(", ", missing.Take(5).Select(m => m.Name))}"
                    : null
            };
        }
    }
}
