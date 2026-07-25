namespace GNAS.Core;

/// <summary>用户账户抽象。</summary>
public interface IUserAccount
{
    /// <summary>创建用户。</summary>
    Task CreateUserAsync(string username, string password, CancellationToken ct);
    /// <summary>删除用户。</summary>
    Task DeleteUserAsync(string username, CancellationToken ct);
    /// <summary>添加用户到组。</summary>
    Task AddUserToGroupAsync(string username, string group, CancellationToken ct);
    /// <summary>设置文件权限。</summary>
    Task SetFilePermissionsAsync(string path, FilePermission permission, CancellationToken ct);
}
