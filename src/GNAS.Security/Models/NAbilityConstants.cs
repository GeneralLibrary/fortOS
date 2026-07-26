namespace GNAS.Security.Models;

/// <summary>
/// Common NAbility constants.
/// </summary>
public static class NAbilityConstants
{
    /// <summary>All storage permissions.</summary>
    public const string StorageAll = "storage:**";
    /// <summary>Storage pool read permission.</summary>
    public const string StoragePoolRead = "storage:pool:*:read";
    /// <summary>Storage share read permission.</summary>
    public const string StorageShareRead = "storage:share:*:read";
    /// <summary>Storage share write permission.</summary>
    public const string StorageShareWrite = "storage:share:*:write";
    /// <summary>Agent deploy permission.</summary>
    public const string AgentDeploy = "agent:lifecycle:deploy";
    /// <summary>Agent start/stop permission.</summary>
    public const string AgentStartStop = "agent:lifecycle:start_stop";
    /// <summary>Agent token issue permission.</summary>
    public const string AgentTokenIssue = "agent:token:issue";
    /// <summary>Administrator all permissions.</summary>
    public const string AdminAll = "admin:**";
    /// <summary>User management permission.</summary>
    public const string AdminUserAll = "admin:user:*";
    /// <summary>Audit read permission.</summary>
    public const string AuditRead = "audit:log:read";
    /// <summary>Internal data access permission.</summary>
    public const string DataInternal = "data:level:internal";
    /// <summary>Sensitive data access permission.</summary>
    public const string DataSensitive = "data:level:sensitive";
}
