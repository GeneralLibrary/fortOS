using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Security.Models;
using Microsoft.Data.Sqlite;

namespace GNAS.Security.Services;

/// <summary>
/// 基于 NasToken、NAbility、ACL 与数据级别的权限决策引擎。
/// </summary>
public sealed class PermissionEngine : IPermissionEngine
{
    private static readonly Regex PrincipalPattern = new("^(user|agent|service|device):[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITokenManager _tokenManager;
    private readonly IDatabaseProvider? _database;
    private readonly IEventBus? _eventBus;
    private readonly object _aclSync = new();
    private readonly Dictionary<string, Dictionary<string, NAbilitySet>> _memoryAcls = new(StringComparer.Ordinal);

    /// <summary>
    /// 初始化权限决策引擎。
    /// </summary>
    /// <param name="tokenManager">令牌管理器。</param>
    /// <param name="database">可选数据库提供器。</param>
    /// <param name="eventBus">可选事件总线。</param>
    public PermissionEngine(ITokenManager tokenManager, IDatabaseProvider? database = null, IEventBus? eventBus = null)
    {
        _tokenManager = tokenManager;
        _database = database;
        _eventBus = eventBus;
    }

    /// <summary>
    /// 添加内存 ACL 规则。
    /// </summary>
    /// <param name="resourcePath">资源路径。</param>
    /// <param name="principal">主体。</param>
    /// <param name="capabilities">允许能力。</param>
    public void AddAcl(string resourcePath, string principal, IEnumerable<string> capabilities)
    {
        var set = new NAbilitySet();
        foreach (var capability in capabilities)
        {
            set.Add(capability);
        }

        lock (_aclSync)
        {
            if (!_memoryAcls.TryGetValue(resourcePath, out var principals))
            {
                principals = new Dictionary<string, NAbilitySet>(StringComparer.Ordinal);
                _memoryAcls[resourcePath] = principals;
            }

            principals[principal] = set;
        }
    }

    /// <inheritdoc />
    public async Task<PermissionResult> CheckPermissionAsync(string token, string requiredCapability, string? resourcePath, NasDataLevel dataLevel, CancellationToken ct)
    {
        var validation = await _tokenManager.ValidateTokenAsync(token, ct).ConfigureAwait(false);
        if (!validation.IsValid || validation.Payload is not NasTokenPayload payload)
        {
            return await DenyAsync(requiredCapability, dataLevel, validation.Subject, resourcePath, validation.ErrorMessage ?? "令牌无效。", ct).ConfigureAwait(false);
        }

        var required = NAbility.Parse(requiredCapability);
        var matched = payload.Capabilities.FirstOrDefault(capability => capability.Matches(required));
        if (matched is null)
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "令牌不包含所需能力。", ct).ConfigureAwait(false);
        }

        if (!await CheckAclAsync(payload, required, resourcePath, ct).ConfigureAwait(false))
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "资源 ACL 拒绝访问。", ct).ConfigureAwait(false);
        }

        if (payload.TrustLevel < (int)dataLevel)
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "信任级别不足。", ct).ConfigureAwait(false);
        }

        if (!ValidateDelegationChain(payload.DelegationChain))
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "委托链格式无效。", ct).ConfigureAwait(false);
        }

        var result = new PermissionResult
        {
            Granted = true,
            RequiredDataLevel = dataLevel,
            MatchedCapability = matched.ToString(),
        };
        await PublishDecisionAsync(true, payload.Sub, requiredCapability, resourcePath, null, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<bool> CheckAclAsync(NasTokenPayload payload, NAbility required, string? resourcePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return true;
        }

        var principals = await LoadAclsAsync(resourcePath, ct).ConfigureAwait(false);
        if (principals.Count == 0)
        {
            return true;
        }

        var candidates = new[] { payload.Sub }.Concat(payload.DelegationChain).Where(static p => !string.IsNullOrWhiteSpace(p));
        return candidates.Any(principal => principals.TryGetValue(principal, out var set) && set.Satisfies(required));
    }

    private async Task<Dictionary<string, NAbilitySet>> LoadAclsAsync(string resourcePath, CancellationToken ct)
    {
        var result = new Dictionary<string, NAbilitySet>(StringComparer.Ordinal);
        lock (_aclSync)
        {
            if (_memoryAcls.TryGetValue(resourcePath, out var memory))
            {
                foreach (var pair in memory)
                {
                    result[pair.Key] = pair.Value;
                }
            }
        }

        if (_database is null)
        {
            return result;
        }

        await EnsureAclTableAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT principal, capabilities_json FROM resource_acls WHERE resource_path = $resource_path;";
        command.Parameters.AddWithValue("$resource_path", resourcePath);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = NAbilitySet.FromJson(reader.IsDBNull(1) ? "[]" : reader.GetString(1));
        }

        return result;
    }

    private async Task EnsureAclTableAsync(CancellationToken ct)
    {
        if (_database is null)
        {
            return;
        }

        await _database.InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS resource_acls (
    resource_path TEXT NOT NULL,
    principal TEXT NOT NULL,
    capabilities_json TEXT DEFAULT '[]',
    PRIMARY KEY(resource_path, principal)
);
""";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool ValidateDelegationChain(IEnumerable<string> chain)
    {
        foreach (var principal in chain)
        {
            if (string.IsNullOrWhiteSpace(principal) || !PrincipalPattern.IsMatch(principal))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<PermissionResult> DenyAsync(string requiredCapability, NasDataLevel dataLevel, string? subject, string? resourcePath, string reason, CancellationToken ct)
    {
        await PublishDecisionAsync(false, subject, requiredCapability, resourcePath, reason, ct).ConfigureAwait(false);
        return new PermissionResult
        {
            Granted = false,
            RequiredDataLevel = dataLevel,
            DenyReason = reason,
        };
    }

    private async Task PublishDecisionAsync(bool granted, string? subject, string capability, string? resourcePath, string? reason, CancellationToken ct)
    {
        if (_eventBus is null)
        {
            return;
        }

        var topic = granted ? "security.auth.granted" : "security.auth.denied";
        var type = granted ? "security.authorization.determined" : "security.authorization.denied";
        var data = JsonSerializer.Serialize(new
        {
            subject,
            capability,
            resourcePath,
            granted,
            reason,
            decidedAt = DateTimeOffset.UtcNow,
        }, JsonOptions);
        await _eventBus.PublishAsync(topic, type, data, ct).ConfigureAwait(false);
    }
}
