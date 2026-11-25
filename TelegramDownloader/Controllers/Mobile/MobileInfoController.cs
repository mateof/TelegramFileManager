using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Models.Mobile;

namespace TelegramDownloader.Controllers.Mobile
{
    /// <summary>
    /// Información del servidor para clientes móviles: versión y funcionalidades opcionales
    /// disponibles, para que el cliente active o desactive opciones según el servidor.
    /// </summary>
    [Route("api/mobile")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Info")]
    public class MobileInfoController : ControllerBase
    {
        /// <summary>Versión del contrato de la API móvil.</summary>
        public const int ApiVersion = 1;

        /// <summary>Identificadores de las funcionalidades opcionales soportadas por este servidor.</summary>
        public static readonly IReadOnlyList<string> Features = new[]
        {
            "playlist.bulkAdd",      // POST /api/mobile/playlists/{id}/tracks/bulk
            "search.global",         // GET  /api/mobile/search
            "channels.refresh",      // POST /api/v1/channels/{id}/refresh
            "channels.refreshProgress", // GET  /api/v1/channels/{id}/refresh/progress, DELETE /api/v1/channels/{id}/refresh
            "stream.localTranscode", // GET  /api/mobile/stream/local/transcoded
            "events.hub",            // SignalR /hubs/events
            "search.v1",             // GET  /api/v1/search
            "files.thumbnails",      // GET  /api/v1/channels/{id}/messages/{messageId}/thumbnail
            "files.content",         // GET  /api/v1/channels/{id}/files/{fileId}/content|stream
            "files.preload",         // POST /api/v1/channels/{id}/files/preload
            "local.copyMove",        // POST /api/v1/local/copy, /api/v1/local/move
            "shares.browse",         // collectionId on /api/v1/channels/{id}/files, .tfm upload on /api/v1/shares/import
            "setup.write",           // POST /api/v1/system/setup...
            "transfers.remove",      // DELETE /api/v1/transfers/{id}
            "system.admin",          // restart, image-cache, orphaned-favorites, ffmpeg, version
            "webAuth.passkeys",      // /api/v1/web-auth/passkeys/** (passkey sign-in of the web app)
            "files.deleteOperations", // POST /api/v1/channels/{id}/files/delete-operations, /api/v1/local/delete-operations, /api/v1/delete-operations
            "messages.content",      // GET  /api/v1/channels/{id}/messages/{messageId}/content (play a message attachment without indexing it)
            "transfers.pauseUploads", // POST /api/v1/transfers/{id}/pause and /retry on an upload; canPause / canRetry on every transfer
        };

        private static readonly string ServerVersion = ResolveServerVersion();

        /// <summary>
        /// Obtener capacidades del servidor
        /// </summary>
        /// <remarks>
        /// Devuelve la versión del contrato de la API móvil (`apiVersion`), la versión del servidor
        /// (`serverVersion`) y la lista de funcionalidades opcionales soportadas (`features`).
        ///
        /// **Funcionalidades actuales:**
        /// - `playlist.bulkAdd`: POST /api/mobile/playlists/{id}/tracks/bulk
        /// - `search.global`: GET /api/mobile/search
        /// - `channels.refresh`: POST /api/v1/channels/{id}/refresh
        /// - `channels.refreshProgress`: GET /api/v1/channels/{id}/refresh/progress y DELETE /api/v1/channels/{id}/refresh (cancelar)
        /// - `stream.localTranscode`: GET /api/mobile/stream/local/transcoded
        /// - `events.hub`: hub SignalR /hubs/events (notificaciones, refrescos, importaciones, sesión, métricas)
        /// - `search.v1`: GET /api/v1/search
        /// - `files.thumbnails`: GET /api/v1/channels/{id}/messages/{messageId}/thumbnail
        /// - `files.content`: GET /api/v1/channels/{id}/files/{fileId}/content y .../stream
        /// - `files.preload`: POST /api/v1/channels/{id}/files/preload
        /// - `local.copyMove`: POST /api/v1/local/copy y /api/v1/local/move
        /// - `shares.browse`: `collectionId` en /api/v1/channels/{id}/files e importación de `.tfm` en POST /api/v1/shares/import
        /// - `setup.write`: POST /api/v1/system/setup, /test-mongo, /test-telegram, /complete
        /// - `transfers.remove`: DELETE /api/v1/transfers/{id}
        /// - `system.admin`: /api/v1/system/restart, image-cache, orphaned-favorites, ffmpeg y version
        /// - `webAuth.passkeys`: /api/v1/web-auth/passkeys/** (inicio de sesión con passkeys de la web)
        /// - `files.deleteOperations`: borrados en segundo plano con progreso y cancelación: POST /api/v1/channels/{id}/files/delete-operations,
        ///   POST /api/v1/local/delete-operations, GET /api/v1/delete-operations[/{id}], POST /api/v1/delete-operations/{id}/cancel y `DeleteProgress` en /hubs/events
        ///
        /// Los servidores antiguos no tienen este endpoint: un **404** debe interpretarse
        /// como "sin funcionalidades opcionales". No accede a base de datos.
        /// </remarks>
        /// <returns>Versión y funcionalidades del servidor</returns>
        [HttpGet("capabilities")]
        [ProducesResponseType(typeof(ApiResponse<CapabilitiesDto>), StatusCodes.Status200OK)]
        public IActionResult GetCapabilities()
        {
            var dto = new CapabilitiesDto
            {
                ApiVersion = ApiVersion,
                ServerVersion = ServerVersion,
                Features = Features.ToList()
            };
            return Ok(ApiResponse<CapabilitiesDto>.Ok(dto));
        }

        private static string ResolveServerVersion()
        {
            var assembly = typeof(Program).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Drop SourceLink build metadata ("+<commit sha>")
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }
}
