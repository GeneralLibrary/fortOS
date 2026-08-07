using System.Threading.Channels;
using FortOS.Core;
using FortOS.Observability.Logging.Stages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortOS.Observability.Logging;

/// <summary>Bounded-channel log pipeline: an independent raw-text parse stage followed by four stages (enrich, classify, filter, dispatch).</summary>
public sealed class LogPipeline : ILogPipeline, IHostedService, IAsyncDisposable
{
    private readonly Channel<LogEntry> _channel;
    private readonly ParseStage _parseStage;
    private readonly IReadOnlyList<ILogStage> _stages;
    private readonly ILogger<LogPipeline>? _logger;
    private CancellationTokenSource _shutdown = new();
    private readonly object _consumerLock = new();
    private Task? _consumer;
    private int _disposed;

    /// <summary>Initialize log pipeline.</summary>
    public LogPipeline(IEnumerable<ILogStore> stores, IFortOSConfiguration? configuration = null, IAuditChain? auditChain = null, ILogger<LogPipeline>? logger = null)
    {
        _logger = logger;
        _parseStage = new ParseStage();
        _stages = new ILogStage[]
        {
            new EnrichStage(),
            new ClassifyStage(),
            new FilterStage(configuration),
            new DispatchStage(stores, auditChain)
        };
        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Single-reader channel: the lock prevents concurrent first calls from starting the consumer twice.
        lock (_consumerLock)
        {
            // On host restart (StartAsync after StopAsync) the cancellation source has already fired; it must be
            // rebuilt, otherwise the new consumer would exit immediately because of the canceled token.
            if (_shutdown.IsCancellationRequested)
            {
                _shutdown.Dispose();
                _shutdown = new CancellationTokenSource();
            }

            // If the consumer has exited (the loop ended due to a stage exception), it must be rebuilt —
            // otherwise nobody consumes the channel and, once full, every log writer blocks forever.
            if (_consumer is null || _consumer.IsCompleted)
            {
                _consumer = Task.Run(() => ConsumeAsync(_shutdown.Token), CancellationToken.None);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_consumer is not null)
        {
            await Task.WhenAny(_consumer, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ProcessAsync(LogEntry entry, CancellationToken ct)
    {
        if (_consumer is null)
        {
            await StartAsync(ct).ConfigureAwait(false);
        }

        await _channel.Writer.WriteAsync(entry, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ProcessRawAsync(string rawText, LogCategory category, string sourceComponent, CancellationToken ct)
    {
        var entry = await _parseStage.ProcessRawAsync(rawText, category, sourceComponent, ct).ConfigureAwait(false);
        await ProcessAsync(entry, ct).ConfigureAwait(false);
    }

    /// <summary>Attempt non-blocking log write.</summary>
    public bool TryEnqueue(LogEntry entry) => _channel.Writer.TryWrite(entry);

    private async Task ConsumeAsync(CancellationToken ct)
    {
        // The outer loop ensures the consumer never exits permanently because of a single stage's exception/timeout:
        // if the consumer dies silently nobody reads the channel and, when full, all log writers (including Serilog)
        // block forever, silently halting the whole system's logging.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var entry in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await ProcessThroughStagesAsync(entry, ct).ConfigureAwait(false);
                }

                // Reaching this point means the channel was closed normally (StopAsync).
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal shutdown.
                return;
            }
            catch (OperationCanceledException ex)
            {
                // Cancellation not caused by shutdown (defensive: an internal stage timeout leaked out): log and restart the consumer loop.
                _logger?.LogError(ex, "Log pipeline consumer aborted unexpectedly; restarting consumer loop.");
            }
        }
    }

    private async Task ProcessThroughStagesAsync(LogEntry entry, CancellationToken ct)
    {
        LogEntry? current = entry;
        foreach (var stage in _stages)
        {
            if (current is null)
            {
                return;
            }

            try
            {
                current = await stage.ProcessAsync(current, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // No stage failure (including Loki push's internal 3s timeout) may kill the consumer:
                // log and skip the entry; the consumer keeps consuming.
                _logger?.LogError(ex, "Log pipeline stage processing failed; dropping the log entry.");
                return;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _channel.Writer.TryComplete();
            await _shutdown.CancelAsync().ConfigureAwait(false);
            if (_consumer is not null)
            {
                await Task.WhenAny(_consumer, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            }

            _shutdown.Dispose();
        }
    }
}
