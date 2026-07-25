using GNAS.Core;

namespace GNAS.Security.Services;

/// <summary>
/// NasToken 流式构建器。
/// </summary>
public sealed class TokenBuilder
{
    private readonly ITokenManager _tokenManager;
    private readonly List<string> _capabilities = [];
    private readonly List<string> _delegationChain = [];
    private string? _subject;
    private TokenType _tokenType = TokenType.Access;
    private int _trustLevel = 1;
    private TimeSpan _lifetime = TimeSpan.FromHours(1);
    private string? _deviceBinding;

    /// <summary>
    /// 初始化令牌构建器。
    /// </summary>
    /// <param name="tokenManager">令牌管理器。</param>
    public TokenBuilder(ITokenManager tokenManager)
    {
        _tokenManager = tokenManager;
    }

    /// <summary>
    /// 设置用户主体。
    /// </summary>
    /// <param name="username">用户名。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder ForUser(string username) => ForSubject($"user:{username}", TokenType.Session);

    /// <summary>
    /// 设置 Agent 主体。
    /// </summary>
    /// <param name="agentId">Agent 标识。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder ForAgent(string agentId) => ForSubject($"agent:{agentId}", TokenType.Agent);

    /// <summary>
    /// 设置服务主体。
    /// </summary>
    /// <param name="serviceId">服务标识。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder ForService(string serviceId) => ForSubject($"service:{serviceId}", TokenType.Service);

    /// <summary>
    /// 添加能力。
    /// </summary>
    /// <param name="capability">能力字符串。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder WithCapability(string capability)
    {
        _capabilities.Add(capability);
        return this;
    }

    /// <summary>
    /// 设置信任级别。
    /// </summary>
    /// <param name="trustLevel">信任级别。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder WithTrustLevel(int trustLevel)
    {
        _trustLevel = trustLevel;
        return this;
    }

    /// <summary>
    /// 设置有效期。
    /// </summary>
    /// <param name="lifetime">有效期。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder WithLifetime(TimeSpan lifetime)
    {
        _lifetime = lifetime;
        return this;
    }

    /// <summary>
    /// 添加委托来源。
    /// </summary>
    /// <param name="principal">委托主体。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder WithDelegationFrom(string principal)
    {
        _delegationChain.Add(principal);
        return this;
    }

    /// <summary>
    /// 设置设备绑定。
    /// </summary>
    /// <param name="deviceBinding">设备绑定。</param>
    /// <returns>构建器。</returns>
    public TokenBuilder WithDeviceBinding(string deviceBinding)
    {
        _deviceBinding = deviceBinding;
        return this;
    }

    /// <summary>
    /// 构建并签发令牌。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>JWT 字符串。</returns>
    public Task<string> BuildAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_subject))
        {
            throw new InvalidOperationException("必须先设置令牌主体。");
        }

        return _tokenManager.IssueTokenAsync(_subject, _tokenType, _capabilities, _trustLevel, _lifetime, _delegationChain, _deviceBinding, ct);
    }

    private TokenBuilder ForSubject(string subject, TokenType tokenType)
    {
        _subject = subject;
        _tokenType = tokenType;
        return this;
    }
}
