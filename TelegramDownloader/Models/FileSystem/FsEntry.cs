#nullable disable
namespace TelegramDownloader.Models.FileSystem
{
    /// <summary>
    /// One file or folder as the service layer exchanges it: an entry of a
    /// channel index (<see cref="BsonFileManagerModel"/>) or of the local
    /// folder. It replaces the UI component library's
    /// <c>FileManagerDirectoryContent</c> in <c>IFileService</c>/<c>IDbService</c>,
    /// so the services do not depend on it. The property names are the ones that
    /// type uses, which keeps the conversion in the Blazor adapter
    /// (<c>SfFileManagerMapper</c>) one to one.
    /// </summary>
    public class FsEntry
    {
        /// <summary>Id of the index entry (MongoDB ObjectId as a string).</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsFile { get; set; }
        public long Size { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        /// <summary>Path of the parent folder, with a leading and a trailing slash ("/a/b/").</summary>
        public string FilterPath { get; set; }
        /// <summary>Chain of ancestor ids, "id1/id2/".</summary>
        public string FilterId { get; set; }
        public string ParentId { get; set; }
        /// <summary>Extension with its dot for files, "folder" for indexed folders.</summary>
        public string Type { get; set; }
        public bool HasChild { get; set; }
        public bool CaseSensitive { get; set; }
        public bool ShowHiddenItems { get; set; }
        /// <summary>Full path of the item; only set by some sources.</summary>
        public string Path { get; set; }
        /// <summary>Name the item had before a copy/move; set on copy results.</summary>
        public string PreviousName { get; set; }
    }

    /// <summary>The content of a folder: the folder itself and its children.</summary>
    public class FsListing
    {
        /// <summary>The folder that was read (current working directory).</summary>
        public FsEntry CWD { get; set; }
        public List<FsEntry> Files { get; set; }
    }

    /// <summary>Copy or move of several entries into a folder.</summary>
    public class FsMoveRequest
    {
        /// <summary>Path of the folder the entries come from.</summary>
        public string Path { get; set; }
        /// <summary>Path of the destination folder ("/a/b/").</summary>
        public string TargetPath { get; set; }
        /// <summary>The destination folder.</summary>
        public FsEntry TargetData { get; set; }
        public FsEntry[] Files { get; set; } = Array.Empty<FsEntry>();
        /// <summary>True to copy, false to move.</summary>
        public bool IsCopy { get; set; }
    }

    /// <summary>Deletion of several entries of a folder.</summary>
    public class FsDeleteRequest
    {
        /// <summary>Path of the folder whose content is answered after the deletion.</summary>
        public string Path { get; set; }
        public FsEntry[] Files { get; set; } = Array.Empty<FsEntry>();
    }

    /// <summary>Creation of a folder inside <see cref="ParentFolder"/>.</summary>
    public class FsCreateFolderRequest
    {
        /// <summary>Path of the parent folder ("/a/b/").</summary>
        public string Path { get; set; }
        public string FolderName { get; set; }
        public FsEntry ParentFolder { get; set; }
    }
}
