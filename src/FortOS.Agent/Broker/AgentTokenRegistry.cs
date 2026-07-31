using System.Collections.Concurrent;
using FortOS.Core;

namespace FortOS.Agent.Broker;

/// <summary>
/// Stores metadata for Agent tokens issued by the current process.
/// </summary>
public sealed class AgentTokenRegistry
{
    private readonly ConcurrentDictionary<string, AgentTokenState> _tokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register or update an Agent token.
    /// </summary>
    public void Upsert(AgentTokenState state) => _tokens[state.AgentId] = state;

    /// <summary>
    /// Remove an Agent token.
    /// </summary>
    public bool Remove(string agentId, out AgentTokenState? state) => _tokens.Remove(agentId, out state);

    /// <summary>
    /// Enumerate token snapshot.
    /// </summary>
    public IReadOnlyList<AgentTokenState> Snapshot() => [.. _tokens.Values];
}

/// <summary>
/// Represents Agent token state.
/// </summary>
public sealed record AgentTokenState(string AgentId, string Token, string? Jti, DateTimeOffset ExpiresAt, string[] Capabilities);
