namespace TelegramDownloader.Models
{
    public class TreeNodeModel
    {
        public string Id { get; set; } = string.Empty;
        public string OriginalId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public bool HasChildren { get; set; }
        public bool IsExpanded { get; set; } = false;
    }
}
