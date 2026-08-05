using System.Text.Json;
using FortOS.Agent.Infrastructure;
using FortOS.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortOS.Agent.Broker;

/// <summary>
/// Publishes events and automatically renews Agent tokens before they expire.
/// </summary>
public sealed class TokenRenewalService : BackgroundService
{
    private readonly AgentTokenRegistry _registry;
    private readonly ITokenBroker _broker;
    private readonly IEventBus _eventBus;
    private readonly IServiceSupervisor _supervisor;
    private readonly ILogger<TokenRenewalService>? _logger;

    /// <summary>
    /// Initialize the token renewal background service.
    /// </summary>
    public TokenRenewalService(AgentTokenRegistry registry, ITokenBroker broker, IEventBus eventBus, IServiceSupervisor supervisor, ILogger<TokenRenewalService>? logger = null)
    {
        _registry = registry;
        _broker = broker;
        _eventBus = eventBus;
        _supervisor = supervisor;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RenewExpiringTokensAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RenewExpiringTokensAsync(CancellationToken ct)
    {
        // Drop already-expired entries first so the renewal loop below never keeps
        // retrying tokens that can no longer be renewed.
        _registry.PruneExpired();

        var threshold = DateTimeOffset.UtcNow.AddHours(1);
        foreach (var state in _registry.Snapshot().Where(s => s.ExpiresAt <= threshold))
        {
            try
            {
                await _eventBus.PublishAsync($"agent.{state.AgentId}.token.expiring", "agent.token.expiring", JsonSerializer.Serialize(new { state.AgentId, state.ExpiresAt }), ct).ConfigureAwait(false);
                var renewed = await _broker.RenewAgentTokenAsync(state.AgentId, state.Token, ct).ConfigureAwait(false);
                // 续期会撤销旧 token，必须把新 token 写回 agent 的 .env 并重建容器，
                // 否则容器内的 NAS_TOKEN 仍是已撤销的旧值，agent 在过期前必然失联
                // （.env 只在部署时写入，续期不会自动同步）。
                await ApplyRenewedTokenAsync(state.AgentId, renewed.Token, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One agent failing to renew (e.g. revoked or malformed token) must not stop
                // renewal for the remaining agents — log and move on instead of letting the
                // exception escape and kill the whole BackgroundService.
                _logger?.LogWarning(ex, "Failed to renew token for agent {AgentId}; skipping.", state.AgentId);
            }
        }
    }

    /// <summary>
    /// 将续期后的 token 写回 agent 的 .env（原子替换、保持 600 权限）并重建其容器，
    /// 使容器内的 NAS_TOKEN 与已签发的新 token 一致。两步均为 best-effort：任一步
    /// 失败只记日志，不中断后续 agent 的续期（token 本身已续期成功，容器重建后生效）。
    /// </summary>
    private async Task ApplyRenewedTokenAsync(string agentId, string token, CancellationToken ct)
    {
        try
        {
            await UpdateEnvTokenAsync(agentId, token, ct).ConfigureAwait(false);
            // RestartAsync 内部为 down + up -d：必须重建容器，docker restart 不会
            // 重新读取 env_file，无法应用新的 NAS_TOKEN。
            await _supervisor.RestartAsync($"agent-{agentId}", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Agent {AgentId} token was renewed but applying it to the container failed; the agent may lose connectivity until its container is recreated.", agentId);
        }
    }

    /// <summary>替换 .env 中的 NAS_TOKEN 行；文件缺失时静默跳过（部署流程尚未完成）。</summary>
    private static async Task UpdateEnvTokenAsync(string agentId, string token, CancellationToken ct)
    {
        var envPath = Path.Combine(AgentPaths.AgentsRoot, agentId, ".env");
        if (!File.Exists(envPath))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(envPath, ct).ConfigureAwait(false);
        var replaced = false;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("NAS_TOKEN=", StringComparison.Ordinal))
            {
                lines[index] = "NAS_TOKEN=" + token;
                replaced = true;
            }
        }

        if (!replaced)
        {
            return;
        }

        // 原子替换：先写临时文件（600 权限，与 ComposeGenerator 一致）再 rename。
        var tempPath = envPath + ".tmp";
        await File.WriteAllLinesAsync(tempPath, lines, ct).ConfigureAwait(false);
        File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tempPath, envPath, overwrite: true);
    }
}
