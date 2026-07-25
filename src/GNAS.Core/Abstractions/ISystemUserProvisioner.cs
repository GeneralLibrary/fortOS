namespace GNAS.Core;

/// <summary>
/// 系统用户供给抽象。
/// 用于在 GNAS 内部用户（SQLite）创建或删除时，将账户桥接到底层系统
/// （如 Linux 系统用户 + Samba 用户数据库），使共享协议客户端可以使用同一套凭据认证。
/// 实现必须是尽力而为的：供给失败不应阻断 GNAS 内部用户的生命周期操作。
/// </summary>
public interface ISystemUserProvisioner
{
    /// <summary>为指定用户名和明文密码供给系统侧账户（幂等）。</summary>
    Task ProvisionAsync(string username, string password, CancellationToken ct);
    /// <summary>移除指定用户名的系统侧账户（幂等）。</summary>
    Task RemoveAsync(string username, CancellationToken ct);
}
