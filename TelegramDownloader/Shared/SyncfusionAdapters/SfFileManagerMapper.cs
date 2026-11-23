#nullable disable
using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Models.FileSystem;

namespace TelegramDownloader.Shared.SyncfusionAdapters
{
    /// <summary>
    /// Conversions between the Syncfusion Blazor FileManager types the Blazor
    /// UI works with and the neutral <see cref="FsEntry"/> family the service
    /// layer exposes. Only the Blazor UI (Pages/, Shared/) uses this class, so
    /// removing the Blazor UI removes the last service-side dependency on
    /// Syncfusion together with it.
    /// <para>
    /// Every <see cref="FsEntry"/> property is copied both ways. The Syncfusion
    /// request-only members (Action, Names, NewName, SearchString, TargetPath,
    /// TargetData, Data, RenameFiles, Permission, UploadFiles) are not carried:
    /// no service ever read them from an entry.
    /// </para>
    /// </summary>
    public static class SfFileManagerMapper
    {
        public static FsEntry ToFs(FileManagerDirectoryContent sf)
        {
            if (sf == null) return null;
            return new FsEntry
            {
                Id = sf.Id,
                Name = sf.Name,
                IsFile = sf.IsFile,
                Size = sf.Size,
                DateCreated = sf.DateCreated,
                DateModified = sf.DateModified,
                FilterPath = sf.FilterPath,
                FilterId = sf.FilterId,
                ParentId = sf.ParentId,
                Type = sf.Type,
                HasChild = sf.HasChild,
                CaseSensitive = sf.CaseSensitive,
                ShowHiddenItems = sf.ShowHiddenItems,
                Path = sf.Path,
                PreviousName = sf.PreviousName
            };
        }

        public static FileManagerDirectoryContent ToSf(FsEntry fs)
        {
            if (fs == null) return null;
            return new FileManagerDirectoryContent
            {
                Id = fs.Id,
                Name = fs.Name,
                IsFile = fs.IsFile,
                Size = fs.Size,
                DateCreated = fs.DateCreated,
                DateModified = fs.DateModified,
                FilterPath = fs.FilterPath,
                FilterId = fs.FilterId,
                ParentId = fs.ParentId,
                Type = fs.Type,
                HasChild = fs.HasChild,
                CaseSensitive = fs.CaseSensitive,
                ShowHiddenItems = fs.ShowHiddenItems,
                Path = fs.Path,
                PreviousName = fs.PreviousName
            };
        }

        public static List<FsEntry> ToFs(IEnumerable<FileManagerDirectoryContent> items)
            => items?.Select(ToFs).ToList();

        public static List<FileManagerDirectoryContent> ToSf(IEnumerable<FsEntry> items)
            => items?.Select(ToSf).ToList();

        public static FileManagerResponse<FileManagerDirectoryContent> ToSfResponse(FsListing listing)
        {
            if (listing == null) return null;
            return new FileManagerResponse<FileManagerDirectoryContent>
            {
                CWD = ToSf(listing.CWD),
                Files = ToSf(listing.Files)
            };
        }

        /// <summary>Wraps a list of entries the way the services used to answer (only Files set).</summary>
        public static FileManagerResponse<FileManagerDirectoryContent> ToSfResponse(IEnumerable<FsEntry> files)
            => new FileManagerResponse<FileManagerDirectoryContent> { Files = ToSf(files) };

        public static FsMoveRequest ToFs(ItemsMoveEventArgs<FileManagerDirectoryContent> args)
        {
            if (args == null) return null;
            return new FsMoveRequest
            {
                Path = args.Path,
                TargetPath = args.TargetPath,
                TargetData = ToFs(args.TargetData),
                Files = args.Files?.Select(ToFs).ToArray(),
                IsCopy = args.IsCopy
            };
        }

        public static FsDeleteRequest ToFs(ItemsDeleteEventArgs<FileManagerDirectoryContent> args)
        {
            if (args == null) return null;
            return new FsDeleteRequest
            {
                Path = args.Path,
                Files = args.Files?.Select(ToFs).ToArray()
            };
        }

        public static FsCreateFolderRequest ToFs(FolderCreateEventArgs<FileManagerDirectoryContent> args)
        {
            if (args == null) return null;
            return new FsCreateFolderRequest
            {
                Path = args.Path,
                FolderName = args.FolderName,
                ParentFolder = ToFs(args.ParentFolder)
            };
        }
    }
}
