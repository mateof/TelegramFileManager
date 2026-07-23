using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Server health, resource usage, application logs and maintenance of the
    /// channel index databases.
    /// </summary>
    [Route("api/v1/system")]
    [Tags("System")]
    public class SystemController : ApiV1ControllerBase
    {
        private readonly ITelegramService _telegram;
        private readonly ISetupService _setup;
        private readonly ISystemMetricsService _metrics;
        private readonly ILogQueryService _logs;
        private readonly IDbService _db;
        private readonly ILogger<SystemController> _logger;

        public SystemController(
            ITelegramService telegram,
            ISetupService setup,
            ISystemMetricsService metrics,
            ILogQueryService logs,
            IDbService db,
            ILogger<SystemController> logger)
        {
            _telegram = telegram;
            _setup = setup;
            _metrics = metrics;
            _logs = logs;
            _db = db;
            _logger = logger;
        }

        /// <summary>Liveness probe.</summary>
        /// <remarks>
        /// Always answers <c>200</c> when the process is up. Use it to verify
        /// connectivity and, when an API key is configured, that the key works.
        /// </remarks>
        [HttpGet("ping")]
        [ProducesResponseType(typeof(ApiResult<string>), StatusCodes.Status200OK)]
        public IActionResult Ping() => OkResult("pong");

        /// <summary>Server identity, versions and readiness.</summary>
        /// <remarks>
        /// The natural first call of a mobile client: it reports whether setup
        /// is complete, whether a Telegram session is active, and the path of
        /// the SignalR hub to connect to.
        /// </remarks>
        [HttpGet("info")]
        [ProducesResponseType(typeof(ApiResult<ServerInfoDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Info()
        {
            var dto = new ServerInfoDto
            {
                Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                TelegramConfigured = _telegram.IsConfigured,
                RequiresApiKey = !string.IsNullOrEmpty(GeneralConfigStatic.tlconfig?.mobile_api_key),
                WebDavRunning = GeneralConfigStatic.config?.webDav?.webDavService?.IsRunning ?? false
            };

            try
            {
                dto.TelegramAuthenticated = _telegram.IsConfigured && _telegram.checkUserLogin();
            }
            catch
            {
                dto.TelegramAuthenticated = false;
            }

            try
            {
                var status = await _setup.GetSetupStatusAsync();
                dto.SetupComplete = status.CurrentStep == SetupStep.Complete;
                dto.MongoConnected = status.MongoDbConnected;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read the setup status");
            }

            return OkResult(dto);
        }

        /// <summary>Progress of the first-run wizard.</summary>
        [HttpGet("setup")]
        [ProducesResponseType(typeof(ApiResult<SetupStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Setup()
        {
            try
            {
                var status = await _setup.GetSetupStatusAsync();
                return OkResult(new SetupStatusDto
                {
                    CurrentStep = status.CurrentStep.ToString(),
                    MongoDbConfigured = status.MongoDbConfigured,
                    MongoDbConnected = status.MongoDbConnected,
                    TelegramConfigured = status.TelegramConfigured,
                    MongoDbError = status.MongoDbError
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading the setup status");
                return ErrorResult("Could not read the setup status", ex);
            }
        }

        /// <summary>CPU, memory and disk usage of the server.</summary>
        [HttpGet("metrics")]
        [ProducesResponseType(typeof(ApiResult<SystemMetricsDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Metrics()
        {
            try
            {
                var metrics = await _metrics.GetMetricsAsync();
                return OkResult(SystemMetricsDto.From(metrics));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading system metrics");
                return ErrorResult("Could not read the system metrics", ex);
            }
        }

        /// <summary>Queries the application logs.</summary>
        /// <remarks>
        /// Logs live in the <c>TFM_Logs</c> MongoDB database. When MongoDB is
        /// not configured the endpoint answers <c>503</c>.
        /// </remarks>
        [HttpGet("logs")]
        [ProducesResponseType(typeof(ApiResult<List<LogEntryDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Logs([FromQuery] LogQuery query)
        {
            if (!_logs.IsInitialized)
                return UnavailableResult("The log store is not available");

            try
            {
                var result = await _logs.GetLogs(new LogQueryRequest
                {
                    Page = query.Page,
                    PageSize = query.PageSize,
                    FromDate = query.FromDate,
                    ToDate = query.ToDate,
                    Level = query.Level,
                    Logger = query.Logger,
                    Version = query.Version,
                    SearchText = query.Search,
                    DescendingOrder = !query.SortDescending ? true : query.SortDescending
                });

                var items = (result.Logs ?? new List<LogEntry>()).Select(l => new LogEntryDto
                {
                    Id = l.Id ?? string.Empty,
                    Timestamp = l.Timestamp,
                    Level = l.Level,
                    Message = l.Message,
                    Logger = l.Logger,
                    Exception = l.Exception,
                    Version = l.Version
                }).ToList();

                return OkPaged(items, PageInfo.Create(result.Page, result.PageSize, result.TotalCount));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying the logs");
                return ErrorResult("Could not query the logs", ex);
            }
        }

        /// <summary>Distinct logger names present in the log store.</summary>
        [HttpGet("logs/loggers")]
        [ProducesResponseType(typeof(ApiResult<List<string>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> LogLoggers()
        {
            if (!_logs.IsInitialized) return UnavailableResult("The log store is not available");
            return OkResult(await _logs.GetLoggerNames());
        }

        /// <summary>Application versions present in the log store.</summary>
        [HttpGet("logs/versions")]
        [ProducesResponseType(typeof(ApiResult<List<string>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> LogVersions()
        {
            if (!_logs.IsInitialized) return UnavailableResult("The log store is not available");
            return OkResult(await _logs.GetVersions());
        }

        /// <summary>Deletes log records older than the given number of days.</summary>
        [HttpDelete("logs")]
        [ProducesResponseType(typeof(ApiResult<long>), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteLogs([FromQuery] int daysToKeep = 30)
        {
            if (!_logs.IsInitialized) return UnavailableResult("The log store is not available");
            if (daysToKeep < 0) return BadRequestResult("daysToKeep cannot be negative");

            var deleted = await _logs.DeleteOldLogs(daysToKeep);
            return OkResult(deleted, $"{deleted} log entries deleted");
        }

        /// <summary>Lists the channel index databases and their size.</summary>
        [HttpGet("databases")]
        [ProducesResponseType(typeof(ApiResult<List<DatabaseStatsDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Databases()
        {
            try
            {
                var names = await _db.GetAllChannelDatabaseNames() ?? new List<string>();
                var result = new List<DatabaseStatsDto>();

                foreach (var name in names)
                {
                    var dto = new DatabaseStatsDto { ChannelId = name };
                    try
                    {
                        var stats = await _db.GetDatabaseStats(name);
                        dto.SizeInBytes = stats.SizeInBytes;
                        dto.SizeText = HelperService.SizeSuffix(stats.SizeInBytes);
                        dto.DocumentCount = stats.DocumentCount;
                        dto.CreatedAt = stats.CreatedAt;
                        dto.LastModified = stats.LastModified;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not read stats of database {Name}", name);
                    }

                    if (long.TryParse(name, out var channelId))
                    {
                        try { dto.ChannelName = _telegram.getChatName(channelId); }
                        catch { /* the account may have left the channel */ }
                    }

                    result.Add(dto);
                }

                return OkResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing the channel databases");
                return ErrorResult("Could not list the channel databases", ex);
            }
        }

        /// <summary>Checks a channel index for broken folder paths.</summary>
        /// <remarks>
        /// Older versions could store inconsistent <c>FilterPath</c>/<c>FilterId</c>
        /// values, which shows up as folders that look empty. Analyse first, then
        /// repair with the endpoint below.
        /// </remarks>
        [HttpGet("databases/{channelId}/analysis")]
        [ProducesResponseType(typeof(ApiResult<PathAnalysisDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AnalyzeDatabase(string channelId)
        {
            try
            {
                var result = await _db.AnalyzeFilterPaths(channelId);
                return OkResult(new PathAnalysisDto
                {
                    DatabaseName = result.DatabaseName,
                    TotalItems = result.TotalItems,
                    ItemsWithIssues = result.ItemsWithIssues,
                    FilterPathIssues = result.FilterPathIssues,
                    FilterIdIssues = result.FilterIdIssues,
                    FilePathIssues = result.FilePathIssues,
                    HasIssues = result.HasIssues,
                    Error = result.Error
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analysing database {ChannelId}", channelId);
                return ErrorResult("Could not analyse the channel database", ex);
            }
        }

        /// <summary>Repairs the broken folder paths of a channel index.</summary>
        [HttpPost("databases/{channelId}/repair")]
        [ProducesResponseType(typeof(ApiResult<int>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RepairDatabase(string channelId)
        {
            try
            {
                var repaired = await _db.RepairFilterPaths(channelId);
                return OkResult(repaired, $"{repaired} entries repaired");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error repairing database {ChannelId}", channelId);
                return ErrorResult("Could not repair the channel database", ex);
            }
        }

        /// <summary>Deletes persisted tasks that are older than the configured limit.</summary>
        [HttpPost("maintenance/cleanup-tasks")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CleanupTasks([FromServices] ITaskPersistenceService persistence)
        {
            try
            {
                await persistence.CleanupStaleTasks();
                return OkEmpty("Stale tasks cleaned up");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up stale tasks");
                return ErrorResult("Could not clean up the stale tasks", ex);
            }
        }
    }
}
