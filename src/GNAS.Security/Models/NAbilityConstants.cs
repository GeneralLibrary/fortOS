namespace GNAS.Security.Models;

/// <summary>
/// 常用 NAbility 常量。
/// </summary>
public static class NAbilityConstants
{
    /// <summary>存储全部权限。</summary>
    public const string StorageAll = "storage:**";
    /// <summary>存储池读取权限。</summary>
    public const string StoragePoolRead = "storage:pool:*:read";
    /// <summary>存储共享读取权限。</summary>
    public const string StorageShareRead = "storage:share:*:read";
    /// <summary>存储共享写入权限。</summary>
    public const string StorageShareWrite = "storage:share:*:write";
    /// <summary>Agent 部署权限。</summary>
    public const string AgentDeploy = "agent:lifecycle:deploy";
    /// <summary>Agent 启停权限。</summary>
    public const string AgentStartStop = "agent:lifecycle:start_stop";
    /// <summary>Agent 令牌签发权限。</summary>
    public const string AgentTokenIssue = "agent:token:issue";
    /// <summary>管理员全部权限。</summary>
    public const string AdminAll = "admin:**";
    /// <summary>用户管理权限。</summary>
    public const string AdminUserAll = "admin:user:*";
    /// <summary>审计读取权限。</summary>
    public const string AuditRead = "audit:log:read";
    /// <summary>内部数据访问权限。</summary>
    public const string DataInternal = "data:level:internal";
    /// <summary>敏感数据访问权限。</summary>
    public const string DataSensitive = "data:level:sensitive";
}
