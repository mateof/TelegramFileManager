using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Builds the DTOs shared by <c>GET /api/v1/transfers</c> and the
    /// <c>transfers</c> SignalR hub, so REST snapshots and live pushes always
    /// have exactly the same shape.
    /// </summary>
    public static class TransferSnapshotBuilder
    {
        /// <summary>Full picture of active and queued transfers plus the summary.</summary>
        public static TransfersSnapshotDto BuildSnapshot(TransactionInfoService tis)
        {
            return new TransfersSnapshotDto
            {
                Downloads = tis.downloadModels.ToList()
                    .Select(d => TransferDto.FromDownload(d)).ToList(),
                QueuedDownloads = tis.pendingDownloadModels.ToList()
                    .Select(d => TransferDto.FromDownload(d, isQueued: true)).ToList(),
                Uploads = tis.uploadModels.ToList()
                    .Select(u => TransferDto.FromUpload(u)).ToList(),
                QueuedUploads = tis.pendingUploadModels.ToList()
                    .Select(u => TransferDto.FromUpload(u, isQueued: true)).ToList(),
                Tasks = tis.infoDownloadTaksModel.ToList()
                    .OrderBy(t => t.creationDate)
                    .Select(TransferDto.FromBatch).ToList(),
                Summary = BuildSummary(tis)
            };
        }

        /// <summary>Counters and current speeds only.</summary>
        public static TransferSummaryDto BuildSummary(TransactionInfoService tis)
        {
            var downloads = tis.downloadModels.ToList();
            var uploads = tis.uploadModels.ToList();
            var tasks = tis.infoDownloadTaksModel.ToList();

            return new TransferSummaryDto
            {
                ActiveDownloads = downloads.Count(d => d.state == StateTask.Working),
                QueuedDownloads = tis.pendingDownloadModels.Count,
                ActiveUploads = uploads.Count(u => u.state == StateTask.Working),
                QueuedUploads = tis.pendingUploadModels.Count,
                ActiveTasks = tasks.Count(t => t.state == StateTask.Working),
                TotalTasks = tasks.Count,
                DownloadSpeed = tis.downloadSpeed ?? "0 KB/s",
                UploadSpeed = tis.uploadSpeed ?? "0 KB/s",
                DownloadBytesPerSecond = tis.bytesDownloaded,
                UploadBytesPerSecond = tis.bytesUploaded,
                DownloadsPaused = tis.isPauseDownloads
            };
        }

        /// <summary>Speed history for the charts, newest last.</summary>
        public static SpeedHistoryDto BuildSpeedHistory(TransactionInfoService tis)
        {
            return new SpeedHistoryDto
            {
                Download = tis.GetDownloadSpeedsHistoryCopy().Select(SpeedPointDto.From).ToList(),
                Upload = tis.GetUploadSpeedsHistoryCopy().Select(SpeedPointDto.From).ToList()
            };
        }

        /// <summary>
        /// Finds a running or queued transfer by its id across every list.
        /// </summary>
        public static bool TryFind(
            TransactionInfoService tis,
            string id,
            out DownloadModel? download,
            out UploadModel? upload,
            out InfoDownloadTaksModel? task)
        {
            download = tis.downloadModels.FirstOrDefault(d => d._internalId == id)
                       ?? tis.pendingDownloadModels.FirstOrDefault(d => d._internalId == id);
            upload = tis.uploadModels.FirstOrDefault(u => u._internalId == id)
                     ?? tis.pendingUploadModels.FirstOrDefault(u => u._internalId == id);
            task = tis.infoDownloadTaksModel.FirstOrDefault(t => t._internalId == id);
            return download != null || upload != null || task != null;
        }
    }
}
