namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>
    /// Named HttpClient for the server-side calls the Blazor UI makes to this
    /// same server. The Syncfusion file manager runs its AJAX operations
    /// (<c>/api/file/FileOperations</c>, <c>GetImage</c>...) from the server,
    /// where the browser's session cookie is not available, so these requests
    /// carry the per-process secret <see cref="IWebAuthService.InternalToken"/>.
    /// </summary>
    public static class InternalHttpClient
    {
        public const string Name = "tfm-internal";

        public static IServiceCollection AddInternalHttpClient(this IServiceCollection services)
        {
            services.AddTransient<InternalTokenHandler>();
            services.AddHttpClient(Name).AddHttpMessageHandler<InternalTokenHandler>();
            return services;
        }
    }

    public class InternalTokenHandler : DelegatingHandler
    {
        private readonly IWebAuthService _auth;

        public InternalTokenHandler(IWebAuthService auth) => _auth = auth;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove(WebAuthService.InternalTokenHeader);
            request.Headers.TryAddWithoutValidation(WebAuthService.InternalTokenHeader, _auth.InternalToken);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
