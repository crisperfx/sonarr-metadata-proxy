using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Serilog;
using Serilog.Events;
using Sonarr.MetadataProxy.Mapping;
using Sonarr.MetadataProxy.Options;
using Sonarr.MetadataProxy.Passthrough;
using Sonarr.MetadataProxy.Providers;
using Sonarr.MetadataProxy.Reverse;
using Sonarr.MetadataProxy.Services;
using Sonarr.MetadataProxy.Tls;
using Sonarr.MetadataProxy.Translation;

var builder = WebApplication.CreateBuilder(args);

var logLevel = ParseLogLevel(builder.Configuration["LOG_LEVEL"]);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(logLevel)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(dispose: false);

var options = ProxyOptions.FromConfiguration(builder.Configuration);

Log.Information("Starting Sonarr Metadata Proxy.");
Log.Information("Metadata source: {Source}", options.MetadataSource);
Log.Information("TMDB credentials configured: {Configured}", options.HasTmdbAuth);
Log.Information("TVDB fallback enabled: {Enabled}", options.EnableTvdbFallback);
Log.Information("Management HTTP port: {Port}", options.Port);
Log.Information("TLS interception enabled: {Tls}", !options.SkipTls);
Log.Information("Data directory: {DataDir}", options.DataDir);
Log.Information("TVDB fallback backend: {SkyhookUrl} (resolved via {Resolver})", options.SkyhookBaseUrl, options.SkyhookResolverUrl);

builder.Services.AddSingleton(options);

var loggerFactory = LoggerFactory.Create(logging => logging.AddSerilog(dispose: false));
var certificateProvider = new CertificateProvider(options, loggerFactory.CreateLogger<CertificateProvider>());
builder.Services.AddSingleton(certificateProvider);

builder.Services.AddSingleton<MappingStore>();
builder.Services.AddSingleton<WikidataTvdbResolver>();
builder.Services.AddSingleton<ITvdbToTmdbResolver, TvdbToTmdbResolver>();
builder.Services.AddSingleton<ITmdbApi, TmdbClient>();
builder.Services.AddSingleton<TmdbMetadataProvider>();
builder.Services.AddSingleton<IMetadataProvider>(
    serviceProvider => MetadataProviderRegistry.Create(options.MetadataSource, serviceProvider));
builder.Services.AddSingleton<RuntimeDnsResolver>();
builder.Services.AddSingleton<ISkyHookPassthrough, SkyHookPassthrough>();
builder.Services.AddSingleton<SkyHookTranslator>();
builder.Services.AddSingleton<MetadataRequestHandler>();

builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
{
    jsonOptions.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    jsonOptions.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddControllers()
    .AddJsonOptions(jsonOptions =>
    {
        jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        jsonOptions.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddCors(cors =>
{
    cors.AddPolicy(Sonarr.MetadataProxy.Controllers.OverridesController.CorsPolicyName, policy =>
    {
        if (options.CorsAllowedOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else if (options.CorsAllowedOrigins.Count > 0)
        {
            policy.WithOrigins(options.CorsAllowedOrigins.ToArray()).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

if (options.CorsAllowedOrigins.Count > 0)
{
    Log.Information("CORS for override UI enabled for origins: {Origins}.", string.Join(", ", options.CorsAllowedOrigins));
}
else
{
    Log.Information("CORS not configured (CORS_ALLOWED_ORIGINS empty); same-origin requests to /api/overrides still work.");
}

builder.WebHost.ConfigureKestrel(kestrel =>
{
    if (!options.SkipTls)
    {
        kestrel.ListenAnyIP(443, listen =>
        {
            listen.UseHttps(certificateProvider.GetOrCreateServerCertificate());
        });
        Log.Information("Listening for intercepted skyhook.sonarr.tv traffic on https://0.0.0.0:443.");
    }

    kestrel.ListenAnyIP(options.Port);
    Log.Information("Listening for management/health traffic on http://0.0.0.0:{Port}.", options.Port);
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", source = options.MetadataSource }));
app.MapGet("/info", () => Results.Ok(new
{
    status = "ok",
    source = options.MetadataSource,
    tmdbConfigured = options.HasTmdbAuth,
    tvdbFallback = options.EnableTvdbFallback,
    version = "1.0.0"
}));
app.MapGet("/", () => Results.Text(
    "<!doctype html><html><head><meta charset=\"utf-8\"><title>Sonarr Metadata Proxy</title></head>" +
    "<body style=\"font-family:system-ui,sans-serif;max-width:640px;margin:40px auto;padding:0 16px;color:#0f172a\">" +
    "<h1>Sonarr Metadata Proxy</h1><p>This endpoint serves the Sonarr metadata replacement. " +
    "There is nothing to see here.</p><ul>" +
    "<li><a href=\"/health\">/health</a></li>" +
    "<li><a href=\"/info\">/info</a></li>" +
    "<li><a href=\"/api/overrides\">/api/overrides</a></li>" +
    "</ul></body></html>",
    "text/html; charset=utf-8"));

app.UseCors(Sonarr.MetadataProxy.Controllers.OverridesController.CorsPolicyName);

app.MapControllers();
app.Run();

static LogEventLevel ParseLogLevel(string? level)
{
    return Enum.TryParse<LogEventLevel>(level ?? "Information", true, out var parsed) ? parsed : LogEventLevel.Information;
}

public partial class Program
{
}