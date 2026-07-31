using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FortOS.Core;
using FortOS.Security.Models;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

namespace FortOS.Security.Services;

/// <summary>
/// NAS token manager based on RS256 JWT.
/// </summary>
public sealed class NasTokenManager : ITokenManager
{
    private const string SigningKeyId = "nas-token-signing";
    private const string ActiveSigningKeyName = "token-signing-active-kid";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INasKeyStore _keyStore;
    private readonly IDatabaseProvider _database;
    private readonly IFortOSConfiguration? _configuration;
    private readonly ConcurrentDictionary<string, bool> _revocationCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialize the token manager.
    /// </summary>
    /// <param name="keyStore">Key store.</param>
    /// <param name="database">Database provider.</param>
    /// <param name="configuration">Optional configuration.</param>
    public NasTokenManager(INasKeyStore keyStore, IDatabaseProvider database, IFortOSConfiguration? configuration = null)
    {
        _keyStore = keyStore;
        _database = database;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<string> IssueTokenAsync(string subject, TokenType tokenType, IEnumerable<string> capabilities, int trustLevel, TimeSpan lifetime, IEnumerable<string>? delegationChain, string? deviceBinding, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Token subject cannot be empty.", nameof(subject));
        }

        if (trustLevel is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(trustLevel), "Trust level must be between 0 and 5.");
        }

        var now = DateTimeOffset.UtcNow;
        var jti = Guid.CreateVersion7().ToString();
        var abilitySet = new NAbilitySet();
        foreach (var capability in capabilities)
        {
            abilitySet.Add(capability);
        }

        var payload = new NasTokenPayload
        {
            Iss = GetIssuer(),
            Sub = subject,
            Iat = now,
            Exp = now.Add(lifetime),
            TokenType = tokenType,
            TrustLevel = trustLevel,
            Capabilities = abilitySet,
            DelegationChain = delegationChain?.Where(static v => !string.IsNullOrWhiteSpace(v)).ToArray() ?? [],
            DeviceBinding = deviceBinding,
            Jti = jti,
        };

        return await CreateJwtAsync(payload, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FortOS.Core.TokenValidationResult> ValidateTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Failed("Token cannot be empty.");
        }

