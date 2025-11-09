using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TelegramDownloader.Data;

using TelegramDownloader.Data.db;
using TelegramDownloader.Services;

using Microsoft.Extensions.FileProviders;
using Syncfusion.Blazor;
using TL;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using TelegramDownloader.Models;
using TelegramDownloader.Services.GitHub;
using System.Net.Http;
using System.Net;

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

builder.Logging.AddLog4Net("log4net.config");


// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<ITelegramService, TelegramService>();
builder.Services.AddSingleton<IDbService, DbService>();
builder.Services.AddTransient<IFileService, FileServiceV2>();
builder.Services.AddSingleton<TransactionInfoService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<GHService>();

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

app.Run();
