using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FortOS.Core;
using FortOS.Security.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FortOS.Security.Services;

/// <summary>
/// FortOS identity authentication service.
/// </summary>
public sealed class IdentityService : IIdentityService
{
    private static readonly Regex UsernamePattern = new("^[a-z_][a-z0-9_-]{0,31}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // Fixed BCrypt hash used to equalize the response time of the "unknown user" login path
    // with the real verification path, preventing username enumeration via timing. The hash
    // itself is never used to authenticate anyone; it just costs the same bcrypt work factor
    // as real password hashes (cost 12, matching CreateUserAsync below).
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("fortos-dummy-password", 12);
    private readonly IDatabaseProvider _database;
    private readonly ITokenManager _tokenManager;
    private readonly IFortOSConfiguration? _configuration;
    private readonly IReadOnlyList<ISystemUserProvisioner> _provisioners;
    private readonly ILogger<IdentityService>? _logger;

    /// <summary>
    /// Initialize the identity service.
    /// </summary>
    /// <param name="database">Database provider.</param>
    /// <param name="tokenManager">Token manager.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <param name="provisioners">Optional collection of system user provisioners (e.g., Samba user bridge).</param>
    /// <param name="logger">Optional logger.</param>
    public IdentityService(IDatabaseProvider database, ITokenManager tokenManager, IFortOSConfiguration? configuration = null, IEnumerable<ISystemUserProvisioner>? provisioners = null, ILogger<IdentityService>? logger = null)
    {
        _database = database;
        _tokenManager = tokenManager;
        _configuration = configuration;
        _provisioners = provisioners?.ToArray() ?? [];
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateLocalAsync(string username, string password, CancellationToken ct)
    {
        await EnsureDatabaseAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        var user = await GetUserAsync(connection, username, ct).ConfigureAwait(false);
        if (user is null)
        {
            // Run a dummy BCrypt verification against a fixed hash so that the time spent
            // here is indistinguishable from the "user exists" path; returning immediately
            // would let an attacker enumerate valid usernames via response timing.
            BCrypt.Net.BCrypt.Verify(password, DummyPasswordHash);
            return Failure("Incorrect username or password.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow)
        {
            return Failure("Account is locked, please try again later.");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            var attempts = user.FailedAttempts + 1;
            var lockedUntil = attempts >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15) : (DateTimeOffset?)null;
            await UpdateLoginStateAsync(connection, username, attempts, lockedUntil, ct).ConfigureAwait(false);
            return Failure(lockedUntil.HasValue ? "Account is locked, please try again in 15 minutes." : "Incorrect username or password.");
        }

        await UpdateLoginStateAsync(connection, username, 0, null, ct).ConfigureAwait(false);
        var capabilities = await ResolveCapabilitiesAsync(connection, user.RolesJson, ct).ConfigureAwait(false);
        // 会话令牌一律附带刷新能力：刷新自己的 token 是已认证用户的自服务操作，
        // 不应要求管理员权限（此前 CapabilityConvention 默认 admin:** 导致普通用户 403）。
        capabilities = [.. capabilities, NAbilityConstants.SessionRefresh];
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
            return Failure("TOTP not configured.");
        }

        return VerifyTotp(secret, code) ? new AuthResult { Success = true } : Failure("TOTP verification failed.");
    }

    /// <inheritdoc />
    public Task<AuthResult> AuthenticateLdapAsync(string domain, string username, string password, CancellationToken ct)
    {
        var section = _configuration?.GetSection("security:ldap") ?? new Dictionary<string, string>();
        if (section.Count == 0 || !_configurationEnabled("security:ldap:enabled"))
        {
            return Task.FromResult(Failure("LDAP authentication not configured"));
        }

        return Task.FromResult(Failure("LDAP authentication configuration detected, but the current version does not include a directory binding client."));
    }

    /// <inheritdoc />
    public Task<AuthResult> AuthenticateOAuthAsync(string provider, string authorizationCode, string? redirectUri, CancellationToken ct)
    {
        var section = _configuration?.GetSection("security:oauth") ?? new Dictionary<string, string>();
        if (section.Count == 0 || !_configurationEnabled("security:oauth:enabled"))
        {
            return Task.FromResult(Failure("OAuth authentication not configured"));
        }

        return Task.FromResult(Failure("OAuth authentication configuration detected, but the current version does not include an OIDC client."));
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
            return Failure("Service account does not exist.");
        }

        var expectedHash = reader.GetString(0);
        var capabilitiesJson = reader.IsDBNull(1) ? "[]" : reader.GetString(1);
        if (!FixedTimeEquals(expectedHash, Sha256Hex(apiKey)))
        {
            return Failure("Service account key is incorrect.");
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
            return Failure(validation.ErrorMessage ?? "Agent token is invalid.");
        }

        return new AuthResult { Success = true, NasToken = token, TokenPayload = validation.Payload };
    }

    /// <inheritdoc />
    public async Task<AuthResult> CreateLocalUserAsync(string username, string password, string? displayName, string? email, CancellationToken ct)
    {
        if (!UsernamePattern.IsMatch(username))
        {
            return Failure("Invalid username format.");
        }

        if (!IsPasswordValid(password))
        {
            return Failure("Password must be at least 8 characters and contain uppercase letters, lowercase letters, and digits.");
        }

        await EnsureDatabaseAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);

        // The first system user automatically gets the admin role, allowing the bootstrap anonymous mode to transition smoothly to mandatory authentication.
        var isFirstUser = await CountUsersAsync(connection, ct).ConfigureAwait(false) == 0;
        var roles = isFirstUser ? new[] { "admin", "user" } : new[] { "user" };

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
        command.Parameters.AddWithValue("$roles_json", JsonSerializer.Serialize(roles, JsonOptions));
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Failure("User already exists.");
        }

        await ProvisionSystemUsersAsync(username, password, ct).ConfigureAwait(false);
        return new AuthResult { Success = true };
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
        if (affected == 0)
        {
            return Failure("User does not exist.");
        }

        await RemoveSystemUsersAsync(username, ct).ConfigureAwait(false);
        return new AuthResult { Success = true };
    }

    private async Task<long> CountUsersAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM users;";
        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
    }

    /// <summary>Invokes all system user provisioners; individual failures are only logged as warnings and do not affect the FortOS internal user creation result.</summary>
    private async Task ProvisionSystemUsersAsync(string username, string password, CancellationToken ct)
    {
        foreach (var provisioner in _provisioners)
        {
            try
            {
                await provisioner.ProvisionAsync(username, password, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "System user provisioner {Provisioner} failed to process user {Username}.", provisioner.GetType().Name, username.ReplaceLineEndings(" "));
            }
        }
    }

    /// <summary>Invokes all system user provisioners to perform removal; individual failures are only logged as warnings.</summary>
    private async Task RemoveSystemUsersAsync(string username, CancellationToken ct)
    {
        foreach (var provisioner in _provisioners)
        {
            try
            {
                await provisioner.RemoveAsync(username, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "System user provisioner {Provisioner} failed to remove user {Username}.", provisioner.GetType().Name, username.ReplaceLineEndings(" "));
            }
        }
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
                throw new ArgumentException("Invalid Base32 TOTP key format.", nameof(value));
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
