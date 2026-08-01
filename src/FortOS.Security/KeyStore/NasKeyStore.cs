using System.Security.Cryptography;
using System.Text;
using FortOS.Core;
using Microsoft.Extensions.Logging;

namespace FortOS.Security.KeyStore;

/// <summary>
/// NAS key store implemented with software encryption fallback.
/// </summary>
public sealed class NasKeyStore : INasKeyStore, IMasterKeyRotationService
{
    private const string DefaultDataRoot = "/srv/nas";
    private readonly string _keyStoreDirectory;
    private readonly ILogger<NasKeyStore>? _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private byte[]? _masterKey;

    /// <summary>
    /// Initialize the NAS key store.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public NasKeyStore(ILogger<NasKeyStore>? logger = null)
    {
        _logger = logger;
        var root = Environment.GetEnvironmentVariable("FortOS_DATA_ROOT");
        root = string.IsNullOrWhiteSpace(root) ? DefaultDataRoot : root;
        _keyStoreDirectory = Path.GetFullPath(Path.Combine(root, "keystore"));
        if (File.Exists("/dev/tpm0") || File.Exists("/dev/tpmrm0"))
        {
            _logger?.LogInformation("TPM device detected; current version records integration point and uses software key store fallback.");
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GetOrCreateSigningKeyAsync(string keyId, CancellationToken ct)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await ReadSecretCoreAsync($"signing-{keyId}", ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            using var rsa = RSA.Create(3072);
            var key = rsa.ExportPkcs8PrivateKey();
            await WriteSecretCoreAsync($"signing-{keyId}", key, ct).ConfigureAwait(false);
            return key;
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> SignDataAsync(string keyId, byte[] data, CancellationToken ct)
    {
        var key = await GetOrCreateSigningKeyAsync(keyId, ct).ConfigureAwait(false);
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(key, out _);
        return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <inheritdoc />
    public async Task<byte[]> GetOrCreateChainKeyAsync(CancellationToken ct)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await ReadSecretCoreAsync("audit-chain", ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var key = RandomNumberGenerator.GetBytes(32);
            await WriteSecretCoreAsync("audit-chain", key, ct).ConfigureAwait(false);
            return key;
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ComputeHmacAsync(string keyId, byte[] data, CancellationToken ct)
    {
        var key = keyId == "audit-chain" ? await GetOrCreateChainKeyAsync(ct).ConfigureAwait(false) : await GetOrCreateNamedKeyAsync($"hmac-{keyId}", ct).ConfigureAwait(false);
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    /// <inheritdoc />
    public async Task<byte[]> EncryptAsync(string keyId, byte[] plaintext, CancellationToken ct)
    {
        var masterKey = await GetMasterKeyAsync(ct).ConfigureAwait(false);
        return EncryptWithMaster(masterKey, plaintext);
    }

    /// <inheritdoc />
    public async Task<byte[]> DecryptAsync(string keyId, byte[] ciphertext, CancellationToken ct)
    {
        var masterKey = await GetMasterKeyAsync(ct).ConfigureAwait(false);
        return DecryptWithMaster(masterKey, ciphertext);
    }

    /// <inheritdoc />
    public async Task StoreSecretAsync(string name, byte[] value, CancellationToken ct)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteSecretCoreAsync(name, value, ct).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetSecretAsync(string name, CancellationToken ct)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadSecretCoreAsync(name, ct).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string name, CancellationToken ct)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = GetSecretPath(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <summary>Atomically re-encrypts all keystore entries under a newly generated master key.</summary>
    public async Task RotateMasterKeyAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FortOS_MASTER_KEY")))
            throw new ConfigurationException("Cannot rotate master key while FortOS_MASTER_KEY environment variable is set.");
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var oldKey = await GetMasterKeyCoreAsync(ct).ConfigureAwait(false);
            var newKey = RandomNumberGenerator.GetBytes(32);
            var masterPath = Path.Combine(_keyStoreDirectory, "master.key");
            var masterBackup = masterPath + ".rotate-bak-" + Guid.CreateVersion7().ToString("N");
            // (Target, Temporary, Plaintext) of every entry staged for the new key; the
            // temporary path is needed to commit the swap and the plaintext to roll back.
            var staged = new List<(string Target, string Temporary, byte[] Plaintext)>();
            var stagedTemps = new List<string>();
            var masterSwapped = false;
            try
            {
                // Phase 1 — stage: re-encrypt every entry under the new key into temp files,
                // leaving all originals untouched so a failure here is trivially harmless.
                foreach (var path in Directory.EnumerateFiles(_keyStoreDirectory, "*.key"))
                {
                    if (string.Equals(Path.GetFileName(path), "master.key", StringComparison.OrdinalIgnoreCase)) continue;
                    var plaintext = DecryptWithMaster(oldKey, await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
                    var temporary = path + ".rotate-" + Guid.CreateVersion7().ToString("N");
                    await File.WriteAllBytesAsync(temporary, EncryptWithMaster(newKey, plaintext), ct).ConfigureAwait(false);
                    SetFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    staged.Add((path, temporary, plaintext));
                    stagedTemps.Add(temporary);
                }

                var masterTemporary = masterPath + ".rotate-" + Guid.CreateVersion7().ToString("N");
                await File.WriteAllTextAsync(masterTemporary, Convert.ToBase64String(newKey), ct).ConfigureAwait(false);
                SetFileMode(masterTemporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stagedTemps.Add(masterTemporary);

                // Phase 2 — commit window: keep a copy of the old master key so that a crash
                // or exception between the entry swaps and the master swap is recoverable.
                File.Copy(masterPath, masterBackup, overwrite: true);
                foreach (var (target, temporary, _) in staged)
                {
                    File.Move(temporary, target, overwrite: true);
                }

                File.Move(masterTemporary, masterPath, overwrite: true);
                // Update the in-memory cache BEFORE the (best-effort) backup cleanup: the
                // on-disk state is now fully consistent under the new key, so the cache must
                // match even if deleting the backup throws below.
                _masterKey = newKey;
                masterSwapped = true;
                try
                {
                    // Backup cleanup is post-commit housekeeping; a failure here means the
                    // rotation already succeeded, so it must not be reported as a failure.
                    File.Delete(masterBackup);
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogWarning(cleanupEx, "Master key rotation committed but the old-key backup could not be removed: {Backup}", masterBackup);
                }
            }
            catch
            {
                // Roll back to a consistent, decryptable state: if the new master key was
                // never committed, re-encrypt the swapped entries under the OLD key so the
                // store remains fully readable. If the master key was already swapped, the
                // rotation actually succeeded and only cleanup is needed.
                try
                {
                    if (!masterSwapped)
                    {
                        foreach (var (target, _, plaintext) in staged)
                        {
                            await File.WriteAllBytesAsync(target, EncryptWithMaster(oldKey, plaintext), CancellationToken.None).ConfigureAwait(false);
                        }

                        if (File.Exists(masterBackup)) File.Delete(masterBackup);
                    }
                    else if (File.Exists(masterBackup))
                    {
                        File.Delete(masterBackup);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogError(cleanupEx, "Failed to fully roll back master key rotation; keystore may need manual recovery.");
                }

                foreach (var temporary in stagedTemps)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* best effort */ }
                }

                throw;
            }
        }
        finally { _sync.Release(); }
    }

    /// <inheritdoc />
    public async Task<string> GenerateAgentSecretAsync(string agentId, CancellationToken ct)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(key);
        await StoreSecretAsync($"agent-{agentId}", Encoding.UTF8.GetBytes(secret), ct).ConfigureAwait(false);
        return secret;
    }

    /// <inheritdoc />
    public async Task<string?> GetAgentSecretAsync(string agentId, CancellationToken ct)
    {
        var value = await GetSecretAsync($"agent-{agentId}", ct).ConfigureAwait(false);
        return value is null ? null : Encoding.UTF8.GetString(value);
    }

    private async Task<byte[]> GetOrCreateNamedKeyAsync(string name, CancellationToken ct)
    {
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existing = await ReadSecretCoreAsync(name, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var key = RandomNumberGenerator.GetBytes(32);
            await WriteSecretCoreAsync(name, key, ct).ConfigureAwait(false);
            return key;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<byte[]> GetMasterKeyAsync(CancellationToken ct)
    {
        if (_masterKey is not null)
        {
            return _masterKey;
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await GetMasterKeyCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task WriteSecretCoreAsync(string name, byte[] value, CancellationToken ct)
    {
        EnsureDirectory();
        var encrypted = EncryptWithMaster(await GetMasterKeyCoreAsync(ct).ConfigureAwait(false), value);
        var path = GetSecretPath(name);
        await File.WriteAllBytesAsync(path, encrypted, ct).ConfigureAwait(false);
        SetFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private async Task<byte[]?> ReadSecretCoreAsync(string name, CancellationToken ct)
    {
        EnsureDirectory();
        var path = GetSecretPath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return DecryptWithMaster(await GetMasterKeyCoreAsync(ct).ConfigureAwait(false), encrypted);
    }

    private async Task<byte[]> GetMasterKeyCoreAsync(CancellationToken ct)
    {
        if (_masterKey is not null)
        {
            return _masterKey;
        }

        EnsureDirectory();
        var env = Environment.GetEnvironmentVariable("FortOS_MASTER_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var key = Convert.FromBase64String(env);
            if (key.Length != 32)
            {
                throw new ConfigurationException("FortOS_MASTER_KEY must be a base64-encoded 32-byte key.");
            }

            _masterKey = key;
            return key;
        }

        var path = Path.Combine(_keyStoreDirectory, "master.key");
        if (File.Exists(path))
        {
            _masterKey = Convert.FromBase64String(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
            return _masterKey;
        }

        _masterKey = RandomNumberGenerator.GetBytes(32);
        await File.WriteAllTextAsync(path, Convert.ToBase64String(_masterKey), ct).ConfigureAwait(false);
        SetFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return _masterKey;
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_keyStoreDirectory);
        SetDirectoryMode(_keyStoreDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private string GetSecretPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid key name.", nameof(name));
        }

        return Path.Combine(_keyStoreDirectory, name + ".key");
    }

    private static byte[] EncryptWithMaster(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. ciphertext, .. tag];
    }

    private static byte[] DecryptWithMaster(byte[] key, byte[] value)
    {
        if (value.Length < 28)
        {
            throw new CryptographicException("Invalid ciphertext format.");
        }

        var nonce = value[..12];
        var tag = value[^16..];
        var ciphertext = value[12..^16];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static void SetDirectoryMode(string path, UnixFileMode mode)
    {
        // Linux-only API; on Windows (local dev, CI) tests exercise the key store logic
        // without POSIX permissions — the deploy target is always Linux.
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    private static void SetFileMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, mode);
        }
    }
}
