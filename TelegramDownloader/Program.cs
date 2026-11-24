using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using MongoDB.Bson;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Syncfusion.Blazor;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Helpers;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TelegramDownloader.Services.GitHub;
using TL;

// Register Guid type mapper for MongoDB to handle Guid types in log properties (used by Serilog sink)
BsonTypeMapper.RegisterCustomTypeMapper(typeof(Guid), new GuidTypeMapper());

var extensionProvider = new FileExtensionContentTypeProvider();
foreach(var mime in FileService.MIMETypesDictionary)
{
    extensionProvider.Mappings.Add(mime);
}
// /app/manifest.webmanifest -> application/manifest+json (installable web app)
TelegramDownloader.Services.Api.SpaHosting.AddContentTypes(extensionProvider);

GeneralConfigStatic.loadDbConfig();

if (GeneralConfigStatic.tlconfig?.avoid_checking_certificate != null || Environment.GetEnvironmentVariable("avoid_checking_certificate") != null)
    if (GeneralConfigStatic.tlconfig?.avoid_checking_certificate ?? Convert.ToBoolean(Environment.GetEnvironmentVariable("avoid_checking_certificate")))
        ServicePointManager.ServerCertificateValidationCallback +=
            (sender, certificate, chain, errors) => {
                return true;
            };


var builder = WebApplication.CreateBuilder(args);
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("MzgzMTc5MUAzMjM5MmUzMDJlMzAzYjMyMzkzYmw1ZDhSMmlTeTVTMFl0Ky9YaHBpZlRhd0NZS2RuQlVvenpnRVduRHFtZkE9");
builder.Services.AddSyncfusionBlazor();

if (!Directory.Exists(FileService.IMGDIR))
{
    Directory.CreateDirectory(FileService.IMGDIR);
}

if (!Directory.Exists(UserService.USERDATAFOLDER))
{
    Directory.CreateDirectory(UserService.USERDATAFOLDER);
}

// Data Protection keys (antiforgery, cookies) must survive container restarts:
// with the default in-container location every restart minted a new key and
// browsers kept failing with "antiforgery token could not be decrypted".
var dataProtectionKeys = Path.Combine(UserService.USERDATAFOLDER, "keys");
Directory.CreateDirectory(dataProtectionKeys);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeys))
    .SetApplicationName("TelegramFileManager");

if (!Directory.Exists(FileService.LOCALDIR))
{
    Directory.CreateDirectory(FileService.LOCALDIR);
}

// Configure Serilog
var mongoConnectionString = GeneralConfigStatic.tlconfig?.mongo_connection_string
    ?? Environment.GetEnvironmentVariable("connectionString");

// Enable Serilog self-logging for debugging
SelfLog.Enable(msg => Console.WriteLine($"[Serilog] {msg}"));

// Get application version from assembly
var appVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown";

// Check if MongoDB is configured and accessible
bool mongoAvailable = false;
MongoDB.Driver.IMongoDatabase? logsDatabase = null;

if (!string.IsNullOrWhiteSpace(mongoConnectionString))
{
    try
    {
        var settings = MongoDB.Driver.MongoClientSettings.FromConnectionString(mongoConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
        settings.ConnectTimeout = TimeSpan.FromSeconds(3);
        var mongoClient = new MongoDB.Driver.MongoClient(settings);
        logsDatabase = mongoClient.GetDatabase("TFM_Logs");

        // Test connection
        var command = new MongoDB.Bson.BsonDocument("ping", 1);
        logsDatabase.RunCommand<MongoDB.Bson.BsonDocument>(command);
        mongoAvailable = true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] MongoDB not available: {ex.Message}");
        mongoAvailable = false;
    }
}

// Configure Serilog - with MongoDB if available, console only otherwise
var logConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("AppVersion", appVersion)
    .WriteTo.Console(
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}");

if (mongoAvailable && logsDatabase != null)
{
    logConfig.WriteTo.MongoDBBson(cfg =>
    {
        cfg.SetMongoDatabase(logsDatabase);
        cfg.SetCollectionName("logs");
    });
}

Log.Logger = logConfig.CreateLogger();

builder.Host.UseSerilog();

