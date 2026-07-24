using GNAS.Agent;
using GNAS.Api.Filters;
using GNAS.Api.Grpc;
using GNAS.Api.Middleware;
using GNAS.Api.Services;
using GNAS.Core;
using GNAS.Modules.Agent;
using GNAS.Modules.Backup;
using GNAS.Modules.Host;
using GNAS.Modules.Network;
using GNAS.Modules.Share;
using GNAS.Modules.Storage;
using GNAS.Modules.Update;
using GNAS.Observability;
using GNAS.Platform;
using GNAS.Security;
using GNAS.ServiceBus;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

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
builder.Services.AddAgentServices();
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddHostedService<StartupOrchestrator>();
builder.Services.AddGrpc();
builder.Services.AddControllers(options => options.Filters.Add<GnasExceptionFilter>())
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
app.UseMiddleware<NasTokenMiddleware>();
app.UseMiddleware<AuditMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseRouting();
app.UseCors();

if (app.Configuration.GetValue("dashboard:enabled", false))
{
    app.UseDefaultFiles(new DefaultFilesOptions { RequestPath = "/dashboard" });
    app.UseStaticFiles(new StaticFileOptions { RequestPath = "/dashboard" });
}

app.MapControllers();
app.MapGrpcService<StorageGrpcService>();
app.MapGrpcService<ShareGrpcService>();
app.MapGrpcService<AgentGrpcService>();
app.MapGrpcService<ServiceBusGrpcService>();
app.MapGrpcService<AuditGrpcService>();
#endregion

app.Run();

/// <summary>WebApplicationFactory 测试入口标记。</summary>
public partial class Program { }
