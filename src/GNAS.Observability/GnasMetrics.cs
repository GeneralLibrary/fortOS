using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GNAS.Observability;

/// <summary>进程内指标与 Prometheus 文本快照。Meter 供 OpenTelemetry 订阅，快照供轻量抓取。</summary>
public sealed class GnasMetrics : IDisposable
{
    private readonly Meter _meter = new("GNAS");
    private readonly Counter<long> _httpRequests;
    private readonly Counter<long> _httpErrors;
    private readonly Histogram<double> _httpDuration;
    private readonly Counter<long> _backupSuccess;
    private readonly Counter<long> _backupFailure;
    private readonly ConcurrentDictionary<string, double> _values = new(StringComparer.Ordinal);

    public GnasMetrics()
    {
        _httpRequests = _meter.CreateCounter<long>("gnas_http_requests_total");
        _httpErrors = _meter.CreateCounter<long>("gnas_http_errors_total");
        _httpDuration = _meter.CreateHistogram<double>("gnas_http_request_duration_seconds");
        _backupSuccess = _meter.CreateCounter<long>("gnas_backup_success_total");
        _backupFailure = _meter.CreateCounter<long>("gnas_backup_failure_total");
    }

    public void RecordHttp(string method, int status, double seconds)
    {
        var tags = new TagList { { "method", method }, { "status", status.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        _httpRequests.Add(1, tags);
        _httpDuration.Record(seconds, tags);
        Increment($"gnas_http_requests_total{{method=\"{method}\",status=\"{status}\"}}");
        if (status >= 500) { _httpErrors.Add(1, tags); Increment($"gnas_http_errors_total{{method=\"{method}\",status=\"{status}\"}}"); }
    }

    public void RecordBackup(bool success, double seconds, double freshnessHours = 0)
    {
        if (success) { _backupSuccess.Add(1); Increment("gnas_backup_success_total"); }
        else { _backupFailure.Add(1); Increment("gnas_backup_failure_total"); }
        _values["gnas_backup_duration_seconds"] = seconds;
        _values["gnas_backup_freshness_hours"] = freshnessHours;
    }

    public void RecordAgentHealth(bool healthy) => _values["gnas_agent_health"] = healthy ? 1 : 0;
    public void RecordAgentRestart() => Increment("gnas_agent_restarts_total");
    public void RecordProtocolHealth(string protocol, bool healthy) => _values[$"gnas_protocol_health{{protocol=\"{protocol}\"}}"] = healthy ? 1 : 0;

    public string ExportPrometheus()
        => string.Join('\n', _values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key} {x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")) + "\n";

    private void Increment(string key) => _values.AddOrUpdate(key, 1, (_, value) => value + 1);
    public void Dispose() => _meter.Dispose();
}
