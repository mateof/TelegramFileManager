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
using System.Net;
using System.Net.Http;
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
    ?? Environment.GetEnvironmentVariable("connectionString")
    ?? "mongodb://localhost:27017";

// Enable Serilog self-logging for debugging
SelfLog.Enable(msg => Console.WriteLine($"[Serilog] {msg}"));

// Get application version from assembly
var appVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown";

// Create MongoDB client and get logs database
var mongoClient = new MongoDB.Driver.MongoClient(mongoConnectionString);
var logsDatabase = mongoClient.GetDatabase("TFM_Logs");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("AppVersion", appVersion)
    .WriteTo.Console(
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
    .WriteTo.MongoDBBson(cfg =>
    {
        cfg.SetMongoDatabase(logsDatabase);
        cfg.SetCollectionName("logs");
    })
    .CreateLogger();

builder.Host.UseSerilog();

// Log application startup
Log.Information("TelegramFileManager starting up. MongoDB logs database: TFM_Logs");


// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<ITelegramService, TelegramService>();
builder.Services.AddSingleton<IDbService, DbService>();
builder.Services.AddTransient<IFileService, FileServiceV2>();
builder.Services.AddSingleton<TransactionInfoService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<GHService>();

// Task persistence services
builder.Services.AddSingleton<ITaskPersistenceService, TaskPersistenceService>();
builder.Services.AddHostedService<TaskResumeService>();

// Log query service
builder.Services.AddSingleton<ILogQueryService>(sp =>
    new LogQueryService(mongoConnectionString, sp.GetRequiredService<ILogger<LogQueryService>>()));

ServiceLocator.ServiceProvider = builder.Services.BuildServiceProvider();
builder.Services.AddBlazorBootstrap();




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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbService>();
    await GeneralConfigStatic.Load(db);
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

app.UseRouting();
app.MapControllers();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

try
{
    Log.Information("TelegramFileManager application started");
    app.Run();
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
