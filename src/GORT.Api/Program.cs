using GORT.Agent;
using GORT.Api.Filters;
using GORT.Api.Authorization;
using GORT.Api.Grpc;
using GORT.Api.Middleware;
using GORT.Api.Services;
using GORT.Core;
using GORT.Modules.Agent;
using GORT.Modules.Backup;
using GORT.Modules.Backup.Services;
using GORT.Modules.Host;
using GORT.Modules.Network;
using GORT.Modules.Share;
using GORT.Modules.Share.Services;
using GORT.Modules.Storage;
using GORT.Modules.Update;
using GORT.Observability;
using GORT.Platform;
using GORT.Security;
using GORT.ServiceBus;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

if (!OperatingSystem.IsLinux())
{
    throw new PlatformNotSupportedException("GORT API only supports Linux.");
}

var builder = WebApplication.CreateBuilder(args);

#region Service Registration
builder.Services.AddGortCore();
builder.Services.AddPlatformServices();
builder.Services.AddGortSecurity(builder.Configuration);
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
// GORT runs only on Linux; synchronizes system users with smbpasswd so SMB clients can use the same credentials.
builder.Services.AddSingleton<ISystemUserProvisioner, SambaUserProvisioner>();
builder.Services.AddAgentServices();
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddHostedService<StartupOrchestrator>();
builder.Services.AddGrpc(options => options.Interceptors.Add<GrpcAuthorizationInterceptor>());
builder.Services.AddControllers(options => { options.Filters.Add<GortExceptionFilter>(); options.Filters.Add<CapabilityAuthorizationFilter>(); options.Conventions.Add(new CapabilityConvention()); })
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

#region Request Pipeline
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
app.MapGet("/metrics", async (HttpContext context, GORT.Observability.GortMetrics metrics) =>
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
