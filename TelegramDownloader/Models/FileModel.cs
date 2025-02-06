using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Data.db;
using Syncfusion.EJ2.Linq;
using TL;

namespace TelegramDownloader.Models
{
    public class FileModel
    {
        public string Id { get; set; }
        public string FileName { get; set; }

    }

    public class FolderModel
    {
        public string Id { get; set; }
        public string FolderName { get; set; }
        public bool Expanded { get; set; }
        public List<FolderModel> Folders { get; set; }
    }

    public class BsonFileManagerModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public bool CaseSensitive { get; set; } = false;
        [BsonRepresentation(BsonType.Document)]
        public DateTime DateCreated { get; set; } = DateTime.Now;
        [BsonRepresentation(BsonType.Document)]
        public DateTime DateModified { get; set; } = DateTime.Now;
        public string FilterId { get; set; }
        public string FilterPath { get; set; }
        public bool HasChild { get; set; }
        public bool IsFile { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string ParentId { get; set; }
        public bool ShowHiddenItems { get; set; }
        public long Size { get; set; }
        public string Type { get; set; }
        public int? MessageId { get; set; }
        public bool isSplit { get; set; } = false;
        public List<int>? ListMessageId { get; set; }
        public bool isEncrypted { get; set; } = false;
        public string MD5Hash { get; set; }
        public string XXHash { get; set; }

        public BsonFileManagerModel()
        {

        }

        public BsonFileManagerModel(string Path,string FolderName, FileManagerDirectoryContent parent)
        {
            this.ParentId = parent.Id;
            this.CaseSensitive = false;
            this.Name = FolderName;
            this.FilePath = System.IO.Path.Combine(Path, FolderName);
            this.FilterId = (string.IsNullOrEmpty(parent.FilterId) ? String.Concat(parent.Id, "/") : String.Concat(System.IO.Path.Combine(parent.FilterId, parent.Id), "/"));
            this.FilterPath = (string.IsNullOrEmpty(parent.FilterId) ? "/" : String.Concat(parent.FilterPath, parent.Name, "/"));
            this.ShowHiddenItems = false;
            this.IsFile = false;
            this.HasChild = true;
            this.Size = 0;
            this.Type = "folder";
        }

        public BsonFileManagerModel(Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent fm)
        {
            this.ParentId = fm.Data.FirstOrDefault()?.Id;
            this.CaseSensitive = fm.CaseSensitive;
            this.Name = fm.Name;
            this.FilePath = fm.Path;
            this.FilterId = fm.FilterId ?? (string.IsNullOrEmpty(fm.TargetData.FilterId) ? String.Concat(fm.TargetData.Id, "/") : String.Concat(System.IO.Path.Combine(fm.TargetData.FilterId, fm.TargetData.Id), "/"));
            this.FilterPath = fm.FilterPath ?? (string.IsNullOrEmpty(fm.TargetData.FilterId) ? "/" : String.Concat(fm.TargetData.FilterPath, fm.TargetData.Name, "/"));
            this.ShowHiddenItems = fm.ShowHiddenItems;
            this.IsFile = fm.IsFile;
            this.HasChild = true;
            this.Size = fm.Size;
            this.Type = fm.Type ?? "folder";
        }

        public BsonFileManagerModel(FileManagerDirectoryContent fm)
        {
            this.ParentId = fm.Data.FirstOrDefault()?.Id;
            this.CaseSensitive = fm.CaseSensitive;
            this.Name = fm.Name;
            this.FilePath = fm.Path;
            this.FilterId = fm.FilterId ?? (string.IsNullOrEmpty(fm.TargetData.FilterId) ? String.Concat(fm.TargetData.Id, "/") : String.Concat(System.IO.Path.Combine(fm.TargetData.FilterId, fm.TargetData.Id), "/"));
            this.FilterPath = fm.FilterPath ?? (string.IsNullOrEmpty(fm.TargetData.FilterId) ? "/" : String.Concat(fm.TargetData.FilterPath, fm.TargetData.Name, "/"));
            this.ShowHiddenItems = fm.ShowHiddenItems;
            this.IsFile = fm.IsFile;
            this.HasChild = true;
            this.Size = fm.Size;
            this.Type = fm.Type ?? "folder";
        }

        public Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent toEJ2FileManagerContent()
        {
            return new Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent()
            {
                Id = this.Id,
                CaseSensitive = this.CaseSensitive,
                DateCreated = this.DateCreated,
                DateModified = this.DateModified,
                FilterId = this.FilterId,
                FilterPath = this.FilterPath,
                HasChild = this.HasChild,
                IsFile = this.IsFile,
                Name = this.Name,
                ParentId = this.ParentId,
                ShowHiddenItems = this.ShowHiddenItems,
                Size = this.Size,
                Type = this.Type
            };
        }

        public FileManagerDirectoryContent toFileManagerContent()
        {
            return new FileManagerDirectoryContent()
            {
                Id = this.Id,
                CaseSensitive = this.CaseSensitive,
                DateCreated = this.DateCreated,
                DateModified = this.DateModified,
                FilterId = this.FilterId,
                FilterPath = this.FilterPath,
                HasChild = this.HasChild,
                IsFile = this.IsFile,
                Name = this.Name,
                ParentId = this.ParentId,
                ShowHiddenItems = this.ShowHiddenItems,
                Size = this.Size,
                Type = this.Type
            };
        }

        public FileManagerDirectoryContent toFileManagerContentInCopy()
        {
            return new FileManagerDirectoryContent()
            {
                DateCreated = this.DateCreated,
                DateModified = this.DateModified,
                FilterPath = this.FilterPath,
                Name = this.Name,
                HasChild = this.HasChild,
                Size = this.Size,
                Type = this.Type,
                PreviousName = this.Name
            };
        }

        public FileDetails toFileDetails()
        {
            return new FileDetails()
            {
                IsFile = this.IsFile,
                Name = this.Name,
                Size = this.Size.ToString(),
                MultipleFiles = this.HasChild,
                Created = this.DateCreated.ToLongDateString(),
                Modified = this.DateModified.ToLongDateString(),
                Location = this.FilterPath

            };
        }
    }

    public class FileManagerModel
    {
        public string Action { get; set; }
        public bool CaseSensitive { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public string FilterId { get; set; }
        public string FilterPath { get; set; }
        public bool HasChild { get; set; }
        public string Id { get; set; }
        public bool IsFile { get; set; }
        public string Name { get; set; }
        public string ParentId { get; set; }
        public bool ShowHiddenItems { get; set; }
        public long Size { get; set; }
        public string Type { get; set; }
        public bool showFileExtension { get; set; } = false;
    }

    public class DirectorySizeModel
    {
        public long SizeBytes { get; set; }
        public string SizeWithSuffix { get; set; }
        public long TotalElements { get; set; }

    }
}
