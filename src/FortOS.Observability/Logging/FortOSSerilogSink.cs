using FortOS.Core;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace FortOS.Observability.Logging;

/// <summary>Sink that routes Serilog events into the FortOS log pipeline.</summary>
public sealed class FortOSSerilogSink : ILogEventSink
{
    private readonly ILogPipeline _pipeline;
    private readonly ILogger<FortOSSerilogSink>? _logger;
    private long _dropped;

    /// <summary>Initialize Serilog sink.</summary>
    public FortOSSerilogSink(ILogPipeline pipeline, ILogger<FortOSSerilogSink>? logger = null)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        var properties = logEvent.Properties.ToDictionary(pair => pair.Key, pair => (object)pair.Value.ToString().Trim('"'));
        properties.TryGetValue("SourceContext", out var sourceContext);
        var entry = new LogEntry
        {
            Category = LogCategory.System,
            Level = MapLevel(logEvent.Level),
            SourceComponent = sourceContext?.ToString() ?? "Serilog",
            SourceLayer = "Host",
            Message = logEvent.RenderMessage(),
            Template = logEvent.MessageTemplate.Text,
            Properties = properties,
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
            SpanId = System.Diagnostics.Activity.Current?.SpanId.ToString(),
            Timestamp = logEvent.Timestamp
        };

        if (_pipeline is LogPipeline concrete && concrete.TryEnqueue(entry))
        {
            return;
        }

        // 管道饱和（消费者慢/挂起）时的有界溢出策略：丢弃并计数。绝不逐条
        // spawn Task 重试 —— 那会在线程池中堆积无界阻塞任务（Task 风暴），
        // 放大内存与调度压力，最终拖垮整个宿主。
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 1000 == 0)
        {
            // 按数量阈值限频记录，避免「每丢一条打一条日志」反而加剧拥塞。
            _logger?.LogWarning("Log pipeline is saturated; {Dropped} log entries have been dropped so far.", dropped);
        }
    }

    private static LogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Critical,
        _ => LogLevel.Information
    };
}
