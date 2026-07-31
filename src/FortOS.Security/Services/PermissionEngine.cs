using System.Text.Json;
using System.Text.RegularExpressions;
using FortOS.Core;
using FortOS.Security.Models;
using Microsoft.Data.Sqlite;

namespace FortOS.Security.Services;

/// <summary>
/// Permission decision engine based on NasToken, NAbility, ACL, and data levels.
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
    /// Initialize the permission engine.
    /// </summary>
    /// <param name="tokenManager">Token manager.</param>
    /// <param name="database">Optional database provider.</param>
    /// <param name="eventBus">Optional event bus.</param>
    public PermissionEngine(ITokenManager tokenManager, IDatabaseProvider? database = null, IEventBus? eventBus = null)
    {
        _tokenManager = tokenManager;
        _database = database;
        _eventBus = eventBus;
    }

    /// <summary>
    /// Adds an in-memory ACL rule.
    /// </summary>
    /// <param name="resourcePath">Resource path.</param>
    /// <param name="principal">Subject.</param>
    /// <param name="capabilities">Allowed capabilities.</param>
    public void AddAcl(string resourcePath, string principal, IEnumerable<string> capabilities)
    {
        var set = new NAbilitySet();
        foreach (var capability in capabilities)
        {
            set.Add(capability);
        }

        lock (_aclSync)
        {
            if (!_memoryAcls.TryGetValue(ResourceAclService.NormalizePath(resourcePath), out var principals))
            {
                principals = new Dictionary<string, NAbilitySet>(StringComparer.Ordinal);
                _memoryAcls[ResourceAclService.NormalizePath(resourcePath)] = principals;
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
            return await DenyAsync(requiredCapability, dataLevel, validation.Subject, resourcePath, validation.ErrorMessage ?? "Token is invalid.", ct).ConfigureAwait(false);
        }

        if (payload.Capabilities.Satisfies(NAbilityConstants.AdminAll))
        {
            var adminResult = new PermissionResult
            {
                Granted = true,
                RequiredDataLevel = dataLevel,
                MatchedCapability = NAbilityConstants.AdminAll,
            };
            await PublishDecisionAsync(true, payload.Sub, requiredCapability, resourcePath, null, ct).ConfigureAwait(false);
            return adminResult;
        }

        var required = NAbility.Parse(requiredCapability);
        var matched = payload.Capabilities.FirstOrDefault(capability => capability.Matches(required));
        if (matched is null)
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "Token does not contain the required capability.", ct).ConfigureAwait(false);
        }

        if (!await CheckAclAsync(payload, required, resourcePath, ct).ConfigureAwait(false))
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "Resource ACL denied access.", ct).ConfigureAwait(false);
        }

        if (payload.TrustLevel < (int)dataLevel)
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "Insufficient trust level.", ct).ConfigureAwait(false);
        }

        if (!ValidateDelegationChain(payload.DelegationChain))
        {
            return await DenyAsync(requiredCapability, dataLevel, payload.Sub, resourcePath, "Invalid delegation chain format.", ct).ConfigureAwait(false);
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
        // A resource ACL is authoritative for its subtree. Walk leaf to root so a
        // more-specific ACL cannot accidentally be widened by an ancestor.
        foreach (var candidate in EnumerateAclPaths(resourcePath))
        {
            var result = new Dictionary<string, NAbilitySet>(StringComparer.Ordinal);
            lock (_aclSync)
            {
                if (_memoryAcls.TryGetValue(candidate, out var memory))
                {
                    foreach (var pair in memory) result[pair.Key] = pair.Value;
                }
            }

            if (_database is not null)
            {
                await _database.InitializeAsync(ct).ConfigureAwait(false);
                await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT principal, capabilities_json FROM resource_acls WHERE resource_path = $resource_path;";
                command.Parameters.AddWithValue("$resource_path", candidate);
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    result[reader.GetString(0)] = NAbilitySet.FromJson(reader.IsDBNull(1) ? "[]" : reader.GetString(1));
            }

            if (result.Count > 0) return result;
        }

        return new Dictionary<string, NAbilitySet>(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateAclPaths(string path)
    {
        var current = ResourceAclService.NormalizePath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            var parent = Path.GetDirectoryName(current)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal)) yield break;
            current = parent;
        }
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