// Log application startup
if (mongoAvailable)
{
    Log.Information("TelegramFileManager starting up. MongoDB logs database: TFM_Logs");
}
else
{
    Log.Warning("TelegramFileManager starting up. MongoDB not configured or not available - setup required.");
}


// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = true; // Enable detailed errors for debugging
    // A phone that comes back after a few minutes should find its circuit
    // still there instead of having to reload the page (default: 3 minutes)
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
})
.AddCircuitOptions(options =>
{
    options.DetailedErrors = true;
});
builder.Services.AddSingleton<ITelegramService, TelegramService>();
builder.Services.AddSingleton<IDbService, DbService>();
builder.Services.AddTransient<IFileService, FileServiceV2>();
builder.Services.AddSingleton<TransactionInfoService>();
builder.Services.AddSingleton<GHService>();

// Progressive download service for streaming with background caching
builder.Services.AddSingleton<IProgressiveDownloadService, ProgressiveDownloadService>();
builder.Services.AddSingleton<TelegramDownloader.Services.WebDavLockManager>();

// Task persistence services
builder.Services.AddSingleton<ITaskPersistenceService, TaskPersistenceService>();
// Single code path for resuming persisted transfers (startup and the UI/API).
builder.Services.AddSingleton<TelegramDownloader.Services.Transfers.TaskResumeCoordinator>();
// Waits for TelegramService.WhenSessionReady, so registration order does not matter.
builder.Services.AddHostedService<TaskResumeService>();

// Restore a previously authorized Telegram session on startup so the API and
// WebDAV work after a restart without opening the web UI first.
builder.Services.AddHostedService<TelegramSessionRestoreService>();

// Log query service - only if MongoDB is available
builder.Services.AddSingleton<ILogQueryService>(sp =>
    new LogQueryService(mongoConnectionString ?? "mongodb://localhost:27017", sp.GetRequiredService<ILogger<LogQueryService>>()));

// System metrics service
builder.Services.AddSingleton<ISystemMetricsService, SystemMetricsService>();

// Setup service for initial configuration
builder.Services.AddSingleton<ISetupService, SetupService>();

#pragma warning disable ASP0000 // ServiceLocator pattern is intentional
ServiceLocator.ServiceProvider = builder.Services.BuildServiceProvider();
#pragma warning restore ASP0000
builder.Services.AddBlazorBootstrap();

// Add controllers for Mobile API and the modular v1 API
builder.Services.AddControllers();

// Web authentication: optional web password (cookie session), LAN bypass and
// the single access policy enforced by WebAccessMiddleware.
builder.Services.AddSingleton<TelegramDownloader.Services.WebAuth.IWebAuthService>(sp =>
    new TelegramDownloader.Services.WebAuth.WebAuthService(
        sp.GetRequiredService<IDbService>(),
        sp.GetRequiredService<ILogger<TelegramDownloader.Services.WebAuth.WebAuthService>>()));
TelegramDownloader.Services.WebAuth.WebAuthSession.AddWebAuthentication(builder.Services);
TelegramDownloader.Services.WebAuth.InternalHttpClient.AddInternalHttpClient(builder.Services);

// Modular v1 API: SignalR transfer hub + its supporting services
builder.Services.AddSignalR();
builder.Services.AddSingleton<TelegramDownloader.Services.Api.QrLoginSessionManager>();
builder.Services.AddScoped<TelegramDownloader.Services.Api.ChannelFolderResolver>();
builder.Services.AddHostedService<TelegramDownloader.Services.Api.TransferBroadcastService>();

// Application events hub (/hubs/events): notifications, refresh/import progress, session state, metrics
builder.Services.AddSingleton<TelegramDownloader.Services.Api.ImportProgressTracker>();
builder.Services.AddSingleton<TelegramDownloader.Hubs.EventsHubState>();
builder.Services.AddHostedService<TelegramDownloader.Services.Api.EventsBroadcastService>();

