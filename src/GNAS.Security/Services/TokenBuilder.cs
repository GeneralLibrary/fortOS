using GNAS.Core;

namespace GNAS.Security.Services;

/// <summary>
/// NasToken fluent builder.
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
    /// Initialize the token builder.
    /// </summary>
    /// <param name="tokenManager">Token manager.</param>
    public TokenBuilder(ITokenManager tokenManager)
    {
        _tokenManager = tokenManager;
    }

    /// <summary>
    /// Sets the user subject.
    /// </summary>
    /// <param name="username">Username.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder ForUser(string username) => ForSubject($"user:{username}", TokenType.Session);

    /// <summary>
    /// Sets the Agent subject.
    /// </summary>
    /// <param name="agentId">Agent ID.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder ForAgent(string agentId) => ForSubject($"agent:{agentId}", TokenType.Agent);

    /// <summary>
    /// Sets the service subject.
    /// </summary>
    /// <param name="serviceId">Service ID.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder ForService(string serviceId) => ForSubject($"service:{serviceId}", TokenType.Service);

    /// <summary>
    /// Adds a capability.
    /// </summary>
    /// <param name="capability">Capability string.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder WithCapability(string capability)
    {
        _capabilities.Add(capability);
        return this;
    }

    /// <summary>
    /// Sets the trust level.
    /// </summary>
    /// <param name="trustLevel">Trust level.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder WithTrustLevel(int trustLevel)
    {
        _trustLevel = trustLevel;
        return this;
    }

    /// <summary>
    /// Sets the lifetime.
    /// </summary>
    /// <param name="lifetime">Lifetime.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder WithLifetime(TimeSpan lifetime)
    {
        _lifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Adds a delegation source.
    /// </summary>
    /// <param name="principal">Delegation subject.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder WithDelegationFrom(string principal)
    {
        _delegationChain.Add(principal);
        return this;
    }

    /// <summary>
    /// Sets the device binding.
    /// </summary>
    /// <param name="deviceBinding">Device binding.</param>
    /// <returns>Builder.</returns>
    public TokenBuilder WithDeviceBinding(string deviceBinding)
    {
        _deviceBinding = deviceBinding;
        return this;
    }

    /// <summary>
    /// Builds and issues the token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JWT string.</returns>
    public Task<string> BuildAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_subject))
        {
            throw new InvalidOperationException("The token subject must be set first.");
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
