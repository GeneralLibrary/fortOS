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

// 运行时配置覆盖（api_config 表）接入 IConfiguration 读取链：
// 必须在 appsettings 之后注册，使覆盖值优先于静态配置文件生效。
builder.Configuration.Add<FortOS.Core.Configuration.SqliteConfigurationSource>(null);

#region Service Registration
builder.Services.AddFortOSCore();
builder.Services.AddPlatformServices();
builder.Services.AddFortOSSecurity(builder.Configuration);
builder.Services.AddServiceBus();
builder.Services.AddModuleHost();
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
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<BackupRunHistoryStore>();
builder.Services.AddSingleton<BackupExecutionService>();
// FortOS runs only on Linux; synchronizes system users with smbpasswd so SMB clients can use the same credentials.
builder.Services.AddSingleton<ISystemUserProvisioner, SambaUserProvisioner>();
builder.Services.AddAgentServices();
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddHostedService<StartupOrchestrator>();
builder.Services.AddGrpc(options => options.Interceptors.Add<GrpcAuthorizationInterceptor>());
builder.Services.AddControllers(options => { options.Filters.Add<FortOSExceptionFilter>(); options.Filters.Add<CapabilityAuthorizationFilter>(); options.Conventions.Add(new CapabilityConvention()); })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// CORS 白名单：默认仅本地来源；部署时用 cors:allowed_origins（逗号分隔）显式放开。
// 不再 AllowAnyOrigin —— NAS token 走 Authorization 头（非 cookie），跨站脚本无法
// 直接携带，但收紧仍是纵深防御，防止 require_auth=false 的部署被任意网页调用管理 API。
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
app.UseCors();

if (app.Configuration.GetValue("dashboard:enabled", false))
{
    // Serve the SPA from wwwroot/dashboard/. Note: do NOT set a RequestPath here —
    // StaticFileOptions.RequestPath strips the prefix before mapping to the web root,
    // so /dashboard/index.html would be looked up as wwwroot/index.html and 404.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    // Friendly entry point: visiting the host root lands on the dashboard.
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
