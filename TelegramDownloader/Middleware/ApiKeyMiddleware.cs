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

        public async Task InvokeAsync(HttpContext context)
        {
            // Only check API key for mobile API endpoints
            if (context.Request.Path.StartsWithSegments("/api/mobile"))
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

                if (string.IsNullOrEmpty(providedApiKey))
                {
                    _logger.LogWarning("Mobile API request without API key from {IP}",
                        context.Connection.RemoteIpAddress);

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "API key required",
                        message = $"Please provide your API key in the {API_KEY_HEADER} header or apiKey query parameter"
                    });
                    return;
                }

                // Validate API key
                if (!configuredApiKey.Equals(providedApiKey, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Invalid mobile API key attempt from {IP}",
                        context.Connection.RemoteIpAddress);

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "Invalid API key"
                    });
                    return;
                }

                _logger.LogDebug("Valid mobile API key from {IP}", context.Connection.RemoteIpAddress);
            }

            await _next(context);
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
