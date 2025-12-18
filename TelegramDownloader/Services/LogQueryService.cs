using MongoDB.Bson;
using MongoDB.Driver;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    public interface ILogQueryService
    {
        Task<LogQueryResult> GetLogs(LogQueryRequest request);
        Task<List<string>> GetLoggerNames();
        Task<List<string>> GetVersions();
        Task<LogStats> GetStats();
        Task<long> DeleteOldLogs(int daysToKeep);
        void ReinitializeConnection(string connectionString);
        bool IsInitialized { get; }
    }

    public class LogQueryService : ILogQueryService
    {
        private IMongoCollection<LogEntry>? _logs;
        private readonly ILogger<LogQueryService> _logger;
        private bool _indexesCreated = false;
        private bool _isInitialized = false;

        public bool IsInitialized => _isInitialized;

        public LogQueryService(string connectionString, ILogger<LogQueryService> logger)
        {
            _logger = logger;

            try
            {
                InitializeConnection(connectionString);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize LogQueryService - will retry when connection is available");
                _isInitialized = false;
            }
        }

        private void InitializeConnection(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("LogQueryService: Connection string is empty");
                _isInitialized = false;
                return;
            }

            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(5);

            var client = new MongoClient(settings);
            var database = client.GetDatabase("TFM_Logs");
            _logs = database.GetCollection<LogEntry>("logs");
            _isInitialized = true;
            _indexesCreated = false;

            // Create indexes asynchronously
            _ = CreateIndexesAsync();
        }

        public void ReinitializeConnection(string connectionString)
        {
            _logger.LogInformation("Reinitializing LogQueryService connection...");
            InitializeConnection(connectionString);
            _logger.LogInformation("LogQueryService reinitialized successfully");
        }

        private async Task CreateIndexesAsync()
        {
            if (_indexesCreated || _logs == null) return;

            try
            {
                // Index for timestamp (most common query)
                await _logs.Indexes.CreateOneAsync(new CreateIndexModel<LogEntry>(
                    Builders<LogEntry>.IndexKeys.Descending(x => x.Timestamp)
                ));

                // Compound index for timestamp + level
                await _logs.Indexes.CreateOneAsync(new CreateIndexModel<LogEntry>(
                    Builders<LogEntry>.IndexKeys
                        .Descending(x => x.Timestamp)
                        .Ascending(x => x.Level)
                ));

                // Index for level filtering
                await _logs.Indexes.CreateOneAsync(new CreateIndexModel<LogEntry>(
                    Builders<LogEntry>.IndexKeys.Ascending(x => x.Level)
                ));

                _indexesCreated = true;
                _logger.LogInformation("Log indexes created successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create log indexes (may already exist)");
            }
        }

        public async Task<LogQueryResult> GetLogs(LogQueryRequest request)
        {
            if (!_isInitialized || _logs == null)
            {
                return new LogQueryResult
                {
                    Logs = new List<LogEntry>(),
                    TotalCount = 0,
                    Page = request.Page,
                    PageSize = request.PageSize
                };
            }

            try
            {
                var filterBuilder = Builders<LogEntry>.Filter;
                var filter = filterBuilder.Empty;

                // Date filter
                if (request.FromDate.HasValue)
                {
                    filter &= filterBuilder.Gte(x => x.Timestamp, request.FromDate.Value.ToUniversalTime());
                }
                if (request.ToDate.HasValue)
                {
                    // Use exact datetime (add 1 second to include the exact second)
                    filter &= filterBuilder.Lte(x => x.Timestamp, request.ToDate.Value.AddSeconds(1).ToUniversalTime());
                }

                // Level filter
                if (!string.IsNullOrEmpty(request.Level))
                {
                    filter &= filterBuilder.Eq(x => x.Level, request.Level);
                }

                // Logger filter (search in Properties.SourceContext)
                if (!string.IsNullOrEmpty(request.Logger))
                {
                    filter &= filterBuilder.Regex("Properties.SourceContext",
                        new BsonRegularExpression(request.Logger, "i"));
                }

                // Version filter (search in Properties.AppVersion)
                if (!string.IsNullOrEmpty(request.Version))
                {
                    filter &= filterBuilder.Eq("Properties.AppVersion", request.Version);
                }

                // Text search in message
                if (!string.IsNullOrEmpty(request.SearchText))
                {
                    filter &= filterBuilder.Regex(x => x.Message,
                        new BsonRegularExpression(request.SearchText, "i"));
                }

                var totalCount = await _logs.CountDocumentsAsync(filter);

                var sort = request.DescendingOrder
                    ? Builders<LogEntry>.Sort.Descending(x => x.Timestamp)
                    : Builders<LogEntry>.Sort.Ascending(x => x.Timestamp);

                var logs = await _logs.Find(filter)
                    .Sort(sort)
                    .Skip((request.Page - 1) * request.PageSize)
                    .Limit(request.PageSize)
                    .ToListAsync();

                return new LogQueryResult
                {
                    Logs = logs,
                    TotalCount = (int)totalCount,
                    Page = request.Page,
                    PageSize = request.PageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying logs");
                return new LogQueryResult
                {
                    Logs = new List<LogEntry>(),
                    TotalCount = 0,
                    Page = request.Page,
                    PageSize = request.PageSize
                };
            }
        }

        public async Task<List<string>> GetLoggerNames()
        {
            if (!_isInitialized || _logs == null)
            {
                return new List<string>();
            }

            try
            {
                var pipeline = new BsonDocument[]
                {
                    new BsonDocument("$group", new BsonDocument("_id", "$Properties.SourceContext")),
                    new BsonDocument("$sort", new BsonDocument("_id", 1)),
                    new BsonDocument("$limit", 100)
                };

                var result = await _logs.Aggregate<BsonDocument>(pipeline).ToListAsync();

                return result
                    .Where(x => x["_id"] != BsonNull.Value)
                    .Select(x => x["_id"].ToString() ?? "Unknown")
                    .Where(x => !string.IsNullOrEmpty(x))
                    .OrderBy(x => x)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting logger names");
                return new List<string>();
            }
        }

        public async Task<List<string>> GetVersions()
        {
            if (!_isInitialized || _logs == null)
            {
                return new List<string>();
            }

            try
            {
                var pipeline = new BsonDocument[]
                {
                    new BsonDocument("$group", new BsonDocument("_id", "$Properties.AppVersion")),
                    new BsonDocument("$sort", new BsonDocument("_id", -1)),
                    new BsonDocument("$limit", 50)
                };

                var result = await _logs.Aggregate<BsonDocument>(pipeline).ToListAsync();

                return result
                    .Where(x => x["_id"] != BsonNull.Value)
                    .Select(x => x["_id"].ToString() ?? "Unknown")
                    .Where(x => !string.IsNullOrEmpty(x))
                    .OrderByDescending(x => x)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting versions");
                return new List<string>();
            }
        }

        public async Task<LogStats> GetStats()
        {
            if (!_isInitialized || _logs == null)
            {
                return new LogStats();
            }

            try
            {
                var stats = new LogStats();

                // Get total count
                stats.TotalLogs = await _logs.CountDocumentsAsync(FilterDefinition<LogEntry>.Empty);

                // Get counts by level
                var levelCounts = await _logs.Aggregate()
                    .Group(x => x.Level, g => new { Level = g.Key, Count = g.Count() })
                    .ToListAsync();

                foreach (var lc in levelCounts)
                {
                    switch (lc.Level)
                    {
                        case "Error":
                        case "Fatal":
                            stats.ErrorCount += lc.Count;
                            break;
                        case "Warning":
                            stats.WarningCount = lc.Count;
                            break;
                        case "Information":
                            stats.InfoCount = lc.Count;
                            break;
                        case "Debug":
                        case "Verbose":
                            stats.DebugCount += lc.Count;
                            break;
                    }
                }

                // Get oldest and newest logs
                var oldest = await _logs.Find(FilterDefinition<LogEntry>.Empty)
                    .Sort(Builders<LogEntry>.Sort.Ascending(x => x.Timestamp))
                    .Limit(1)
                    .FirstOrDefaultAsync();

                var newest = await _logs.Find(FilterDefinition<LogEntry>.Empty)
                    .Sort(Builders<LogEntry>.Sort.Descending(x => x.Timestamp))
                    .Limit(1)
                    .FirstOrDefaultAsync();

                stats.OldestLog = oldest?.Timestamp;
                stats.NewestLog = newest?.Timestamp;

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting log stats");
                return new LogStats();
            }
        }

        public async Task<long> DeleteOldLogs(int daysToKeep)
        {
            if (!_isInitialized || _logs == null)
            {
                return 0;
            }

            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
                var filter = Builders<LogEntry>.Filter.Lt(x => x.Timestamp, cutoffDate);
                var result = await _logs.DeleteManyAsync(filter);

                _logger.LogInformation("Deleted {Count} logs older than {Days} days",
                    result.DeletedCount, daysToKeep);

                return result.DeletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting old logs");
                return 0;
            }
        }
    }
}
