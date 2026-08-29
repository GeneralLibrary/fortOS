using FortOS.Agent;
using FortOS.Api.Filters;
using FortOS.Api.Authorization;
using FortOS.Api.Grpc;
using FortOS.Api.Middleware;
using FortOS.Api.Services;
using FortOS.Core;
using FortOS.Modules.Agent;
using FortOS.Modules.Backup;
using FortOS.Modules.Backup.Services;
using FortOS.Modules.Host;
using FortOS.Modules.Network;
using FortOS.Modules.Share;
using FortOS.Modules.Share.Services;
using FortOS.Modules.Storage;
using FortOS.Modules.Update;
using FortOS.Observability;
using FortOS.Platform;
using FortOS.Security;
using FortOS.ServiceBus;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Serialization;

if (!OperatingSystem.IsLinux())
{
    throw new PlatformNotSupportedException("FortOS API only supports Linux.");
}

var builder = WebApplication.CreateBuilder(args);

// Runtime configuration overrides (api_config table) are wired into the IConfiguration read chain:
// Must be registered after appsettings so overrides take precedence over the static configuration files.
builder.Configuration.Add<FortOS.Core.Configuration.SqliteConfigurationSource>(null);

#region Service Registration
builder.Services.AddFortOSCore();
builder.Services.AddFortOSPlatform();
builder.Services.AddFortOSSecurity(builder.Configuration);
builder.Services.AddFortOSServiceBus();
builder.Services.AddFortOSModuleHost();
builder.Services.AddSingleton<StorageModule>();
builder.Services.AddSingleton<ShareModule>();
builder.Services.AddSingleton<NetworkModule>();
builder.Services.AddSingleton<AgentModule>();
builder.Services.AddSingleton<BackupModule>();
builder.Services.AddSingleton<UpdateModule>();
builder.Services.AddSingleton<INasModule>(sp => sp.GetRequiredService<StorageModule>());
builder.Services.AddSingleton<INasModule>(sp => sp.GetRequiredService<ShareModule>());
builder.Services.AddSingleton<INasModule>(sp => sp.GetRequiredService<NetworkModule>());
builder.Services.AddSingleton<INasModule>(sp => sp.GetRequiredService<AgentModule>());
builder.Services.AddSingleton<INasModule>(sp => sp.GetRequiredService<BackupModule>());
builder.Services.AddSingleton<INasModule>(sp => sp.GetRequiredService<UpdateModule>());
builder.Services.AddSingleton<FilePathResolver>();
builder.Services.AddSingleton<RecycleBinService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<UploadSessionService>();
builder.Services.AddSingleton<BackupRunHistoryStore>();
builder.Services.AddSingleton<BackupExecutionService>();
builder.Services.AddHttpClient<AiAssistantService>();
builder.Services.AddSingleton<RemoteAccessService>();
// FortOS runs only on Linux; synchronizes system users with smbpasswd so SMB clients can use the same credentials.
builder.Services.AddSingleton<ISystemUserProvisioner, SambaUserProvisioner>();
builder.Services.AddFortOSAgent();
builder.Services.AddFortOSObservability(builder.Configuration);
builder.Services.AddHostedService<StartupOrchestrator>();
builder.Services.AddGrpc(options => options.Interceptors.Add<GrpcAuthorizationInterceptor>());
builder.Services.AddControllers(options => { options.Filters.Add<FortOSExceptionFilter>(); options.Filters.Add<CapabilityAuthorizationFilter>(); options.Conventions.Add(new CapabilityConvention()); })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// CORS allowlist: local-only origins by default; deployments explicitly open it up via cors:allowed_origins (comma-separated).
// No more AllowAnyOrigin — the NAS token travels in the Authorization header (not a cookie), so cross-site scripts
// cannot carry it directly, but tightening remains defense in depth to prevent deployments with require_auth=false from letting any web page call the management API.
var allowedOrigins = (builder.Configuration.GetValue<string>("cors:allowed_origins") ?? "http://localhost:5000,http://127.0.0.1:5000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
#endregion

var app = builder.Build();

#region Request Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ApiVersionCompatibilityMiddleware>();
app.UseRouting();
app.UseCors();
app.UseMiddleware<NasTokenMiddleware>();
app.UseMiddleware<AuditMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<HttpMetricsMiddleware>();

if (app.Configuration.GetValue("dashboard:enabled", false))
{
    // Serve the SPA from wwwroot/dashboard/. Note: do NOT set a RequestPath here —
    // StaticFileOptions.RequestPath strips the prefix before mapping to the web root,
    // so /dashboard/index.html would be looked up as wwwroot/index.html and 404.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    // Friendly entry point: visiting the host root lands on the dashboard.
    // Note: do not register a separate redirect for /dashboard — UseDefaultFiles responds to directory requests
    // (without a trailing slash) with an automatic 302 to /dashboard/; if MapGet("/dashboard") also intercepts,
    // /dashboard/ would hit that endpoint and redirect back to itself, forming an infinite loop.
    app.MapGet("/", () => Results.Redirect("/dashboard/"));
}

app.MapControllers();
app.MapGet("/metrics", async (HttpContext context, FortOS.Observability.FortOSMetrics metrics) =>
{
    if (!app.Configuration.GetValue("metrics:allow_anonymous", false) && context.Items["NasTokenPayload"] is null)
    {
        await ApiProblem.WriteAsync(context, StatusCodes.Status401Unauthorized, "TOKEN_MISSING", "Authentication is required.").ConfigureAwait(false);
        return;
    }
    context.Response.ContentType = "text/plain; version=0.0.4";
    await context.Response.WriteAsync(metrics.ExportPrometheus(), context.RequestAborted).ConfigureAwait(false);
});
app.MapGrpcService<StorageGrpcService>();
app.MapGrpcService<ShareGrpcService>();
app.MapGrpcService<AgentGrpcService>();
app.MapGrpcService<ServiceBusGrpcService>();
app.MapGrpcService<AuditGrpcService>();
EndpointCapabilityValidation.Validate(app);
#endregion

app.Run();

/// <summary>WebApplicationFactory test entry point marker.</summary>
public partial class Program { }
