namespace TelegramDownloader.Models.Mobile
{
    /// <summary>
    /// Server capabilities advertised to mobile clients
    /// </summary>
    public class CapabilitiesDto
    {
        /// <summary>Version of the mobile API contract. Bumped on breaking changes.</summary>
        public int ApiVersion { get; set; }
        /// <summary>Server application version.</summary>
        public string ServerVersion { get; set; } = string.Empty;
        /// <summary>Optional features this server supports (stable identifiers).</summary>
        public List<string> Features { get; set; } = new();
    }

    /// <summary>
    /// Result of a global search across indexed channels and local files
    /// </summary>
    public class GlobalSearchResultDto
    {
        /// <summary>Matches grouped by channel. Only channels with at least one match are listed.</summary>
        public List<ChannelSearchGroupDto> Channels { get; set; } = new();
        /// <summary>Matches in the server's local files directory.</summary>
        public List<FileItemDto> Local { get; set; } = new();
        /// <summary>True when more matches existed than the requested limit.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// Search matches inside one channel
    /// </summary>
    public class ChannelSearchGroupDto
    {
        public string ChannelId { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public List<ChannelFileDto> Files { get; set; } = new();
    }
}
