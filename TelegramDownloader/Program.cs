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
})
.AddCircuitOptions(options =>
{
    options.DetailedErrors = true;
});
builder.Services.AddSingleton<ITelegramService, TelegramService>();
builder.Services.AddSingleton<IDbService, DbService>();
builder.Services.AddTransient<IFileService, FileServiceV2>();
builder.Services.AddSingleton<TransactionInfoService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<GHService>();

// Progressive download service for streaming with background caching
builder.Services.AddSingleton<IProgressiveDownloadService, ProgressiveDownloadService>();

// Task persistence services
builder.Services.AddSingleton<ITaskPersistenceService, TaskPersistenceService>();
builder.Services.AddHostedService<TaskResumeService>();

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

// Modular v1 API: SignalR transfer hub + its supporting services
builder.Services.AddSignalR();
builder.Services.AddSingleton<TelegramDownloader.Services.Api.QrLoginSessionManager>();
builder.Services.AddScoped<TelegramDownloader.Services.Api.ChannelFolderResolver>();
builder.Services.AddHostedService<TelegramDownloader.Services.Api.TransferBroadcastService>();

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

// Configure forwarded headers for reverse proxy support (HTTPS via nginx, traefik, etc.)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
// Clear default limits to accept forwarded headers from any proxy
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
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

// CORS must be before routing
app.UseCors("MobilePolicy");

// API Key authentication middleware for mobile endpoints
app.UseMiddleware<TelegramDownloader.Middleware.ApiKeyMiddleware>();

app.UseRouting();
app.MapControllers();

// Live transfer progress for API clients (mobile apps, dashboards...)
app.MapHub<TelegramDownloader.Hubs.TransferHub>("/hubs/transfers");

app.MapBlazorHub();
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
