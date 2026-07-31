namespace FortOS.Core;

/// <summary>User account abstraction.</summary>
public interface IUserAccount
{
    /// <summary>Create a user.</summary>
    Task CreateUserAsync(string username, string password, CancellationToken ct);
    /// <summary>Delete a user.</summary>
    Task DeleteUserAsync(string username, CancellationToken ct);
    /// <summary>Add a user to a group.</summary>
    Task AddUserToGroupAsync(string username, string group, CancellationToken ct);
    /// <summary>Set file permissions.</summary>
    Task SetFilePermissionsAsync(string path, FilePermission permission, CancellationToken ct);
}
