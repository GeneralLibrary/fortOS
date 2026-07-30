using System.Text.Json;
using GORT.Core;

namespace GORT.Observability.Logging.Stages;

/// <summary>Raw log parsing stage.</summary>
public sealed class ParseStage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Parse raw text into a log entry.</summary>
    public Task<LogEntry> ProcessRawAsync(string rawText, LogCategory category, string sourceComponent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(rawText))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<LogEntry>(rawText, JsonOptions);
                if (parsed is not null)
                {
                    return Task.FromResult(parsed);
                }
            }
            catch (JsonException)
            {
            }
        }

        return Task.FromResult(new LogEntry
        {
            Category = category,
            Level = Microsoft.Extensions.Logging.LogLevel.Information,
            SourceComponent = sourceComponent,
            Message = rawText
        });
    }
}
