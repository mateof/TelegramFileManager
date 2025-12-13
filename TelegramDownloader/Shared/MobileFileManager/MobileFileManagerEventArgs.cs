using Syncfusion.Blazor.FileManager;

namespace TelegramDownloader.Shared.MobileFileManager
{
    // Custom event args classes for MobileFileManager since Syncfusion's are read-only

    public class MfmReadEventArgs
    {
        public string Path { get; set; } = "/";
        public FileManagerResponse<FileManagerDirectoryContent>? Response { get; set; }
    }

    public class MfmDeleteEventArgs
    {
        public FileManagerDirectoryContent[] Files { get; set; } = Array.Empty<FileManagerDirectoryContent>();
        public string Path { get; set; } = "/";
        public FileManagerResponse<FileManagerDirectoryContent>? Response { get; set; }
    }

    public class MfmMoveEventArgs
    {
        public FileManagerDirectoryContent[] Files { get; set; } = Array.Empty<FileManagerDirectoryContent>();
        public string SourcePath { get; set; } = "/";
        public string TargetPath { get; set; } = "/";
        public FileManagerDirectoryContent? TargetData { get; set; }
        public bool IsCopy { get; set; }
        public FileManagerResponse<FileManagerDirectoryContent>? Response { get; set; }
    }

    public class MfmRenameEventArgs
    {
        public FileManagerDirectoryContent? File { get; set; }
        public string NewName { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public FileManagerResponse<FileManagerDirectoryContent>? Response { get; set; }
    }

    public class MfmFolderCreateEventArgs
    {
        public string FolderName { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public FileManagerDirectoryContent? ParentFolder { get; set; }
        public FileManagerResponse<FileManagerDirectoryContent>? Response { get; set; }
    }

    public class MfmSearchEventArgs
    {
        public string Path { get; set; } = "/";
        public string SearchText { get; set; } = string.Empty;
        public FileManagerResponse<FileManagerDirectoryContent>? Response { get; set; }
    }

    public class MfmFileOpenEventArgs
    {
        public FileManagerDirectoryContent? FileDetails { get; set; }
        public bool Cancel { get; set; }
    }

    public class MfmDownloadEventArgs
    {
        public string[] Names { get; set; } = Array.Empty<string>();
        public string Path { get; set; } = "/";
        public FileManagerDirectoryContent[] Files { get; set; } = Array.Empty<FileManagerDirectoryContent>();
    }

    public class MfmDownloadToLocalEventArgs
    {
        public FileManagerDirectoryContent[] Files { get; set; } = Array.Empty<FileManagerDirectoryContent>();
        public string Path { get; set; } = "/";
    }

    public class MfmShareFileEventArgs
    {
        public FileManagerDirectoryContent? File { get; set; }
    }

    public class MfmShowInAppEventArgs
    {
        public FileManagerDirectoryContent? File { get; set; }
    }

    public class MfmUrlMediaEventArgs
    {
        public FileManagerDirectoryContent? File { get; set; }
    }

    public class MfmUploadToTelegramEventArgs
    {
        public FileManagerDirectoryContent[] Files { get; set; } = Array.Empty<FileManagerDirectoryContent>();
        public string Path { get; set; } = "/";
    }

    public class MfmUploadToLocalEventArgs
    {
        public string Path { get; set; } = "/";
    }

    public class MfmStrmEventArgs
    {
        public FileManagerDirectoryContent? Folder { get; set; }
        public string Path { get; set; } = "/";
    }

    public class MfmPreloadFilesEventArgs
    {
        public FileManagerDirectoryContent[] Items { get; set; } = Array.Empty<FileManagerDirectoryContent>();
        public string Path { get; set; } = "/";
    }

    public class MfmAddToPlaylistEventArgs
    {
        public FileManagerDirectoryContent? File { get; set; }
        public FileManagerDirectoryContent[] Files { get; set; } = Array.Empty<FileManagerDirectoryContent>();
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Type { get; set; } = "audio/mpeg";
        public bool IsMultiple => Files.Length > 0;
    }
}
