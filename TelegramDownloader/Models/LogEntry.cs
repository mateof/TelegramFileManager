using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace TelegramDownloader.Models
{
    /// <summary>
    /// Represents a log entry stored in MongoDB
    /// </summary>
    [BsonIgnoreExtraElements]
    public class LogEntry
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("UtcTimeStamp")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime Timestamp { get; set; }

        [BsonElement("Level")]
        public string Level { get; set; } = "Information";

        [BsonElement("RenderedMessage")]
        public string Message { get; set; } = string.Empty;

        [BsonElement("Exception")]
        public BsonValue? ExceptionRaw { get; set; }

        [BsonElement("Properties")]
        public BsonDocument? Properties { get; set; }

        // Helper properties for display
        [BsonIgnore]
        public string? Exception => ExceptionRaw?.IsBsonDocument == true
            ? ExceptionRaw.AsBsonDocument.ToString()
            : ExceptionRaw?.ToString();

        [BsonIgnore]
        public string Logger => GetPropertyValue("SourceContext") ?? "Unknown";

        [BsonIgnore]
        public string Version => GetPropertyValue("AppVersion") ?? "Unknown";

        [BsonIgnore]
        public string ShortLogger
        {
            get
            {
                var logger = Logger;
                if (string.IsNullOrEmpty(logger)) return "Unknown";
                var parts = logger.Split('.');
                return parts.Length > 0 ? parts[^1] : logger;
            }
        }

        private string? GetPropertyValue(string key)
        {
            if (Properties == null) return null;
            if (Properties.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }
            return null;
        }
    }

    /// <summary>
    /// Request parameters for log queries
    /// </summary>
    public class LogQueryRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? Level { get; set; }
        public string? Logger { get; set; }
        public string? Version { get; set; }
        public string? SearchText { get; set; }
        public bool DescendingOrder { get; set; } = true;
    }

    /// <summary>
    /// Result of a log query with pagination info
    /// </summary>
    public class LogQueryResult
    {
        public List<LogEntry> Logs { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    }

    /// <summary>
    /// Statistics about log entries
    /// </summary>
    public class LogStats
    {
        public long TotalLogs { get; set; }
        public long ErrorCount { get; set; }
        public long WarningCount { get; set; }
        public long InfoCount { get; set; }
        public long DebugCount { get; set; }
        public DateTime? OldestLog { get; set; }
        public DateTime? NewestLog { get; set; }
    }
}
