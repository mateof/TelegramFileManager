using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Mobile;
using TelegramDownloader.Services;
using System.Web;

namespace TelegramDownloader.Controllers.Mobile
{
    /// <summary>
    /// Streaming de audio desde Telegram y archivos locales. Soporta HTTP Range requests
    /// para seeking/salto en la reproducción. Los archivos se pueden precargar en caché
    /// para mejorar la velocidad de reproducción posterior.
    /// </summary>
    [Route("api/mobile/stream")]
    [ApiController]
    [Tags("Streaming")]
    public class MobileStreamController : ControllerBase
    {
        private readonly ITelegramService _ts;
        private readonly IDbService _db;
        private readonly IFileService _fs;
        private readonly TransactionInfoService _tis;
        private readonly IProgressiveDownloadService _progressiveDownload;
        private readonly ILogger<MobileStreamController> _logger;
        private static readonly SemaphoreSlim _downloadSemaphore = new(5); // Allow 5 concurrent downloads for preload

        public MobileStreamController(
            ITelegramService ts,
            IDbService db,
            IFileService fs,
            TransactionInfoService tis,
            IProgressiveDownloadService progressiveDownload,
            ILogger<MobileStreamController> logger)
        {
            _ts = ts;
            _db = db;
            _fs = fs;
            _tis = tis;
            _progressiveDownload = progressiveDownload;
            _logger = logger;
        }

