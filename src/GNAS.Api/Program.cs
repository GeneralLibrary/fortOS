using GNAS.Agent;
using GNAS.Api.Filters;
using GNAS.Api.Authorization;
using GNAS.Api.Grpc;
using GNAS.Api.Middleware;
using GNAS.Api.Services;
using GNAS.Core;
using GNAS.Modules.Agent;
using GNAS.Modules.Backup;
using GNAS.Modules.Backup.Services;
using GNAS.Modules.Host;
using GNAS.Modules.Network;
using GNAS.Modules.Share;
using GNAS.Modules.Share.Services;
using GNAS.Modules.Storage;
using GNAS.Modules.Update;
using GNAS.Observability;
using GNAS.Platform;
using GNAS.Security;
using GNAS.ServiceBus;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

if (!OperatingSystem.IsLinux())
{
    throw new PlatformNotSupportedException("GNAS API 仅支持 Linux。");
}

var builder = WebApplication.CreateBuilder(args);

#region 服务注册
builder.Services.AddGnasCore();
builder.Services.AddPlatformServices();
builder.Services.AddGnasSecurity(builder.Configuration);
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
// GNAS 仅运行于 Linux；同步供给系统用户与 smbpasswd，使 SMB 客户端可使用同一套凭据。
builder.Services.AddSingleton<ISystemUserProvisioner, SambaUserProvisioner>();
builder.Services.AddAgentServices();
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddHostedService<StartupOrchestrator>();
builder.Services.AddGrpc(options => options.Interceptors.Add<GrpcAuthorizationInterceptor>());
builder.Services.AddControllers(options => { options.Filters.Add<GnasExceptionFilter>(); options.Filters.Add<CapabilityAuthorizationFilter>(); options.Conventions.Add(new CapabilityConvention()); })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
#endregion

var app = builder.Build();

#region 请求管线
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ApiVersionCompatibilityMiddleware>();
app.UseRouting();
app.UseMiddleware<NasTokenMiddleware>();
app.UseMiddleware<AuditMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<HttpMetricsMiddleware>();
app.UseCors();

if (app.Configuration.GetValue("dashboard:enabled", false))
{
    app.UseDefaultFiles(new DefaultFilesOptions { RequestPath = "/dashboard" });
    app.UseStaticFiles(new StaticFileOptions { RequestPath = "/dashboard" });
}

app.MapControllers();
app.MapGet("/metrics", async (HttpContext context, GNAS.Observability.GnasMetrics metrics) =>
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

/// <summary>WebApplicationFactory 测试入口标记。</summary>
public partial class Program { }
