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