// Media library: identification through metadata providers, cached images, watch state
builder.Services.AddHttpClient("library-provider", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("library-images", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<TelegramDownloader.Data.db.ILibraryDbService, TelegramDownloader.Data.db.LibraryDbService>();
builder.Services.AddSingleton<TelegramDownloader.Services.Library.MetadataProviderRegistry>();
builder.Services.AddSingleton<TelegramDownloader.Services.Library.IMetadataProviderSource>(sp => sp.GetRequiredService<TelegramDownloader.Services.Library.MetadataProviderRegistry>());
builder.Services.AddSingleton<TelegramDownloader.Services.Library.LibraryImageService>();
builder.Services.AddSingleton<TelegramDownloader.Services.Library.LibraryScanService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramDownloader.Services.Library.LibraryScanService>());
builder.Services.AddSingleton<TelegramDownloader.Services.Library.LibraryService>();
builder.Services.AddSingleton(sp => new TelegramDownloader.Services.Mobile.ChannelSearchIndex(
    sp.GetRequiredService<IDbService>(),
    sp.GetRequiredService<ILogger<TelegramDownloader.Services.Mobile.ChannelSearchIndex>>()));

// CORS for PWA and mobile apps
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobilePolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Length", "Content-Range", "Accept-Ranges");
    });
});

// Swagger/OpenAPI for Mobile API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TelegramFileManager Mobile API",
        Version = "v1",
        Description = "REST API for the mobile audio player application. Provides access to playlists, Telegram channels, file navigation and audio streaming.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "TFM"
        }
    });

    c.SwaggerDoc("api-v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "TelegramFileManager API v1",
        Version = "1.0",
        Description =
            "Modular REST API exposing the full feature set of the web application: Telegram authentication, " +
            "channel management, remote and local file management, transfers (downloads/uploads) with live " +
            "progress over SignalR, playlists, sharing, configuration and system diagnostics.\n\n" +
            "Live transfer progress is streamed over the SignalR hub at /hubs/transfers.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "TFM"
        }
    });

    // Route each controller to its document. FileController and the other legacy
    // controllers use Syncfusion types that break schema generation, so they are
    // excluded from both documents.
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        var route = apiDesc.RelativePath ?? string.Empty;
        var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"];

        if (docName == "api-v1")
            return route.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase);

        return controllerName?.StartsWith("Mobile") == true;
    });

    // Surface the XML doc comments written on the controllers and DTOs.
    var xmlPath = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    // API Key authentication
    c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "API Key for mobile app authentication. Add to request header as: X-Api-Key: your-api-key",
        Name = "X-Api-Key",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "ApiKey"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});




//builder.Services.AddTransient<HttpClient>();
////builder.Services.AddServerSideBlazor()
////    .AddHubOptions(options => options.MaximumReceiveMessageSize = 1024 * 1024 * 1024);
//builder.Services.AddServerSideBlazor().AddHubOptions(options =>
//{
//    //options.SupportedProtocols = new List<string>();
//    //options.SupportedProtocols.Add("json");
//    // options.MaximumParallelInvocationsPerClient = 10;
//    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
//});

var app = builder.Build();

// Record the socket peer address and the proxy headers as they arrived, before
// UseForwardedHeaders rewrites/removes them: the LAN bypass of the web password
// must decide on the real TCP peer, never on a client-supplied header.
app.UseMiddleware<TelegramDownloader.Middleware.ConnectionInfoCaptureMiddleware>();

// Configure forwarded headers for reverse proxy support (HTTPS via nginx, traefik, etc.)
// Only proxies listed in TrustedProxies (IPs/CIDRs; appsettings/env
// "TrustedProxies" or config.json "trusted_proxies"; default: loopback and
// private ranges) may rewrite the client address, scheme and host. Previously
// every peer was trusted, so any client on the internet could forge
// X-Forwarded-For (spoofing the IP in logs and in the login rate limiter) or
// X-Forwarded-Proto/Host. Reverse proxies in Docker networks or on the LAN keep
// working with the default.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
{
    var invalidProxies = new List<string>();
    var trustedProxyList = TelegramDownloader.Services.WebAuth.NetworkRules.ReadTrustedProxyList(
        app.Configuration, GeneralConfigStatic.tlconfig?.trusted_proxies);
    var (trustedProxies, trustedNetworks) = TelegramDownloader.Services.WebAuth.NetworkRules.ParseTrustedProxies(trustedProxyList, invalidProxies);
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    foreach (var proxy in trustedProxies) forwardedHeadersOptions.KnownProxies.Add(proxy);
    foreach (var network in trustedNetworks) forwardedHeadersOptions.KnownIPNetworks.Add(network);
    foreach (var bad in invalidProxies) Log.Warning("TrustedProxies: ignoring invalid entry {Entry}", bad);
    Log.Information("Trusted reverse proxies: {Proxies}", string.Join(", ", trustedProxyList));
}
app.UseForwardedHeaders(forwardedHeadersOptions);