        /// <summary>
        /// Transmitir archivo de audio desde Telegram (espera descarga completa)
        /// </summary>
        /// <remarks>
        /// Endpoint para reproducción de audio desde canales de Telegram usando MessageId.
        /// Soporta HTTP Range requests (RFC 7233) solo para archivos ya cacheados.
        ///
        /// **Funcionamiento:**
        /// 1. Si el archivo está en caché local, se sirve desde disco con soporte de seeking
        /// 2. Si no está en caché, se descarga COMPLETAMENTE de Telegram antes de enviarlo
        ///
        /// **Headers de respuesta:**
        /// - `Accept-Ranges: bytes` - Indica soporte para range requests (solo archivos cacheados)
        /// - `Content-Range: bytes start-end/total` - Para respuestas parciales (206)
        /// - `Content-Disposition: inline; filename="..."` - Nombre del archivo
        ///
        /// **Nota:** Para archivos no cacheados, este endpoint espera la descarga completa
        /// antes de enviar la respuesta. Esto garantiza la integridad del archivo.
        ///
        /// Use el endpoint `/preload/{channelId}/{fileId}` para precargar archivos en caché.
        /// </remarks>
        /// <param name="channelId">ID del canal de Telegram</param>
        /// <param name="fileId">ID del mensaje/archivo (MessageId)</param>
        /// <param name="fileName">Nombre del archivo para Content-Disposition (opcional)</param>
        /// <returns>Stream de audio (200 OK completo, 206 Partial Content para ranges en archivos cacheados)</returns>
        [HttpGet("audio/{channelId}/{fileId}")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StreamAudio(string channelId, string fileId, [FromQuery] string? fileName = null)
        {
            try
            {
                var message = await _ts.getMessageFile(channelId, int.Parse(fileId));
                if (message == null)
                {
                    return NotFound(ApiResponse<object>.Fail("File not found"));
                }

                var document = message.media as TL.MessageMediaDocument;
                var doc = document?.document as TL.Document;
                if (doc == null)
                {
                    return NotFound(ApiResponse<object>.Fail("Document not found"));
                }

                var fileSize = doc.size;
                var mimeType = doc.mime_type ?? "audio/mpeg";
                var name = fileName ?? doc.Filename ?? $"audio_{fileId}";

                // Check if file is cached locally - serve immediately with range support
                var cacheFileName = $"{channelId}-{fileId}-{name}";
                var tempPath = Path.Combine(FileService.TEMPDIR, "_temp");
                var cachedFilePath = Path.Combine(tempPath, cacheFileName);

                if (System.IO.File.Exists(cachedFilePath) && new FileInfo(cachedFilePath).Length >= fileSize)
                {
                    _logger.LogDebug("Serving cached file (zero-copy): {Path}", cachedFilePath);
                    return PhysicalFile(cachedFilePath, mimeType, name, enableRangeProcessing: true);
                }

                // File not cached - download completely before serving
                var semaphoreAcquired = await _downloadSemaphoreSequential.WaitAsync(TimeSpan.FromMinutes(10));
                if (!semaphoreAcquired)
                {
                    _logger.LogWarning("Download queue timeout for file {FileId}", fileId);
                    return StatusCode(503, ApiResponse<object>.Fail("Download queue is busy. Please try again later."));
                }

                try
                {
                    // Re-check cache after acquiring semaphore
                    if (System.IO.File.Exists(cachedFilePath) && new FileInfo(cachedFilePath).Length >= fileSize)
                    {
                        _logger.LogInformation("File was cached by another request: {FileName}", name);
                        return PhysicalFile(cachedFilePath, mimeType, name, enableRangeProcessing: true);
                    }

                    _logger.LogInformation("Downloading file from Telegram (waiting for complete): {FileName} ({Size} bytes)", name, fileSize);

                    // Ensure temp directory exists
                    if (!Directory.Exists(tempPath))
                    {
                        Directory.CreateDirectory(tempPath);
                    }

                    // Create file stream for download
                    using var file = new FileStream(cachedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

                    // Create download model for progress tracking
                    var dm = new DownloadModel
                    {
                        tis = _tis,
                        startDate = DateTime.Now,
                        path = cachedFilePath,
                        name = name,
                        _size = fileSize,
                        channelName = _ts.getChatName(Convert.ToInt64(channelId))
                    };
                    _tis.addToDownloadList(dm);

                    // Download file completely (blocks until complete)
                    var chatMessage = new ChatMessages { message = message };
                    await _ts.DownloadFileAndReturn(chatMessage, file, model: dm);

                    _logger.LogInformation("File download complete: {FileName} ({Size} bytes)", name, file.Length);
                }
                finally
                {
                    _downloadSemaphoreSequential.Release();
                }

                // Serve the downloaded file with PhysicalFile (supports range requests)
                return PhysicalFile(cachedFilePath, mimeType, name, enableRangeProcessing: true);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Audio stream cancelled by client");
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming audio {FileId} from channel {ChannelId}", fileId, channelId);
                return StatusCode(500, ApiResponse<object>.Fail("Error streaming audio"));
            }
        }

        /// <summary>
        /// Streaming progresivo de audio con caché del servidor (recomendado)
        /// </summary>
        /// <remarks>
        /// Endpoint principal para reproducción de audio. Implementa streaming progresivo que permite:
        /// - Reproducción inmediata sin esperar descarga completa
        /// - Seeking/salto en cualquier momento (soporta Range requests)
        /// - Descarga en background para caché del servidor
        /// - Servir desde caché local cuando esté disponible
        ///
        /// **Flujo de funcionamiento:**
        /// 1. Si el archivo está completamente en caché → sirve con EnableRangeProcessing=true
        /// 2. Si no está en caché:
        ///    - Inicia descarga en background si no está en progreso
        ///    - Para cada Range request:
        ///      - Si el rango está en caché local → sirve desde archivo
        ///      - Si no → descarga ese rango de Telegram y lo sirve
        ///
        /// **Soporta seeking completo:** El cliente puede saltar a cualquier posición.
        /// Si esa posición no está en caché, se descarga de Telegram directamente.
        ///
        /// **URL de ejemplo:**
        /// `/api/mobile/stream/tfm/{channelId}/{tfmId}?fileName=song.flac`
        /// </remarks>
        /// <param name="channelId">ID del canal de Telegram</param>
        /// <param name="tfmId">ID del archivo en la base de datos TFM (MongoDB ObjectId)</param>
        /// <param name="fileName">Nombre del archivo para Content-Disposition (opcional)</param>
        /// <returns>Stream de audio (200 OK completo, 206 Partial Content para ranges)</returns>
        [HttpGet("tfm/{channelId}/{tfmId}")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> StreamAudioByTfmId(string channelId, string tfmId, [FromQuery] string? fileName = null)
        {
            try
            {
                // Get file from database by TFM ID
                var dbFile = await _fs.getItemById(channelId, tfmId);
                if (dbFile == null)
                {
                    return NotFound(ApiResponse<object>.Fail("File not found in database"));
                }

                var name = fileName ?? dbFile.Name;
                var mimeType = FileService.getMimeType(dbFile.Type?.TrimStart('.') ?? "mp3");

                // Build cache file path
                var cacheFileName = $"{channelId}-{(dbFile.MessageId != null ? dbFile.MessageId.ToString() : dbFile.Id)}-{name}";
                var tempPath = Path.Combine(FileService.TEMPDIR, "_temp");
                var filePath = Path.Combine(tempPath, cacheFileName);

                // Ensure temp directory exists
                if (!Directory.Exists(tempPath))
                {
                    Directory.CreateDirectory(tempPath);
                }

                // Check if file is completely cached - serve directly with full Range support
                if (System.IO.File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length >= dbFile.Size)
                    {
                        _logger.LogDebug("Serving fully cached file: {FileName} ({Size} bytes)", name, fileInfo.Length);
                        return PhysicalFile(filePath, mimeType, name, enableRangeProcessing: true);
                    }
                }

                // Parse range header
                var rangeHeader = Request.Headers["Range"].ToString();
                long from = 0;
                long to = 0;
                bool hasRange = false;

                if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
                {
                    hasRange = true;
                    var range = rangeHeader.Replace("bytes=", "").Split('-');
                    from = long.Parse(range[0]);
                    if (range.Length > 1 && !string.IsNullOrEmpty(range[1]))
                        to = long.Parse(range[1]);
                }

                long totalLength = dbFile.Size;

                // Check how much is already cached
                long cachedBytes = 0;
                if (System.IO.File.Exists(filePath))
                {
                    cachedBytes = new FileInfo(filePath).Length;
                }

                // For initial request without Range, return first chunk as 206 with full size info
                // LibVLC will use Content-Range to know the total duration
                if (!hasRange)
                {
                    from = 0;
                    to = Math.Min(2 * 1024 * 1024, totalLength - 1); // First 2MB
                }

                // Handle Range request
                if (to == 0 || to >= totalLength)
                {
                    // Open-ended range
                    to = Math.Min(from + (5 * 524288), totalLength - 1); // ~2.5MB chunk
                }

                // Check if range is available locally
                if (from < cachedBytes)
                {
                    // Part or all of range is in cache
                    var availableEnd = Math.Min(to, cachedBytes - 1);
                    var length = availableEnd - from + 1;

                    _logger.LogDebug("Serving from cache: bytes {From}-{To} of {Total}", from, availableEnd, totalLength);

                    // Read from cache file
                    using var cacheStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    cacheStream.Seek(from, SeekOrigin.Begin);
                    var buffer = new byte[length];
                    var bytesRead = await cacheStream.ReadAsync(buffer, 0, (int)length);

                    Response.StatusCode = StatusCodes.Status206PartialContent;
                    Response.ContentType = mimeType;
                    Response.ContentLength = bytesRead;
                    Response.Headers["Content-Range"] = $"bytes {from}-{from + bytesRead - 1}/{totalLength}";
                    Response.Headers["Accept-Ranges"] = "bytes";
                    Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(name)}\"";

                    await Response.Body.WriteAsync(buffer, 0, bytesRead);
                    return new EmptyResult();
                }

                // Range not in cache - stream from Telegram
                _logger.LogDebug("Streaming from Telegram: bytes {From}-{To} of {Total}", from, to, totalLength);

                // Get message from Telegram
                if (!dbFile.MessageId.HasValue)
                {
                    return NotFound(ApiResponse<object>.Fail("File has no MessageId"));
                }

                var message = await _ts.getMessageFile(channelId, dbFile.MessageId.Value);
                if (message == null)
                {
                    return NotFound(ApiResponse<object>.Fail("Message not found in Telegram"));
                }

                // Align to 512KB boundaries for Telegram
                var alignedFrom = (from / 524288) * 524288;
                var alignedTo = ((to + 524288) / 524288) * 524288;
                if (alignedTo > totalLength) alignedTo = totalLength;

                var downloadLength = alignedTo - alignedFrom;

                // Download the range from Telegram
                byte[] data = await _ts.DownloadFileStream(message, alignedFrom, (int)downloadLength);

                // Calculate skip bytes to get to the exact requested position
                var skipBytes = from - alignedFrom;
                var responseLength = Math.Min(to - from + 1, data.Length - skipBytes);

                if (skipBytes < 0 || skipBytes >= data.Length)
                {
                    _logger.LogError("Invalid skip calculation: skipBytes={Skip}, dataLength={DataLen}", skipBytes, data.Length);
                    return StatusCode(500, "Error calculating response range");
                }

                Response.StatusCode = StatusCodes.Status206PartialContent;
                Response.ContentType = mimeType;
                Response.ContentLength = responseLength;
                Response.Headers["Content-Range"] = $"bytes {from}-{from + responseLength - 1}/{totalLength}";
                Response.Headers["Accept-Ranges"] = "bytes";
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(name)}\"";

                await Response.Body.WriteAsync(data, (int)skipBytes, (int)responseLength);
                return new EmptyResult();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Audio stream cancelled by client");
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming audio by TfmId {TfmId} from channel {ChannelId}", tfmId, channelId);
                return StatusCode(500, ApiResponse<object>.Fail("Error streaming audio"));
            }
        }

