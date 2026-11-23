using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Mobile;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Global file search across every indexed channel and the server local
    /// storage. Same engine and results as <c>GET /api/mobile/search</c>
    /// (in-memory name index per channel), with v1 envelopes and DTOs.
    /// </summary>
    [Route("api/v1/search")]
    [Tags("Search")]
    public class SearchController : ApiV1ControllerBase
    {
        public const int MinQueryLength = 2;
        public const int DefaultLimit = 200;
        public const int MaxLimit = 500;
        private const int MaxChannelParallelism = 4;
        private const int MaxLocalDepth = 32;

        private readonly IDbService _db;
        private readonly ITelegramService _telegram;
        private readonly ChannelSearchIndex _index;
        private readonly ILogger<SearchController> _logger;

        public SearchController(IDbService db, ITelegramService telegram, ChannelSearchIndex index, ILogger<SearchController> logger)
        {
            _db = db;
            _telegram = telegram;
            _index = index;
            _logger = logger;
        }

        /// <summary>Searches files (not folders) by name everywhere.</summary>
        /// <remarks>
        /// Case-insensitive substring match on the file name.
        /// <list type="bullet">
        /// <item><c>q</c>: at least 2 characters.</item>
        /// <item><c>filter</c>: <c>all</c> (default), <c>audio</c>, <c>video</c>, <c>photo</c>, <c>document</c> or <c>archive</c>.</item>
        /// <item><c>scope</c>: <c>all</c> (default), <c>channels</c> or <c>local</c>.</item>
        /// <item><c>limit</c>: 1-500 (default 200), applied separately to channel and to local results.</item>
        /// </list>
        /// Channel results are grouped by channel (ordered by channel name) and
        /// carry the same media URLs as the browse endpoints. <c>truncated</c> is
        /// true when more matches existed. A channel whose index fails is skipped.
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<ApiGlobalSearchResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Search(
            [FromQuery] string? q,
            [FromQuery] string? filter = "all",
            [FromQuery] string? scope = "all",
            [FromQuery] int limit = DefaultLimit,
            CancellationToken cancellationToken = default)
        {
            var query = q?.Trim() ?? string.Empty;
            if (query.Length < MinQueryLength)
                return BadRequestResult($"q is required and must have at least {MinQueryLength} characters");

            if (!TryParseFilter(filter, out var category))
                return BadRequestResult("filter must be all, audio, video, photo, document or archive");

            var scopeLower = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();
            if (scopeLower != "all" && scopeLower != "channels" && scopeLower != "local")
                return BadRequestResult("scope must be all, channels or local");

            limit = Math.Clamp(limit, 1, MaxLimit);

            try
            {
                var result = new ApiGlobalSearchResultDto();

                if (scopeLower != "local")
                {
                    var (groups, truncated) = await SearchChannels(query, category, limit, cancellationToken);
                    result.Channels = groups;
                    result.Truncated |= truncated;
                }

                if (scopeLower != "channels")
                {
                    var (files, truncated) = SearchLocal(FileService.LOCALDIR, query, category, limit, BaseUrl, cancellationToken);
                    result.Local = files;
                    result.Truncated |= truncated;
                }

                return OkResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running the global search for {Query}", query);
                return ErrorResult("Could not run the search", ex);
            }
        }

        /// <summary>Maps the <c>filter</c> parameter to an <see cref="ApiFileDto.Category"/>; null means every category.</summary>
        public static bool TryParseFilter(string? filter, out string? category)
        {
            category = (filter ?? "all").Trim().ToLowerInvariant() switch
            {
                "" or "all" => null,
                "audio" => "Audio",
                "video" => "Video",
                "photo" or "photos" or "image" or "images" => "Photo",
                "document" or "documents" => "Document",
                "archive" or "archives" => "Archive",
                _ => "?"
            };
            if (category == "?")
            {
                category = null;
                return false;
            }
            return true;
        }

        private async Task<(List<ApiSearchChannelGroupDto>, bool)> SearchChannels(string query, string? category, int limit, CancellationToken ct)
        {
            var dbNames = await _db.GetAllChannelDatabaseNames() ?? new List<string>();
            var names = await GetChannelNames();
            var found = new ConcurrentBag<ApiSearchChannelGroupDto>();
            var anyHasMore = 0;
            var audioOnly = category == "Audio";
            // The name index only knows the audio flag: over-fetch for the other categories.
            var indexLimit = category == null || audioOnly ? limit : Math.Min(limit * 4, 2000);
            var baseUrl = BaseUrl;

            await Parallel.ForEachAsync(dbNames,
                new ParallelOptions { MaxDegreeOfParallelism = MaxChannelParallelism, CancellationToken = ct },
                async (dbName, token) =>
                {
                    if (!long.TryParse(dbName, out var channelId)) return;
                    try
                    {
                        var match = await _index.SearchAsync(dbName, query, audioOnly, indexLimit, token);
                        if (match.Ids.Count == 0) return;
                        if (match.HasMore) Interlocked.Exchange(ref anyHasMore, 1);

                        var docs = await _db.GetFilesByIds(dbName, match.Ids, token);
                        var files = docs
                            .Where(m => m.IsFile)
                            .Select(m => ApiFileDto.FromBson(m, dbName, baseUrl))
                            .Where(f => category == null || f.Category == category)
                            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (files.Count == 0) return;

                        found.Add(new ApiSearchChannelGroupDto
                        {
                            ChannelId = dbName,
                            ChannelName = names.TryGetValue(channelId, out var n) && !string.IsNullOrWhiteSpace(n) ? n : dbName,
                            Files = files
                        });
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Global search skipped channel database {Db}", dbName);
                    }
                });

            var (capped, truncated) = CapGroups(found, limit);
            return (capped, truncated || anyHasMore == 1);
        }

        /// <summary>
        /// Orders groups by channel name and keeps at most <paramref name="limit"/>
        /// files in total, dropping what is left over. Returns whether anything was cut.
        /// </summary>
        public static (List<ApiSearchChannelGroupDto> Groups, bool Truncated) CapGroups(IEnumerable<ApiSearchChannelGroupDto> groups, int limit)
        {
            var ordered = groups
                .OrderBy(g => g.ChannelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.ChannelId, StringComparer.Ordinal)
                .ToList();

            var result = new List<ApiSearchChannelGroupDto>();
            var remaining = limit;
            var truncated = false;
            foreach (var g in ordered)
            {
                if (remaining <= 0)
                {
                    truncated = true;
                    break;
                }
                if (g.Files.Count > remaining)
                {
                    g.Files = g.Files.Take(remaining).ToList();
                    truncated = true;
                }
                remaining -= g.Files.Count;
                result.Add(g);
            }
            return (result, truncated);
        }

        private async Task<Dictionary<long, string>> GetChannelNames()
        {
            try
            {
                var chats = await _telegram.getAllSavedChats();
                var dict = new Dictionary<long, string>();
                foreach (var c in chats)
                {
                    if (c?.chat == null) continue;
                    dict[c.chat.ID] = c.chat.Title;
                }
                return dict;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Global search could not load channel names; using ids");
                return new Dictionary<long, string>();
            }
        }

        /// <summary>Walks the local root looking for file names containing <paramref name="query"/>.</summary>
        public static (List<ApiFileDto> Files, bool Truncated) SearchLocal(
            string root, string query, string? category, int limit, string baseUrl, CancellationToken ct = default)
        {
            var results = new List<ApiFileDto>();
            var truncated = false;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return (results, false);

            var rootFull = Path.GetFullPath(root);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = MaxLocalDepth,
                ReturnSpecialDirectories = false
            };

            try
            {
                foreach (var path in Directory.EnumerateFiles(rootFull, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Path.GetFileName(path).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    ApiFileDto dto;
                    try
                    {
                        var info = new FileInfo(path);
                        dto = ApiFileDto.FromLocalFile(info, Path.GetRelativePath(rootFull, info.FullName).Replace("\\", "/"), baseUrl);
                    }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    if (category != null && dto.Category != category) continue;
                    if (results.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }
                    results.Add(dto);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));
            return (results, truncated);
        }
    }

    /// <summary>Result of <c>GET /api/v1/search</c>.</summary>
    public class ApiGlobalSearchResultDto
    {
        /// <summary>Matches per channel; only channels with matches appear.</summary>
        public List<ApiSearchChannelGroupDto> Channels { get; set; } = new();

        /// <summary>Matches in the server local storage.</summary>
        public List<ApiFileDto> Local { get; set; } = new();

        /// <summary>More matches existed than <c>limit</c>.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>Matches of one channel.</summary>
    public class ApiSearchChannelGroupDto
    {
        public string ChannelId { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public List<ApiFileDto> Files { get; set; } = new();
    }
}
