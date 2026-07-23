using TelegramDownloader.Models;

namespace TelegramDownloader.Middleware
{
    /// <summary>
    /// Middleware for validating API key on mobile API endpoints
    /// </summary>
    public class ApiKeyMiddleware
    {
        private const string API_KEY_HEADER = "X-Api-Key";
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyMiddleware> _logger;

        public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Path prefixes protected by the API key: the legacy mobile API, the
        /// modular v1 API and the SignalR hubs it exposes.
        /// </summary>
        private static readonly string[] PROTECTED_PREFIXES =
        {
            "/api/mobile",
            "/api/v1",
            "/hubs"
        };

        public async Task InvokeAsync(HttpContext context)
        {
            if (PROTECTED_PREFIXES.Any(p => context.Request.Path.StartsWithSegments(p)))
            {
                var configuredApiKey = GeneralConfigStatic.tlconfig?.mobile_api_key;

                // If no API key is configured, allow all requests (development mode)
                if (string.IsNullOrEmpty(configuredApiKey))
                {
                    _logger.LogDebug("Mobile API key not configured - allowing request without authentication");
                    await _next(context);
                    return;
                }

                // Check for API key in header first, then query string
                // Query string is needed for MediaElement which cannot send custom headers
                string? providedApiKey = null;

                if (context.Request.Headers.TryGetValue(API_KEY_HEADER, out var headerApiKey))
                {
                    providedApiKey = headerApiKey;
                }
                else if (context.Request.Query.TryGetValue("apiKey", out var queryApiKey))
                {
                    providedApiKey = queryApiKey;
                }
                else if (context.Request.Query.TryGetValue("access_token", out var accessToken))
                {
                    // SignalR clients cannot set custom headers on the WebSocket
                    // handshake, so they pass the key through the standard
                    // access_token query parameter.
                    providedApiKey = accessToken;
                }
                else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
                {
                    // The SignalR JS/.NET clients send the access token as a
                    // Bearer header on the negotiate request (only the socket
                    // itself falls back to the query string).
                    var value = authHeader.ToString();
                    if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        providedApiKey = value["Bearer ".Length..].Trim();
                }

                if (string.IsNullOrEmpty(providedApiKey))
                {
                    _logger.LogWarning("API request without API key from {IP}",
                        context.Connection.RemoteIpAddress);

                    await WriteUnauthorized(context, "API key required",
                        $"Provide your API key in the {API_KEY_HEADER} header, or in the apiKey/access_token query parameter");
                    return;
                }

                // Validate API key
                if (!configuredApiKey.Equals(providedApiKey, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Invalid API key attempt from {IP}",
                        context.Connection.RemoteIpAddress);

                    await WriteUnauthorized(context, "Invalid API key", null);
                    return;
                }

                _logger.LogDebug("Valid mobile API key from {IP}", context.Connection.RemoteIpAddress);
            }

            await _next(context);
        }

        /// <summary>
        /// Writes the 401 body. The v1 API and the hubs use the v1 envelope
        /// (<c>error</c> is an object with a machine-readable <c>code</c>);
        /// <c>/api/mobile</c> keeps its original flat shape so the existing
        /// audio app is not broken.
        /// </summary>
        private static async Task WriteUnauthorized(HttpContext context, string error, string? detail)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            if (context.Request.Path.StartsWithSegments("/api/mobile"))
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error,
                    message = detail
                });
                return;
            }

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = new
                {
                    code = Models.Api.ApiErrorCodes.Unauthorized,
                    message = error,
                    detail
                }
            });
        }
    }

    /// <summary>
    /// Extension methods for ApiKeyMiddleware
    /// </summary>
    public static class ApiKeyMiddlewareExtensions
    {
        public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiKeyMiddleware>();
        }
    }
}
