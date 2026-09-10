using System.Net;
using System.Text.Json;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Shared HTTP plumbing for JSON metadata providers: a polite request rate,
    /// retries on 429/5xx and provider errors surfaced as <see cref="ProviderException"/>.
    /// </summary>
    public abstract class HttpMetadataProviderBase : IMetadataProvider
    {
        protected static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _nextAllowed = DateTime.MinValue;

        protected readonly HttpClient Http;
        protected readonly ILogger Logger;
        protected readonly string ApiKey;

        protected HttpMetadataProviderBase(HttpClient http, string apiKey, ILogger logger)
        {
            Http = http;
            ApiKey = apiKey;
            Logger = logger;
        }

        public abstract string Id { get; }
        public abstract ProviderDescriptor Descriptor { get; }
        /// <summary>Minimum gap between two requests to this provider.</summary>
        protected virtual TimeSpan MinInterval => TimeSpan.FromMilliseconds(250);

        public abstract Task<IReadOnlyList<ProviderCandidate>> SearchAsync(string kind, string query, int? year, CancellationToken ct);
        public abstract Task<LibraryItem?> GetItemAsync(string kind, string providerId, CancellationToken ct);
        public abstract Task<IReadOnlyList<LibraryEpisode>> GetSeasonAsync(string providerId, int season, CancellationToken ct);
        public abstract Task<ProviderCandidate?> FindByImdbAsync(string imdbId, string? kind, CancellationToken ct);
        public abstract Task EnrichAsync(LibraryItem item, CancellationToken ct);

        protected async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct, bool nullOn404 = true)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await ThrottleAsync(ct);
                HttpResponseMessage response;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    Authorize(request);
                    response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == 3) throw new ProviderException($"{Descriptor.Name} is not reachable: {ex.Message}", null, ex);
                    await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
                    continue;
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound && nullOn404)
                        return null;
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        throw new ProviderException($"{Descriptor.Name} rejected the API key", (int)response.StatusCode);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * (attempt + 1));
                        Logger.LogWarning("{Provider} answered {Status}; retrying in {Wait}s", Id, (int)response.StatusCode, wait.TotalSeconds);
                        if (attempt == 3) throw new ProviderException($"{Descriptor.Name} keeps answering {(int)response.StatusCode}", (int)response.StatusCode);
                        await Task.Delay(wait, ct);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                        throw new ProviderException($"{Descriptor.Name} answered {(int)response.StatusCode}", (int)response.StatusCode);

                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }
            }
            return null;
        }

        protected virtual void Authorize(HttpRequestMessage request) { }

        private async Task ThrottleAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                var now = DateTime.UtcNow;
                if (now < _nextAllowed)
                    await Task.Delay(_nextAllowed - now, ct);
                _nextAllowed = DateTime.UtcNow + MinInterval;
            }
            finally
            {
                _gate.Release();
            }
        }

        protected static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        protected static int? Int(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
            return null;
        }

        protected static double? Dbl(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
            return null;
        }

        protected static int? YearOf(string? date)
        {
            if (string.IsNullOrWhiteSpace(date) || date.Length < 4) return null;
            return int.TryParse(date.AsSpan(0, 4), out var y) ? y : null;
        }

        protected static DateTime? DateOf(string? date) =>
            DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

        protected static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