// Load config from DB only if MongoDB is available
if (mongoAvailable)
{
    try
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDbService>();
            await GeneralConfigStatic.Load(db);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not load configuration from database - will use defaults");
    }
}

// Web password / LAN bypass (MongoDB, with a local copy so it fails closed
// when the database is unreachable).
await app.Services.GetRequiredService<TelegramDownloader.Services.WebAuth.IWebAuthService>().LoadAsync(mongoAvailable);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

var wrProvider = new PhysicalFileProvider(FileService.LOCALDIR);
var imgProvider = new PhysicalFileProvider(Path.Combine(Environment.CurrentDirectory, "wwwroot", "img", "telegram"));

// combine multiple file providers to serve files from
var compositeProvider = new CompositeFileProvider(wrProvider);
var compositeProviderImg = new CompositeFileProvider(imgProvider);

// app.UseHttpsRedirection();

// CORS first so preflights are answered and 401s carry the CORS headers.
app.UseCors("MobilePolicy");

// Access gate (replaces the former ApiKeyMiddleware): web session cookie, API
// key or LAN bypass, see WebAccessPolicy. It runs before every static file
// provider because /local/** (user files) and /img/telegram/** are protected
// too; wwwroot assets and the /app shell are classified as public.
app.UseAuthentication();
app.UseMiddleware<TelegramDownloader.Middleware.WebAccessMiddleware>();
// "Default web interface" setting: a browser opening / goes to /app/ when it
// is "app" (escape hatch /?ui=classic, remembered in the tfm_ui cookie).
app.UseMiddleware<TelegramDownloader.Middleware.DefaultWebUiMiddleware>();

// New web app (React SPA) at /app: hashed assets are cached for a year,
// index.html and the service worker are always revalidated.
var spaRoot = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "app");
Directory.CreateDirectory(spaRoot);
// Builds without the web app (dotnet run without building ClientApp) get an
// explanatory page instead of a 404. The real build replaces it; keeping it
// out of wwwroot/app means building never leaves the repo dirty.
var spaIndex = Path.Combine(spaRoot, "index.html");
var spaPlaceholder = Path.Combine(app.Environment.ContentRootPath, "Resources", "app-not-built.html");
if (!File.Exists(spaIndex) && File.Exists(spaPlaceholder))
{
    File.Copy(spaPlaceholder, spaIndex);
}
var spaStaticOptions = new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(spaRoot),
    RequestPath = "/app",
    ContentTypeProvider = extensionProvider,
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.CacheControl = TelegramDownloader.Services.Api.SpaCachePolicy.For(ctx.Context.Request.Path.Value)
};
app.UseStaticFiles(spaStaticOptions);
// A file of the build that does not exist (hashed asset of an older deploy,
// sw.js / manifest.webmanifest / icons while the app is not built) is a real
// 404: the /app/{**path} fallback below must not answer it with index.html.
app.Use((context, next) =>
{
    if (TelegramDownloader.Services.Api.SpaHosting.IsMissingAppFile(context.Request.Path.Value))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.Headers.CacheControl = TelegramDownloader.Services.Api.SpaCachePolicy.NoCache;
        return Task.CompletedTask;
    }
    return next(context);
});

app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = compositeProvider,
    RequestPath = "/local",
    ContentTypeProvider = extensionProvider,
    ServeUnknownFileTypes = true,

});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = compositeProviderImg,
    RequestPath = "/img/telegram",

});
//app.UseStaticFiles(new StaticFileOptions
//{
//    FileProvider = new PhysicalFileProvider(
//           Path.Combine(builder.Environment.ContentRootPath, "localData")),
//    RequestPath = "/localData"
//});
//app.UseStaticFiles(new StaticFileOptions
//{
//    FileProvider = new PhysicalFileProvider(
//           Path.Combine(builder.Environment.ContentRootPath, "workingDir")),
//    RequestPath = "/workingDir"
//});