        /// <summary>
        /// Transmitir archivo de audio local
        /// </summary>
        /// <remarks>
        /// Reproduce archivos de audio almacenados en el servidor local (directorio LOCALDIR).
        /// Soporta HTTP Range requests para seeking, igual que el streaming de Telegram.
        ///
        /// **Seguridad:**
        /// La ruta se valida para asegurar que esté dentro del directorio LOCALDIR.
        /// Intentos de acceder fuera de este directorio (path traversal) serán rechazados con 400 Bad Request.
        ///
        /// **Uso:**
        /// El parámetro `path` debe ser la ruta relativa al archivo, como se obtiene
        /// en el campo `DirectUrl` de los tracks en playlists o en la navegación de archivos locales.
        ///
        /// Ejemplo: `/api/mobile/stream/local?path=music/album/song.mp3`
        /// </remarks>
        /// <param name="path">Ruta relativa del archivo dentro del directorio local</param>
        /// <returns>Stream de audio (200 OK completo, 206 Partial Content para ranges)</returns>
        [HttpGet("local")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult StreamLocalAudio([FromQuery] string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return BadRequest(ApiResponse<object>.Fail("Path is required"));
                }

                var basePath = FileService.LOCALDIR;
                var decodedPath = Uri.UnescapeDataString(path).TrimStart('/');
                var fullPath = Path.Combine(basePath, decodedPath);

                // Security check
                var resolvedPath = Path.GetFullPath(fullPath);
                if (!resolvedPath.StartsWith(Path.GetFullPath(basePath)))
                {
                    return BadRequest(ApiResponse<object>.Fail("Invalid path"));
                }

                if (!System.IO.File.Exists(resolvedPath))
                {
                    return NotFound(ApiResponse<object>.Fail("File not found"));
                }

                var fileInfo = new FileInfo(resolvedPath);
                var mimeType = GetMimeType(fileInfo.Extension);
                var name = fileInfo.Name;

                // Use PhysicalFile for kernel-level zero-copy transfer with range support
                return PhysicalFile(resolvedPath, mimeType, name, enableRangeProcessing: true);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Local audio stream cancelled by client");
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming local audio at path {Path}", path);
                return StatusCode(500, ApiResponse<object>.Fail("Error streaming audio"));
            }
        }

