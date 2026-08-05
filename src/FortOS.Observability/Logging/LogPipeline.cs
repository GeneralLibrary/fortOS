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
        // 单读者通道：加锁防止并发首次调用重复启动消费者。
        lock (_consumerLock)
        {
            // 宿主重启（StopAsync 后再 StartAsync）时取消源已触发，必须重建，
            // 否则新消费者立即因已取消的 token 退出。
            if (_shutdown.IsCancellationRequested)
            {
                _shutdown.Dispose();
                _shutdown = new CancellationTokenSource();
            }

            // 消费者若已退出（stage 异常导致循环结束），必须重建 ——
            // 否则 channel 无人消费，写满后全部日志写入方永久阻塞。
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
        // 外层循环保证消费者不会因单个 stage 的异常/超时而永久退出：
        // 消费者静默死亡后 channel 无人读取，写满时所有日志写入方（含 Serilog）
        // 会永久阻塞，整个系统日志静默停摆。
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var entry in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await ProcessThroughStagesAsync(entry, ct).ConfigureAwait(false);
                }

                // 只有 channel 被正常关闭（StopAsync）才会走到这里。
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 正常停机。
                return;
            }
            catch (OperationCanceledException ex)
            {
                // 非停机触发的取消（防御：stage 内部超时漏出）：记录并重启消费循环。
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
                // 任何 stage 失败（含 Loki 推送的内部 3s 超时）都不能杀死消费者：
                // 记录并跳过该条日志，消费者继续消费。
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
