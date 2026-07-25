using System.Collections.Concurrent;
using GNAS.Core;

namespace GNAS.Agent.Broker;

/// <summary>
/// 保存当前进程已签发的 Agent 令牌元数据。
/// </summary>
public sealed class AgentTokenRegistry
{
    private readonly ConcurrentDictionary<string, AgentTokenState> _tokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册或更新 Agent 令牌。
    /// </summary>
    public void Upsert(AgentTokenState state) => _tokens[state.AgentId] = state;

    /// <summary>
    /// 移除 Agent 令牌。
    /// </summary>
    public bool Remove(string agentId, out AgentTokenState? state) => _tokens.Remove(agentId, out state);

    /// <summary>
    /// 枚举令牌快照。
    /// </summary>
    public IReadOnlyList<AgentTokenState> Snapshot() => [.. _tokens.Values];
}

/// <summary>
/// 表示 Agent 令牌状态。
/// </summary>
public sealed record AgentTokenState(string AgentId, string Token, string? Jti, DateTimeOffset ExpiresAt, string[] Capabilities);
