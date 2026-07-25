using System.Security.Cryptography;
using System.Text;
using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Security.KeyStore;

/// <summary>
/// 使用软件加密回退实现的 NAS 密钥存储。
/// </summary>
public sealed class NasKeyStore : INasKeyStore, IMasterKeyRotationService
{
    private const string DefaultDataRoot = "/srv/nas";
    private readonly string _keyStoreDirectory;
    private readonly ILogger<NasKeyStore>? _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private byte[]? _masterKey;

    /// <summary>
    /// 初始化 NAS 密钥存储。
    /// </summary>
    /// <param name="logger">可选日志记录器。</param>
    public NasKeyStore(ILogger<NasKeyStore>? logger = null)
    {
        _logger = logger;
        var root = Environment.GetEnvironmentVariable("GNAS_DATA_ROOT");
        root = string.IsNullOrWhiteSpace(root) ? DefaultDataRoot : root;
        _keyStoreDirectory = Path.GetFullPath(Path.Combine(root, "keystore"));
        if (File.Exists("/dev/tpm0") || File.Exists("/dev/tpmrm0"))
        {
            _logger?.LogInformation("检测到 TPM 设备；当前版本记录集成点并使用软件密钥存储回退。");
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
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GNAS_MASTER_KEY")))
            throw new ConfigurationException("????? GNAS_MASTER_KEY ?????????");
        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var oldKey = await GetMasterKeyCoreAsync(ct).ConfigureAwait(false);
            var replacements = new List<(string Target, string Temporary)>();
            try
            {
                var newKey = RandomNumberGenerator.GetBytes(32);
                foreach (var path in Directory.EnumerateFiles(_keyStoreDirectory, "*.key"))
                {
                    if (string.Equals(Path.GetFileName(path), "master.key", StringComparison.OrdinalIgnoreCase)) continue;
                    var plaintext = DecryptWithMaster(oldKey, await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
                    var temporary = path + ".rotate-" + Guid.CreateVersion7().ToString("N");
                    await File.WriteAllBytesAsync(temporary, EncryptWithMaster(newKey, plaintext), ct).ConfigureAwait(false);
                    SetFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    replacements.Add((path, temporary));
                }
                var masterPath = Path.Combine(_keyStoreDirectory, "master.key");
                var masterTemporary = masterPath + ".rotate-" + Guid.CreateVersion7().ToString("N");
                await File.WriteAllTextAsync(masterTemporary, Convert.ToBase64String(newKey), ct).ConfigureAwait(false);
                SetFileMode(masterTemporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                foreach (var replacement in replacements) File.Move(replacement.Temporary, replacement.Target, overwrite: true);
                File.Move(masterTemporary, masterPath, overwrite: true);
                _masterKey = newKey;
            }
            catch
            {
                foreach (var replacement in replacements)
                    if (File.Exists(replacement.Temporary)) File.Delete(replacement.Temporary);
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
        var env = Environment.GetEnvironmentVariable("GNAS_MASTER_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var key = Convert.FromBase64String(env);
            if (key.Length != 32)
            {
                throw new ConfigurationException("GNAS_MASTER_KEY 必须是 base64 编码的 32 字节密钥。");
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
            throw new ArgumentException("密钥名称无效。", nameof(name));
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
            throw new CryptographicException("密文格式无效。");
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
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    private static void SetFileMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }
}
