using FortOS.Core;
using Microsoft.Extensions.Logging;

namespace FortOS.Tests.Integration.Observability;

internal sealed class TestConfiguration : IFortOSConfiguration
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _arrays = new(StringComparer.OrdinalIgnoreCase);

    public TestConfiguration Set(string key, string value)
    {
        _values[key] = value;
        return this;
    }

    public TestConfiguration SetArray(string key, params string[] values)
    {
        _arrays[key] = values;
        _values[key] = string.Join(',', values);
        return this;
    }

    public string? GetValue(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public string[] GetArray(string key) => _arrays.TryGetValue(key, out var value) ? value : [];

    public IReadOnlyDictionary<string, string> GetSection(string key) => _values.Where(pair => pair.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)).ToDictionary();

    public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
}

internal sealed class TestKeyStore : INasKeyStore
{
    private readonly byte[] _chainKey = "0123456789abcdef0123456789abcdef"u8.ToArray();

    public Task<byte[]> GetOrCreateSigningKeyAsync(string keyId, CancellationToken ct) => Task.FromResult(_chainKey);
    public Task<byte[]> SignDataAsync(string keyId, byte[] data, CancellationToken ct) => Task.FromResult(data);
    public Task<byte[]> GetOrCreateChainKeyAsync(CancellationToken ct) => Task.FromResult(_chainKey);
    public Task<byte[]> ComputeHmacAsync(string keyId, byte[] data, CancellationToken ct) => Task.FromResult(data);
    public Task<byte[]> EncryptAsync(string keyId, byte[] plaintext, CancellationToken ct) => Task.FromResult(plaintext);
    public Task<byte[]> DecryptAsync(string keyId, byte[] ciphertext, CancellationToken ct) => Task.FromResult(ciphertext);
    public Task StoreSecretAsync(string name, byte[] value, CancellationToken ct) => Task.CompletedTask;
    public Task<byte[]?> GetSecretAsync(string name, CancellationToken ct) => Task.FromResult<byte[]?>(null);
    public Task DeleteSecretAsync(string name, CancellationToken ct) => Task.CompletedTask;
    public Task<string> GenerateAgentSecretAsync(string agentId, CancellationToken ct) => Task.FromResult(agentId + "-secret");
    public Task<string?> GetAgentSecretAsync(string agentId, CancellationToken ct) => Task.FromResult<string?>(null);
}

internal sealed class TestEventBus : IEventBus
{
    private readonly List<(string Pattern, Func<EventEnvelope, CancellationToken, Task> Handler)> _handlers = [];
    public List<EventEnvelope> Published { get; } = [];

    public Task PublishAsync(EventEnvelope envelope, CancellationToken ct)
    {
        Published.Add(envelope);
        return Task.WhenAll(_handlers.Where(h => Matches(h.Pattern, envelope.Topic)).Select(h => h.Handler(envelope, ct)));
    }

    public Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct)
        => PublishAsync(new EventEnvelope { Topic = topic, Type = type, DataJson = dataJson }, ct);

    public IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler)
    {
        var item = (topicPattern, handler);
        _handlers.Add(item);
        return new DelegateDisposable(() => _handlers.Remove(item));
    }

    private static bool Matches(string pattern, string topic) => pattern is "*" or "**" || topic == pattern || (pattern.EndsWith('*') && topic.StartsWith(pattern.TrimEnd('*')));

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _dispose;
        public DelegateDisposable(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}

internal sealed class TestNotifier : global::FortOS.Observability.Alerts.Notifiers.INotifier
{
    public List<ActiveAlert> Alerts { get; } = [];
    public List<ActiveAlert> ResolvedAlerts { get; } = [];
    public Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct)
    {
        Alerts.Add(alert);
        return Task.CompletedTask;
    }

    public Task NotifyResolvedAsync(ActiveAlert alert, AlertRule rule, MetricData metric, CancellationToken ct)
    {
        ResolvedAlerts.Add(alert);
        return Task.CompletedTask;
    }
}

internal static class ObservabilityTestPaths
{
    public static string CreateDataRoot(string name)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "TestData", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
