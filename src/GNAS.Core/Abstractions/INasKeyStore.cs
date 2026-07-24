namespace GNAS.Core;

/// <summary>NAS 密钥存储接口。</summary>
public interface INasKeyStore
{
    /// <summary>获取或创建签名密钥。</summary>
    Task<byte[]> GetOrCreateSigningKeyAsync(string keyId, CancellationToken ct);
    /// <summary>签名数据。</summary>
    Task<byte[]> SignDataAsync(string keyId, byte[] data, CancellationToken ct);
    /// <summary>获取或创建审计链密钥。</summary>
    Task<byte[]> GetOrCreateChainKeyAsync(CancellationToken ct);
    /// <summary>计算 HMAC。</summary>
    Task<byte[]> ComputeHmacAsync(string keyId, byte[] data, CancellationToken ct);
    /// <summary>加密数据。</summary>
    Task<byte[]> EncryptAsync(string keyId, byte[] plaintext, CancellationToken ct);
    /// <summary>解密数据。</summary>
    Task<byte[]> DecryptAsync(string keyId, byte[] ciphertext, CancellationToken ct);
    /// <summary>存储秘密。</summary>
    Task StoreSecretAsync(string name, byte[] value, CancellationToken ct);
    /// <summary>读取秘密。</summary>
    Task<byte[]?> GetSecretAsync(string name, CancellationToken ct);
    /// <summary>删除秘密。</summary>
    Task DeleteSecretAsync(string name, CancellationToken ct);
    /// <summary>生成 Agent Secret。</summary>
    Task<string> GenerateAgentSecretAsync(string agentId, CancellationToken ct);
    /// <summary>读取 Agent Secret。</summary>
    Task<string?> GetAgentSecretAsync(string agentId, CancellationToken ct);
}
