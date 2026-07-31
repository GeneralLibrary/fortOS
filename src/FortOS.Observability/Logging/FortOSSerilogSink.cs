using FortOS.Core;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace FortOS.Observability.Logging;

/// <summary>Sink that routes Serilog events into the FortOS log pipeline.</summary>
public sealed class FortOSSerilogSink : ILogEventSink
{
    private readonly ILogPipeline _pipeline;

    /// <summary>Initialize Serilog sink.</summary>
    public FortOSSerilogSink(ILogPipeline pipeline)
    {
        _pipeline = pipeline;
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

        _ = Task.Run(() => _pipeline.ProcessAsync(entry, CancellationToken.None));
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