// Swagger UI - available in all environments for mobile app development
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/api-v1/swagger.json", "TFM API v1 (full)");
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "TFM Mobile API (audio player)");
    c.RoutePrefix = "api-docs";
});

app.UseRouting();
app.MapControllers();

// Live transfer progress for API clients (mobile apps, dashboards...)
app.MapHub<TelegramDownloader.Hubs.TransferHub>("/hubs/transfers");

// Application events (notifications, refresh/import progress, session state, metrics)
app.MapHub<TelegramDownloader.Hubs.EventsHub>("/hubs/events");

// Behind a proxy that does not forward WebSocket upgrades, SignalR falls back
// to long polling; keep each poll below the usual 60 s proxy read timeout so
// the fallback works instead of dropping the circuit every minute.
app.MapBlazorHub(options => options.LongPolling.PollTimeout = TimeSpan.FromSeconds(45));
// SPA deep links (dots included, e.g. /app/files/song.mp3) get index.html;
// the literal "app" segment makes this fallback win over the Blazor one.
// Missing build files (assets, sw.js, manifest, icons) were already answered
// with a 404 right after the /app static files (SpaHosting.IsMissingAppFile).
app.MapFallbackToFile("/app/{**path}", "index.html", new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(spaRoot),
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = TelegramDownloader.Services.Api.SpaCachePolicy.NoCache
});
app.MapFallbackToPage("/_Host");

try
{
    // Check if running in Docker
    var isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
                   || File.Exists("/.dockerenv");

    // Start the application
    await app.StartAsync();

    // Get the URLs the application is listening on
    var urls = app.Urls;
    var serverAddresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
        .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses;

    var listeningUrls = serverAddresses?.ToList() ?? urls.ToList();

    // Display startup banner with URLs
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║           TelegramFileManager - Application Started          ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
    foreach (var url in listeningUrls)
    {
        var paddedUrl = url.PadRight(46);
        Console.WriteLine($"║  🌐 Listening on: {paddedUrl}║");
    }
    Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
    if (isDocker)
    {
        Console.WriteLine("║  🐳 Running in Docker container                              ║");
    }
    else
    {
        Console.WriteLine("║  💻 Running on local machine                                 ║");
    }
    Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    Console.WriteLine();

    Log.Information("TelegramFileManager application started. Listening on: {Urls}", string.Join(", ", listeningUrls));

    // Open browser automatically if not in Docker and not disabled in config
    var shouldOpenBrowser = GeneralConfigStatic.tlconfig?.open_browser_on_startup ?? true;
    if (!isDocker && shouldOpenBrowser && listeningUrls.Any())
    {
        var urlToOpen = listeningUrls.FirstOrDefault(u => u.StartsWith("http://")) ?? listeningUrls.First();

        // Replace 0.0.0.0 or * with localhost for browser
        urlToOpen = urlToOpen.Replace("://0.0.0.0", "://localhost")
                            .Replace("://[::]", "://localhost")
                            .Replace("://*", "://localhost");

        Log.Information("Opening browser at: {Url}", urlToOpen);
        Console.WriteLine($"🚀 Opening browser at: {urlToOpen}");

        try
        {
            OpenBrowser(urlToOpen);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open browser automatically. Please navigate to {Url} manually.", urlToOpen);
            Console.WriteLine($"⚠️  Could not open browser automatically. Please navigate to {urlToOpen} manually.");
        }
    }

    // Wait for the application to stop
    await app.WaitForShutdownAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.Information("TelegramFileManager application shutting down");
    Log.CloseAndFlush();
}

// Helper method to open browser cross-platform
static void OpenBrowser(string url)
{
    try
    {
        Process.Start(url);
    }
    catch
    {
        // Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            url = url.Replace("&", "^&");
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
        // Linux
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        // macOS
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
        else
        {
            throw;
        }
    }
}