        /// <summary>
        /// Obtener metadatos de un archivo de audio
        /// </summary>
        /// <remarks>
        /// Retorna información detallada del archivo de audio incluyendo metadatos ID3/audio.
        ///
        /// **Información disponible:**
        /// - FileName: Nombre del archivo
        /// - FileSize: Tamaño en bytes
        /// - MimeType: Tipo MIME (ej: audio/mpeg)
        /// - Duration: Duración en segundos (si está disponible)
        /// - Title: Título de la pista (metadato ID3)
        /// - Artist: Artista/intérprete (metadato ID3)
        /// - SupportsStreaming: Indica si el archivo soporta streaming
        ///
        /// Útil para mostrar información del track antes de reproducirlo
        /// o para construir una interfaz de reproductor.
        /// </remarks>
        /// <param name="channelId">ID del canal de Telegram</param>
        /// <param name="fileId">ID del mensaje/archivo</param>
        /// <returns>Metadatos del archivo de audio</returns>
        [HttpGet("info/{channelId}/{fileId}")]
        [ProducesResponseType(typeof(ApiResponse<AudioInfoDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAudioInfo(string channelId, string fileId)
        {
            try
            {
                var message = await _ts.getMessageFile(channelId, int.Parse(fileId));
                if (message == null)
                {
                    return NotFound(ApiResponse<AudioInfoDto>.Fail("File not found"));
                }

                var document = message.media as TL.MessageMediaDocument;
                var doc = document?.document as TL.Document;
                if (doc == null)
                {
                    return NotFound(ApiResponse<AudioInfoDto>.Fail("Document not found"));
                }

                // Try to get audio attributes
                var audioAttr = doc.attributes?.OfType<TL.DocumentAttributeAudio>().FirstOrDefault();

                var info = new AudioInfoDto
                {
                    FileName = doc.Filename ?? $"audio_{fileId}",
                    FileSize = doc.size,
                    MimeType = doc.mime_type ?? "audio/mpeg",
                    Duration = audioAttr?.duration,
                    Title = audioAttr?.title,
                    Artist = audioAttr?.performer,
                    SupportsStreaming = true
                };

                return Ok(ApiResponse<AudioInfoDto>.Ok(info));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audio info {FileId} from channel {ChannelId}", fileId, channelId);
                return StatusCode(500, ApiResponse<AudioInfoDto>.Fail("Error retrieving audio info"));
            }
        }

        /// <summary>
        /// Precargar archivo de audio en caché para streaming más rápido
        /// </summary>
        /// <remarks>
        /// Inicia la descarga de un archivo de audio en segundo plano para almacenarlo en caché.
        /// Una vez en caché, el streaming será instantáneo sin latencia de Telegram.
        ///
        /// **Casos de uso:**
        /// - Precargar la siguiente canción mientras se reproduce la actual
        /// - Descargar toda una playlist mientras el usuario la navega
        /// - Mejorar experiencia en conexiones lentas
        ///
        /// **Respuesta:**
        /// - Si ya está en caché: `{ cached: true, path: "..." }`
        /// - Si inicia descarga: `{ cached: false, status: "preloading" }`
        ///
        /// La descarga se realiza en background y no bloquea la respuesta.
        /// Máximo 5 descargas simultáneas (semáforo interno).
        /// </remarks>
        /// <param name="channelId">ID del canal de Telegram</param>
        /// <param name="fileId">ID del mensaje/archivo a precargar</param>
        /// <returns>202 Accepted con estado de la precarga</returns>
        [HttpPost("preload/{channelId}/{fileId}")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PreloadAudio(string channelId, string fileId)
        {
            try
            {
                var message = await _ts.getMessageFile(channelId, int.Parse(fileId));
                if (message == null)
                {
                    return NotFound(ApiResponse<object>.Fail("File not found"));
                }

                var document = message.media as TL.MessageMediaDocument;
                var doc = document?.document as TL.Document;
                if (doc == null)
                {
                    return NotFound(ApiResponse<object>.Fail("Document not found"));
                }

                var name = doc.Filename ?? $"audio_{fileId}";
                var tempPath = Path.Combine(FileService.TEMPDIR, "_temp", $"{channelId}-{fileId}-{name}");

                // Check if already cached
                if (System.IO.File.Exists(tempPath) && new FileInfo(tempPath).Length == doc.size)
                {
                    return Accepted(ApiResponse<object>.Ok(new { cached = true, path = tempPath }, "File already cached"));
                }

                // Start preload in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _downloadSemaphore.WaitAsync();
                        try
                        {
                            var chatMessage = new ChatMessages { message = message };
                            await _ts.DownloadFile(chatMessage, name, Path.GetDirectoryName(tempPath));
                            _logger.LogInformation("Preloaded audio file {FileId} to cache", fileId);
                        }
                        finally
                        {
                            _downloadSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error preloading audio {FileId}", fileId);
                    }
                });

                return Accepted(ApiResponse<object>.Ok(new { cached = false, status = "preloading" }, "Preload started"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting preload for {FileId} from channel {ChannelId}", fileId, channelId);
                return StatusCode(500, ApiResponse<object>.Fail("Error starting preload"));
            }
        }

        /// <summary>
        /// Descargar archivo de audio completo antes de enviarlo (para descargas offline)
        /// </summary>
        /// <remarks>
        /// Este endpoint está diseñado para descargas de playlists offline. A diferencia del
        /// streaming normal, este endpoint:
        ///
        /// 1. **Descarga completa:** Espera a que el archivo se descargue completamente de Telegram
        ///    antes de enviar la respuesta. Esto garantiza que el archivo esté íntegro.
        ///
        /// 2. **Verificación de caché:** Antes de descargar, verifica si el archivo ya existe
        ///    en la carpeta temporal con el tamaño correcto.
        ///
        /// 3. **Semáforo de descarga:** Solo permite UNA descarga simultánea para evitar
        ///    saturar la conexión con Telegram y mejorar la velocidad individual.
        ///
        /// 4. **Caché automático:** El archivo se guarda en caché, por lo que futuras
        ///    solicitudes del mismo archivo serán instantáneas.
        ///
        /// **Casos de uso:**
        /// - Descarga de playlists para modo offline
        /// - Descarga de archivos grandes donde el streaming podría fallar
        /// - Cuando se necesita garantizar la integridad del archivo
        ///
        /// **Nota:** Este endpoint puede tardar varios segundos/minutos dependiendo del
        /// tamaño del archivo y la velocidad de conexión con Telegram.
        /// </remarks>
        /// <param name="channelId">ID del canal de Telegram</param>
        /// <param name="tfmId">ID del archivo en la base de datos TFM (MongoDB ObjectId)</param>
        /// <param name="fileName">Nombre del archivo para Content-Disposition (opcional)</param>
        /// <returns>Archivo de audio completo (200 OK)</returns>
        [HttpGet("download/{channelId}/{tfmId}")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DownloadAudioComplete(string channelId, string tfmId, [FromQuery] string? fileName = null)
        {
            try
            {
                // Get file from database first (before semaphore to check cache quickly)
                var dbFile = await _fs.getItemById(channelId, tfmId);
                if (dbFile == null)
                {
                    return NotFound(ApiResponse<object>.Fail("File not found in database"));
                }

                var name = fileName ?? dbFile.Name;
                var mimeType = FileService.getMimeType(dbFile.Type?.TrimStart('.') ?? "mp3");

                // Build cache file path - same format as StreamAudioByTfmId
                var cacheFileName = $"{channelId}-{(dbFile.MessageId != null ? dbFile.MessageId.ToString() : dbFile.Id)}-{name}";
                var tempPath = Path.Combine(FileService.TEMPDIR, "_temp");
                var filePath = Path.Combine(tempPath, cacheFileName);

                // Check if file exists in cache and is complete (check both size from DB and actual file)
                FileStream? file = _fs.ExistFileIntempFolder(cacheFileName);
                var isFileComplete = file != null && file.Length >= dbFile.Size;

                // Also check if this file was already fully downloaded via TransactionInfoService
                var isFileDownloaded = _tis.isFileDownloaded(filePath);

                // If file exists and is complete (either by size check or TransactionInfoService)
                if (file != null && (isFileComplete || isFileDownloaded))
                {
                    // File is already cached and complete - use PhysicalFile for zero-copy transfer
                    var fileSize = file.Length;
                    _logger.LogInformation("Serving cached file (zero-copy): {FileName} ({Size} bytes)", name, fileSize);

                    // Close the file handle so PhysicalFile can open it
                    var physicalPath = ((FileStream)file).Name;
                    await file.DisposeAsync();

                    // Use PhysicalFile for kernel-level zero-copy transfer (sendfile syscall)
                    // This is the fastest way to serve static files in ASP.NET Core
                    return PhysicalFile(physicalPath, mimeType, name, enableRangeProcessing: false);
                }

                // File not in cache or incomplete - need to download from Telegram
                file?.Dispose();
                file = null;

                // Semaphore for sequential downloads (only 1 at a time)
                var semaphoreAcquired = await _downloadSemaphoreSequential.WaitAsync(TimeSpan.FromMinutes(10));
                if (!semaphoreAcquired)
                {
                    _logger.LogWarning("Download queue timeout for file {TfmId}", tfmId);
                    return StatusCode(503, ApiResponse<object>.Fail("Download queue is busy. Please try again later."));
                }

                try
                {
                    // Re-check cache after acquiring semaphore (another request might have downloaded it)
                    file = _fs.ExistFileIntempFolder(cacheFileName);
                    isFileComplete = file != null && file.Length >= dbFile.Size;

                    if (isFileComplete)
                    {
                        _logger.LogInformation("File was cached by another request: {FileName}", name);
                        file!.Position = 0;

                        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Uri.EscapeDataString(name)}\"";
                        Response.Headers["Content-Length"] = file.Length.ToString();
                        Response.Headers["X-Cache-Status"] = "HIT-AFTER-WAIT";

                        return new FileStreamResult(file, mimeType)
                        {
                            FileDownloadName = name,
                            EnableRangeProcessing = false
                        };
                    }

                    file?.Dispose();
                    file = null;

                    _logger.LogInformation("Downloading file from Telegram: {FileName} ({Size} bytes)", name, dbFile.Size);

                    // Ensure temp directory exists
                    if (!Directory.Exists(tempPath))
                    {
                        Directory.CreateDirectory(tempPath);
                    }

                    // Get message from Telegram
                    if (!dbFile.MessageId.HasValue)
                    {
                        return NotFound(ApiResponse<object>.Fail("File has no MessageId"));
                    }

                    var message = await _ts.getMessageFile(channelId, dbFile.MessageId.Value);
                    if (message == null)
                    {
                        return NotFound(ApiResponse<object>.Fail("Message not found in Telegram"));
                    }

                    // Create file stream for download
                    file = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

                    // Create download model for progress tracking
                    var dm = new DownloadModel
                    {
                        tis = _tis,
                        startDate = DateTime.Now,
                        path = filePath,
                        name = name,
                        _size = dbFile.Size,
                        channelName = _ts.getChatName(Convert.ToInt64(channelId))
                    };
                    _tis.addToDownloadList(dm);

                    // Download file completely (blocks until complete)
                    var chatMessage = new ChatMessages { message = message };
                    await _ts.DownloadFileAndReturn(chatMessage, file, model: dm);

                    // Reset stream position
                    file.Position = 0;

                    _logger.LogInformation("File download complete from Telegram: {FileName}", name);

                    // Set headers
                    Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Uri.EscapeDataString(name)}\"";
                    Response.Headers["Content-Length"] = file.Length.ToString();
                    Response.Headers["X-Cache-Status"] = "MISS";

                    return new FileStreamResult(file, mimeType)
                    {
                        FileDownloadName = name,
                        EnableRangeProcessing = false
                    };
                }
                finally
                {
                    _downloadSemaphoreSequential.Release();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Download cancelled by client for file {TfmId}", tfmId);
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file {TfmId} from channel {ChannelId}", tfmId, channelId);
                return StatusCode(500, ApiResponse<object>.Fail($"Error downloading file: {ex.Message}"));
            }
        }

        // Allow 3 concurrent downloads for better playlist download throughput
        // (balance between speed and not overwhelming Telegram API)
        private static readonly SemaphoreSlim _downloadSemaphoreSequential = new(3);

        private static string GetMimeType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".mp3" => "audio/mpeg",
                ".ogg" => "audio/ogg",
                ".flac" => "audio/flac",
                ".aac" => "audio/aac",
                ".wav" => "audio/wav",
                ".m4a" => "audio/mp4",
                ".wma" => "audio/x-ms-wma",
                ".opus" => "audio/opus",
                _ => "application/octet-stream"
            };
        }
    }
}
