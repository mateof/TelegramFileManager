using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models.Mobile;

namespace TelegramDownloader.Controllers.Mobile
{
    /// <summary>
    /// Búsqueda global de archivos por nombre en todos los canales indexados
    /// y en el directorio local del servidor.
    /// </summary>
    [Route("api/mobile/search")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Search")]
    public class MobileSearchController : ControllerBase
    {
        public const int MinQueryLength = 2;
        public const int DefaultLimit = 200;
        public const int MaxLimit = 500;
        private const int MaxChannelParallelism = 4;
        private const int MaxLocalDepth = 32;

        private readonly IDbService _db;
        private readonly ITelegramService _ts;
        private readonly ILogger<MobileSearchController> _logger;

        public MobileSearchController(IDbService db, ITelegramService ts, ILogger<MobileSearchController> logger)
        {
            _db = db;
            _ts = ts;
            _logger = logger;
        }

        /// <summary>
        /// Búsqueda global de archivos por nombre
        /// </summary>
        /// <remarks>
        /// Busca archivos (no carpetas) cuyo nombre contenga `q` (sin distinguir mayúsculas)
        /// en todos los canales indexados en base de datos y/o en el directorio local del servidor (LOCALDIR).
        ///
        /// **Parámetros:**
        /// - `q` (obligatorio, mínimo 2 caracteres): texto a buscar en el nombre del archivo
        /// - `filter`: `audio` (por defecto, solo archivos de audio) o `all`
        /// - `scope`: `all` (por defecto), `channels` o `local`
        /// - `limit`: máximo de resultados (por defecto 200, máximo 500). Se aplica por separado
        ///   al total de resultados de canales y al de archivos locales.
        ///
        /// **Respuesta:**
        /// - `channels`: resultados agrupados por canal (`channelId`, `channelName`, `files`), con el mismo
        ///   formato de archivo que GET /api/mobile/channels/{id}/files. Solo aparecen canales con resultados.
        /// - `local`: archivos locales con el mismo formato que GET /api/mobile/files/local.
        /// - `truncated`: `true` si había más resultados que `limit`.
        ///
        /// Si la base de datos de un canal falla, ese canal se omite y el resto de la búsqueda continúa.
        /// </remarks>
        /// <param name="q">Texto a buscar (mínimo 2 caracteres)</param>
        /// <param name="filter">audio | all</param>
        /// <param name="scope">all | channels | local</param>
        /// <param name="limit">Máximo de resultados por origen (1-500)</param>
        /// <param name="cancellationToken">Cancelación de la petición</param>
        /// <returns>Resultados agrupados por canal y archivos locales</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<GlobalSearchResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<GlobalSearchResultDto>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Search(
            [FromQuery] string? q,
            [FromQuery] string? filter = "audio",
            [FromQuery] string? scope = "all",
            [FromQuery] int limit = DefaultLimit,
            CancellationToken cancellationToken = default)
        {
            var query = q?.Trim() ?? string.Empty;
            if (query.Length < MinQueryLength)
            {
                return BadRequest(ApiResponse<GlobalSearchResultDto>.Fail(
                    $"Query 'q' is required and must have at least {MinQueryLength} characters"));
            }

            var filterLower = string.IsNullOrWhiteSpace(filter) ? "audio" : filter.Trim().ToLowerInvariant();
            if (filterLower != "audio" && filterLower != "all")
            {
                return BadRequest(ApiResponse<GlobalSearchResultDto>.Fail("Invalid filter: use 'audio' or 'all'"));
            }
            var audioOnly = filterLower == "audio";

            var scopeLower = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();
            if (scopeLower != "all" && scopeLower != "channels" && scopeLower != "local")
            {
                return BadRequest(ApiResponse<GlobalSearchResultDto>.Fail("Invalid scope: use 'all', 'channels' or 'local'"));
            }

            if (limit < 1) limit = 1;
            if (limit > MaxLimit) limit = MaxLimit;

            try
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var result = new GlobalSearchResultDto();

                if (scopeLower != "local")
                {
                    var (groups, truncated) = await SearchChannels(query, audioOnly, limit, baseUrl, cancellationToken);
                    result.Channels = groups;
                    result.Truncated |= truncated;
                }

                if (scopeLower != "channels")
                {
                    var (files, truncated) = SearchLocalFiles(FileService.LOCALDIR, query, audioOnly, limit, baseUrl, cancellationToken);
                    result.Local = files;
                    result.Truncated |= truncated;
                }

                return Ok(ApiResponse<GlobalSearchResultDto>.Ok(result));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499, ApiResponse<GlobalSearchResultDto>.Fail("Request cancelled"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running global search for {Query}", query);
                return StatusCode(500, ApiResponse<GlobalSearchResultDto>.Fail("Error searching files"));
            }
        }

        private async Task<(List<ChannelSearchGroupDto> Groups, bool Truncated)> SearchChannels(
            string query, bool audioOnly, int limit, string baseUrl, CancellationToken ct)
        {
            var dbNames = await _db.GetAllChannelDatabaseNames();
            var names = await GetChannelNames();
            var found = new ConcurrentBag<ChannelSearchGroupDto>();

            await Parallel.ForEachAsync(dbNames,
                new ParallelOptions { MaxDegreeOfParallelism = MaxChannelParallelism, CancellationToken = ct },
                async (dbName, token) =>
                {
                    if (!long.TryParse(dbName, out var channelId)) return;
                    try
                    {
                        var matches = await _db.Search(dbName, "", query);
                        var files = matches
                            .Where(m => m.IsFile)
                            .Select(m => ChannelFileDto.FromBsonFileManagerModel(m, channelId, baseUrl))
                            .Where(f => !audioOnly || f.Category == "Audio")
                            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (files.Count == 0) return;

                        found.Add(new ChannelSearchGroupDto
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

            return CapGroups(found, limit);
        }

        /// <summary>
        /// Orders channel groups by name and keeps at most <paramref name="limit"/> files in total,
        /// dropping groups left empty. Returns whether anything was cut.
        /// </summary>
        public static (List<ChannelSearchGroupDto> Groups, bool Truncated) CapGroups(
            IEnumerable<ChannelSearchGroupDto> groups, int limit)
        {
            var ordered = groups
                .OrderBy(g => g.ChannelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.ChannelId, StringComparer.Ordinal)
                .ToList();

            var result = new List<ChannelSearchGroupDto>();
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
                var chats = await _ts.getAllSavedChats();
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

        /// <summary>
        /// Walks <paramref name="root"/> recursively looking for files whose name contains
        /// <paramref name="query"/> (case-insensitive). Inaccessible directories are skipped and
        /// the walk stops as soon as more than <paramref name="limit"/> matches were seen.
        /// </summary>
        public static (List<FileItemDto> Files, bool Truncated) SearchLocalFiles(
            string root, string query, bool audioOnly, int limit, string baseUrl, CancellationToken ct = default)
        {
            var results = new List<FileItemDto>();
            var truncated = false;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return (results, false);

            var rootFull = Path.GetFullPath(root);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = MaxLocalDepth,
                MatchCasing = MatchCasing.CaseInsensitive,
                ReturnSpecialDirectories = false
            };

            try
            {
                foreach (var path in Directory.EnumerateFiles(rootFull, "*", options))
                {
                    ct.ThrowIfCancellationRequested();

                    var name = Path.GetFileName(path);
                    if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    FileItemDto dto;
                    try
                    {
                        var info = new FileInfo(path);
                        var relative = Path.GetRelativePath(rootFull, info.FullName).Replace("\\", "/");
                        dto = FileItemDto.FromLocalFile(info, relative, baseUrl);
                    }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    if (audioOnly && dto.Category != "Audio") continue;

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

            results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
            return (results, truncated);
        }
    }
}