        try
        {
            var keyId = ReadKeyId(token);
            var keyBytes = await _keyStore.GetSecretAsync($"signing-{keyId}", ct).ConfigureAwait(false);
            if (keyBytes is null) return Failed("Signing key not found.");
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(keyBytes, out _);
            var validationKey = new RsaSecurityKey(rsa)
            {
                KeyId = keyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            };
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = validationKey,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero,
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, parameters, out _);
            var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti) ?? principal.FindFirstValue(ClaimTypes.SerialNumber);
            if (string.IsNullOrWhiteSpace(jti))
            {
                return Failed("Token missing jti.");
            }

            if (await IsTokenRevokedAsync(jti, ct).ConfigureAwait(false))
            {
                return Failed("Token has been revoked.", jti: jti);
            }

            var payload = BuildPayload(principal, jti);
            var expectedDevice = _configuration?.GetValue("security:device_binding") ?? Environment.GetEnvironmentVariable("FortOS_DEVICE_BINDING");
            if (!string.IsNullOrWhiteSpace(expectedDevice) && !string.Equals(expectedDevice, payload.DeviceBinding, StringComparison.Ordinal))
            {
                return Failed("Token device binding does not match.", jti: jti);
            }

            return new FortOS.Core.TokenValidationResult
            {
                IsValid = true,
                Payload = payload,
                Jti = payload.Jti,
                Subject = payload.Sub,
                TokenType = payload.TokenType,
                Capabilities = payload.Capabilities.Select(static a => a.ToString()).ToArray(),
                ExpiresAt = payload.Exp,
            };
        }
        catch (SecurityTokenExpiredException ex)
        {
            return Failed("Token has expired.", ex);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException or CryptographicException)
        {
            return Failed($"Token validation failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task RevokeTokenAsync(string jti, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            throw new ArgumentException("jti cannot be empty.", nameof(jti));
        }

        await EnsureSecurityTablesAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO token_revocations (jti, revoked_at, reason) VALUES ($jti, $revoked_at, $reason);";
        command.Parameters.AddWithValue("$jti", jti);
        command.Parameters.AddWithValue("$revoked_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$reason", reason);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _revocationCache[jti] = true;
    }

    /// <inheritdoc />
    public async Task<string> RenewTokenAsync(string token, CancellationToken ct)
    {
        var validation = await ValidateTokenAsync(token, ct).ConfigureAwait(false);
        if (!validation.IsValid || validation.Payload is not NasTokenPayload payload)
        {
            throw new TokenValidationException(validation.ErrorMessage ?? "Token cannot be renewed.");
        }

        var lifetime = GetDefaultLifetime();
        var newToken = await IssueTokenAsync(payload.Sub, payload.TokenType, payload.Capabilities.Select(static a => a.ToString()), payload.TrustLevel, lifetime, payload.DelegationChain, payload.DeviceBinding, ct).ConfigureAwait(false);
        await RevokeTokenAsync(payload.Jti, "renewed", ct).ConfigureAwait(false);
        return newToken;
    }

    /// <inheritdoc />
    public async Task<bool> IsTokenRevokedAsync(string jti, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        if (_revocationCache.ContainsKey(jti))
        {
            return true;
        }

        await EnsureSecurityTablesAsync(ct).ConfigureAwait(false);
        await using var connection = await _database.GetConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM token_revocations WHERE jti = $jti LIMIT 1;";
        command.Parameters.AddWithValue("$jti", jti);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not null)
        {
            _revocationCache[jti] = true;
            return true;
        }

        return false;
    }

    private async Task<string> CreateJwtAsync(NasTokenPayload payload, CancellationToken ct)
    {
        var keyId = await GetActiveSigningKeyIdAsync(ct).ConfigureAwait(false);
        var keyBytes = await _keyStore.GetOrCreateSigningKeyAsync(keyId, ct).ConfigureAwait(false);
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(keyBytes, out _);
        var signingKey = new RsaSecurityKey(rsa)
        {
            KeyId = keyId,
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Iss, payload.Iss),
            new(JwtRegisteredClaimNames.Sub, payload.Sub),
            new(JwtRegisteredClaimNames.Jti, payload.Jti),
            new(JwtRegisteredClaimNames.Iat, payload.Iat.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("token_type", payload.TokenType.ToString()),
            new("trust_level", payload.TrustLevel.ToString(), ClaimValueTypes.Integer32),
            new("capabilities", payload.Capabilities.ToJson()),
            new("delegation_chain", JsonSerializer.Serialize(payload.DelegationChain, JsonOptions)),
        };
        if (!string.IsNullOrWhiteSpace(payload.DeviceBinding))
        {
            claims.Add(new Claim("device_binding", payload.DeviceBinding));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = payload.Exp.UtcDateTime,
            NotBefore = (payload.Exp > payload.Iat ? payload.Iat : payload.Exp.AddSeconds(-1)).UtcDateTime,
            IssuedAt = payload.Iat.UtcDateTime,
            SigningCredentials = credentials,
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private NasTokenPayload BuildPayload(ClaimsPrincipal principal, string jti)
    {
        var capabilitiesJson = principal.FindFirstValue("capabilities") ?? "[]";
        var chainJson = principal.FindFirstValue("delegation_chain") ?? "[]";
        var tokenTypeValue = principal.FindFirstValue("token_type") ?? TokenType.Access.ToString();
        var trustLevelValue = principal.FindFirstValue("trust_level") ?? "0";
        var iatClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Iat);
        var expClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);
        var iat = long.TryParse(iatClaim, out var iatSeconds) ? DateTimeOffset.FromUnixTimeSeconds(iatSeconds) : DateTimeOffset.UtcNow;
        var exp = long.TryParse(expClaim, out var expSeconds) ? DateTimeOffset.FromUnixTimeSeconds(expSeconds) : DateTimeOffset.UtcNow;
        return new NasTokenPayload
        {
            Iss = principal.FindFirstValue(JwtRegisteredClaimNames.Iss) ?? GetIssuer(),
            Sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            Iat = iat,
            Exp = exp,
            TokenType = Enum.TryParse<TokenType>(tokenTypeValue, true, out var tokenType) ? tokenType : TokenType.Access,
            TrustLevel = int.TryParse(trustLevelValue, out var trustLevel) ? trustLevel : 0,
            Capabilities = NAbilitySet.FromJson(capabilitiesJson),
            DelegationChain = JsonSerializer.Deserialize<string[]>(chainJson, JsonOptions) ?? [],
            DeviceBinding = principal.FindFirstValue("device_binding"),
            Jti = jti,
        };
    }

    private async Task EnsureSecurityTablesAsync(CancellationToken ct)
    {
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

    public async Task<string> RotateSigningKeyAsync(CancellationToken ct)
    {
        var keyId = $"nas-token-signing-{Guid.CreateVersion7():N}";
        await _keyStore.GetOrCreateSigningKeyAsync(keyId, ct).ConfigureAwait(false);
        await _keyStore.StoreSecretAsync(ActiveSigningKeyName, System.Text.Encoding.UTF8.GetBytes(keyId), ct).ConfigureAwait(false);
        return keyId;
    }

    private async Task<string> GetActiveSigningKeyIdAsync(CancellationToken ct)
    {
        var stored = await _keyStore.GetSecretAsync(ActiveSigningKeyName, ct).ConfigureAwait(false);
        if (stored is not null)
        {
            var value = System.Text.Encoding.UTF8.GetString(stored);
            if (IsSafeKeyId(value)) return value;
        }
        await _keyStore.GetOrCreateSigningKeyAsync(SigningKeyId, ct).ConfigureAwait(false);
        return SigningKeyId;
    }

    private static string ReadKeyId(string token)
    {
        var kid = new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid;
        return IsSafeKeyId(kid) ? kid! : SigningKeyId;
    }

    private static bool IsSafeKeyId(string? keyId) => !string.IsNullOrWhiteSpace(keyId) && keyId.Length <= 128 && keyId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private TimeSpan GetDefaultLifetime()
    {
        var configured = _configuration?.GetValue("security:token:lifetime_minutes");
        return int.TryParse(configured, out var minutes) && minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromHours(1);
    }

    private string GetIssuer() => _configuration?.GetValue("security:issuer") ?? "nas://local";

    private static FortOS.Core.TokenValidationResult Failed(string message, Exception? exception = null, string? jti = null) => new()
    {
        IsValid = false,
        ErrorMessage = exception is null ? message : message,
        Jti = jti,
    };
}
