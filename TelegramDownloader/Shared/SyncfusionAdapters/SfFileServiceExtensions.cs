#nullable disable
using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Models.FileSystem;
using TelegramDownloader.Services.Upload;
using static TelegramDownloader.Shared.SyncfusionAdapters.SfFileManagerMapper;

namespace TelegramDownloader.Shared.SyncfusionAdapters
{
    /// <summary>
    /// <see cref="IFileService"/> operations with the Syncfusion signatures the
    /// Blazor FileManager pages were written against. Each one converts the
    /// arguments with <see cref="SfFileManagerMapper"/>, calls the neutral
    /// service method and converts the answer back. Blazor UI only.
    /// </summary>
    public static class SfFileServiceExtensions
    {
        public static async Task<FileManagerResponse<FileManagerDirectoryContent>> SfGetFilesPath(
            this IFileService fs, string dbName, string path, List<FileManagerDirectoryContent> fileDetails = null, string collectionName = null)
        {
            var fsDetails = ToFs(fileDetails);
            var listing = await fs.GetFilesPath(dbName, path, fsDetails, collectionName);
            var response = ToSfResponse(listing);
            // The service answers the folder it was handed as the CWD: give the
            // component back its own instance, as it got before the adapter.
            if (response != null && fsDetails != null && fsDetails.Count > 0 && fsDetails[0] != null
                && ReferenceEquals(listing.CWD, fsDetails[0]))
                response.CWD = fileDetails[0];
            return response;
        }

        public static async Task<FileManagerResponse<FileManagerDirectoryContent>> SfSearchAsync(
            this IFileService fs, string dbName, string path, string searchText, string collectionId = null)
            => ToSfResponse(await fs.SearchAsync(dbName, path, searchText, collectionId));

        public static async Task<FileManagerResponse<FileManagerDirectoryContent>> SfItemDeleteAsync(
            this IFileService fs, string dbName, ItemsDeleteEventArgs<FileManagerDirectoryContent> args)
            => ToSfResponse(await fs.itemDeleteAsync(dbName, ToFs(args)));

        public static Task SfOneItemDeleteAsync(this IFileService fs, string dbName, FileManagerDirectoryContent file)
            => fs.oneItemDeleteAsync(dbName, ToFs(file));

        public static async Task<FileManagerResponse<FileManagerDirectoryContent>> SfRenameFileOrFolder(
            this IFileService fs, string dbName, FileManagerDirectoryContent file, string newName)
            => ToSfResponse(await fs.RenameFileOrFolder(dbName, ToFs(file), newName));

        public static async Task<FileManagerResponse<FileManagerDirectoryContent>> SfCopyItems(
            this IFileService fs, string dbName, ItemsMoveEventArgs<FileManagerDirectoryContent> args)
            => ToSfResponse(await fs.CopyItems(dbName, ToFs(args)));

        public static async Task<FileManagerResponse<FileManagerDirectoryContent>> SfCopyOrMoveItems(
            this IFileService fs, string dbName, FileManagerDirectoryContent[] files, string targetPath, FileManagerDirectoryContent targetData, bool isCopy)
            => ToSfResponse(await fs.CopyOrMoveItems(dbName, files?.Select(ToFs).ToArray(), targetPath, ToFs(targetData), isCopy));

        public static async Task<List<FileManagerDirectoryContent>> SfCreateFolder(
            this IFileService fs, string dbName, FolderCreateEventArgs<FileManagerDirectoryContent> args)
            => ToSf(await fs.createFolder(dbName, ToFs(args)));

        public static async Task<List<FileManagerDirectoryContent>> SfCreateFolder(
            this IFileService fs, string dbName, string path, string folderName, FileManagerDirectoryContent parentFolder)
            => ToSf(await fs.createFolder(dbName, path, folderName, ToFs(parentFolder)));

        public static Task SfDownloadFile(
            this IFileService fs, string dbName, List<FileManagerDirectoryContent> files, string targetPath, string collectionId = null, string channelId = null)
            => fs.downloadFile(dbName, ToFs(files), targetPath, collectionId, channelId);

        public static Task<string> SfAddUploadFileFromServer(
            this IFileService fs, string dbName, string currentPath, List<FileManagerDirectoryContent> files, InfoDownloadTaksModel idt = null)
            => fs.AddUploadFileFromServer(dbName, currentPath, ToFs(files), idt);

        public static Task<UploadPreviewResult> SfPreviewUploadFromServer(
            this IFileService fs, string dbName, string currentPath, List<FileManagerDirectoryContent> files, bool findDuplicates = false,
            int maxItems = UploadPreviewPlanner.DefaultMaxItems, CancellationToken cancellationToken = default)
            => fs.PreviewUploadFromServer(dbName, currentPath, ToFs(files), findDuplicates, maxItems, cancellationToken);

        public static Task<int> SfPreloadFilesToTemp(this IFileService fs, string channelId, List<FileManagerDirectoryContent> items)
            => fs.PreloadFilesToTemp(channelId, ToFs(items));
    }
}
