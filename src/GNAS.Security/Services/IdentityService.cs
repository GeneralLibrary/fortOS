using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNAS.Core;
using GNAS.Security.Models;
using Microsoft.Data.Sqlite;

namespace GNAS.Security.Services;

/// <summary>
/// GNAS 身份认证服务。
/// </summary>
public sealed class IdentityService : IIdentityService
{
    private static readonly Regex UsernamePattern = new("^[a-z_][a-z0-9_-]{0,31}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabaseProvider _database;
    private readonly ITokenManager _tokenManager;
    private readonly IGnasConfiguration? _configuration;

    /// <summary>
    /// 初始化身份认证服务。
    /// </summary>
    /// <param name="database">数据库提供器。</param>
    /// <param name="tokenManager">令牌管理器。</param>
    /// <param name="configuration">可选配置。</param>
    public IdentityService(IDatabaseProvider database, ITokenManager tokenManager, IGnasConfiguration? configuration = null)
    {
        _database = database;
        _tokenManager = tokenManager;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateLocalAsync(string username, string password, CancellationToken ct)
    {
        await EnsureDatabaseAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        var user = await GetUserAsync(connection, username, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Failure("用户名或密码错误。");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow)
        {
            return Failure("账户已锁定，请稍后重试。");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            var attempts = user.FailedAttempts + 1;
            var lockedUntil = attempts >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15) : (DateTimeOffset?)null;
            await UpdateLoginStateAsync(connection, username, attempts, lockedUntil, ct).ConfigureAwait(false);
            return Failure(lockedUntil.HasValue ? "账户已锁定，请 15 分钟后重试。" : "用户名或密码错误。");
        }

        await UpdateLoginStateAsync(connection, username, 0, null, ct).ConfigureAwait(false);
        var capabilities = await ResolveCapabilitiesAsync(connection, user.RolesJson, ct).ConfigureAwait(false);
        var token = await _tokenManager.IssueTokenAsync($"user:{username}", TokenType.Session, capabilities, 3, TimeSpan.FromHours(8), [$"user:{username}"], null, ct).ConfigureAwait(false);
        var validation = await _tokenManager.ValidateTokenAsync(token, ct).ConfigureAwait(false);
        return new AuthResult { Success = true, NasToken = token, TokenPayload = validation.Payload };
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateTotpAsync(string username, string code, CancellationToken ct)
    {
        await EnsureDatabaseAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT totp_secret FROM users WHERE username = $username;";
        command.Parameters.AddWithValue("$username", username);
        var secret = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Failure("TOTP 未配置。");
        }

        return VerifyTotp(secret, code) ? new AuthResult { Success = true } : Failure("TOTP 验证失败。");
    }

    /// <inheritdoc />
    public Task<AuthResult> AuthenticateLdapAsync(string domain, string username, string password, CancellationToken ct)
    {
        var section = _configuration?.GetSection("security:ldap") ?? new Dictionary<string, string>();
        if (section.Count == 0 || !_configurationEnabled("security:ldap:enabled"))
        {
            return Task.FromResult(Failure("LDAP 认证未配置"));
        }

        return Task.FromResult(Failure("LDAP 认证配置已检测到，但当前版本未集成目录绑定客户端。"));
    }

    /// <inheritdoc />
    public Task<AuthResult> AuthenticateOAuthAsync(string provider, string authorizationCode, string? redirectUri, CancellationToken ct)
    {
        var section = _configuration?.GetSection("security:oauth") ?? new Dictionary<string, string>();
        if (section.Count == 0 || !_configurationEnabled("security:oauth:enabled"))
        {
            return Task.FromResult(Failure("OAuth 认证未配置"));
        }

        return Task.FromResult(Failure("OAuth 认证配置已检测到，但当前版本未集成 OIDC 客户端。"));
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateServiceAsync(string accountId, string apiKey, CancellationToken ct)
    {
        await EnsureDatabaseAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT api_key_hash, capabilities_json FROM service_accounts WHERE account_id = $account_id;";
        command.Parameters.AddWithValue("$account_id", accountId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return Failure("服务账号不存在。");
        }

        var expectedHash = reader.GetString(0);
        var capabilitiesJson = reader.IsDBNull(1) ? "[]" : reader.GetString(1);
        if (!FixedTimeEquals(expectedHash, Sha256Hex(apiKey)))
        {
            return Failure("服务账号密钥错误。");
        }

        var capabilities = JsonSerializer.Deserialize<string[]>(capabilitiesJson, JsonOptions) ?? [];
        var token = await _tokenManager.IssueTokenAsync($"service:{accountId}", TokenType.Service, capabilities, 2, TimeSpan.FromHours(1), [$"service:{accountId}"], null, ct).ConfigureAwait(false);
        var validation = await _tokenManager.ValidateTokenAsync(token, ct).ConfigureAwait(false);
        return new AuthResult { Success = true, NasToken = token, TokenPayload = validation.Payload };
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateAgentAsync(string agentId, string token, CancellationToken ct)
    {
        var validation = await _tokenManager.ValidateTokenAsync(token, ct).ConfigureAwait(false);
        if (!validation.IsValid || !string.Equals(validation.Subject, $"agent:{agentId}", StringComparison.Ordinal))
        {
            return Failure(validation.ErrorMessage ?? "Agent 令牌无效。");
        }

        return new AuthResult { Success = true, NasToken = token, TokenPayload = validation.Payload };
    }

    /// <inheritdoc />
    public async Task<AuthResult> CreateLocalUserAsync(string username, string password, string? displayName, string? email, CancellationToken ct)
    {
        if (!UsernamePattern.IsMatch(username))
        {
            return Failure("用户名格式无效。");
        }

        if (!IsPasswordValid(password))
        {
            return Failure("密码必须至少 8 位且包含大小写字母和数字。");
        }

        await EnsureDatabaseAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO users (username, password_hash, display_name, email, failed_attempts, locked_until, created_at, roles_json)
VALUES ($username, $password_hash, $display_name, $email, 0, NULL, $created_at, $roles_json);
""";
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$password_hash", BCrypt.Net.BCrypt.HashPassword(password, 12));
        command.Parameters.AddWithValue("$display_name", (object?)displayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$email", (object?)email ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$roles_json", JsonSerializer.Serialize(new[] { "user" }, JsonOptions));
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new AuthResult { Success = true };
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Failure("用户已存在。");
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> DeleteLocalUserAsync(string username, CancellationToken ct)
    {
        await EnsureDatabaseAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM users WHERE username = $username;";
        command.Parameters.AddWithValue("$username", username);
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0 ? new AuthResult { Success = true } : Failure("用户不存在。");
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct) => await _database.InitializeAsync(ct).ConfigureAwait(false);

    private static async Task<UserRow?> GetUserAsync(SqliteConnection connection, string username, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT password_hash, failed_attempts, locked_until, roles_json FROM users WHERE username = $username;";
        command.Parameters.AddWithValue("$username", username);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var lockedText = reader.IsDBNull(2) ? null : reader.GetString(2);
        return new UserRow(
            reader.GetString(0),
            reader.GetInt32(1),
            DateTimeOffset.TryParse(lockedText, out var lockedUntil) ? lockedUntil : null,
            reader.IsDBNull(3) ? "[]" : reader.GetString(3));
    }

    private static async Task UpdateLoginStateAsync(SqliteConnection connection, string username, int failedAttempts, DateTimeOffset? lockedUntil, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET failed_attempts = $failed_attempts, locked_until = $locked_until WHERE username = $username;";
        command.Parameters.AddWithValue("$failed_attempts", failedAttempts);
        command.Parameters.AddWithValue("$locked_until", lockedUntil.HasValue ? lockedUntil.Value.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$username", username);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string[]> ResolveCapabilitiesAsync(SqliteConnection connection, string rolesJson, CancellationToken ct)
    {
        var values = JsonSerializer.Deserialize<string[]>(rolesJson, JsonOptions) ?? [];
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            switch (value)
            {
                case "admin":
                    capabilities.Add(NAbilityConstants.AdminAll);
                    capabilities.Add(NAbilityConstants.StorageAll);
                    break;
                case "auditor":
                    capabilities.Add(NAbilityConstants.AuditRead);
                    break;
                case "agent-admin":
                    capabilities.Add("agent:**");
                    break;
                case "user":
                    capabilities.Add(NAbilityConstants.DataInternal);
                    break;
                default:
                    if (value.Contains(':', StringComparison.Ordinal))
                    {
                        capabilities.Add(value);
                    }
                    else
                    {
                        await AddRoleCapabilitiesAsync(connection, value, capabilities, ct).ConfigureAwait(false);
                    }
                    break;
            }
        }

        return [.. capabilities];
    }

    private static async Task AddRoleCapabilitiesAsync(SqliteConnection connection, string role, ISet<string> capabilities, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT capabilities_json FROM roles WHERE role_id = $role OR name = $role LIMIT 1;";
        command.Parameters.AddWithValue("$role", role);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        foreach (var capability in JsonSerializer.Deserialize<string[]>(json ?? "[]", JsonOptions) ?? [])
        {
            capabilities.Add(capability);
        }
    }

    private static bool VerifyTotp(string secret, string code)
    {
        if (!int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        var key = DecodeBase32(secret);
        var timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            if (string.Equals(ComputeTotp(key, timestep + offset), code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeTotp(byte[] key, long timestep)
    {
        Span<byte> counter = stackalloc byte[8];
        BitConverter.TryWriteBytes(counter, timestep);
        if (BitConverter.IsLittleEndian)
        {
            counter.Reverse();
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var bitCount = 0;
        var output = new List<byte>();
        foreach (var c in value.ToUpperInvariant().Where(static c => c != '=' && !char.IsWhiteSpace(c)))
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
            {
                throw new ArgumentException("Base32 TOTP 密钥格式无效。", nameof(value));
            }

            bits = (bits << 5) | index;
            bitCount += 5;
            if (bitCount >= 8)
            {
                output.Add((byte)((bits >> (bitCount - 8)) & 0xFF));
                bitCount -= 8;
            }
        }

        return [.. output];
    }

    private static bool IsPasswordValid(string password) => password.Length >= 8 && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);

    private bool _configurationEnabled(string key) => bool.TryParse(_configuration?.GetValue(key), out var enabled) && enabled;

    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedTimeEquals(string leftHex, string rightHex)
    {
        var left = Encoding.UTF8.GetBytes(leftHex.ToLowerInvariant());
        var right = Encoding.UTF8.GetBytes(rightHex.ToLowerInvariant());
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static AuthResult Failure(string message) => new() { Success = false, ErrorMessage = message };

    private sealed record UserRow(string PasswordHash, int FailedAttempts, DateTimeOffset? LockedUntil, string RolesJson);
}
