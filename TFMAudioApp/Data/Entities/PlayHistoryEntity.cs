using SQLite;

namespace TFMAudioApp.Data.Entities;

/// <summary>
/// Play history entry
/// </summary>
[Table("PlayHistory")]
public class PlayHistoryEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string TrackId { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public string? ChannelName { get; set; }
    public string? Artist { get; set; }
    public DateTime PlayedAt { get; set; }
}
