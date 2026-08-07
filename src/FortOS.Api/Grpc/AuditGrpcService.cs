using Google.Protobuf;
using Grpc.Core;
using FortOS.Core;
using FortOS.Observability.Logging;
using static FortOS.Api.Grpc.GrpcMappings;
using Proto = FortOS.Proto;
using ProtoLogEntry = FortOS.Proto.LogEntry;

namespace FortOS.Api.Grpc;

/// <summary>Audit gRPC service.</summary>
public sealed class AuditGrpcService : Proto.AuditService.AuditServiceBase
{
    private readonly MemoryLogStore logs;
    private readonly IAuditChain chain;

    /// <summary>Initializes the audit gRPC service.</summary>
    public AuditGrpcService(MemoryLogStore logs, IAuditChain chain) { this.logs = logs; this.chain = chain; }

    /// <inheritdoc />
    public override async Task<Proto.LogQueryResponse> QueryLogs(Proto.LogQueryRequest request, ServerCallContext context)
    {
        var entries = await logs.QueryAsync(ToCore(request), context.CancellationToken).ConfigureAwait(false);
        var response = new Proto.LogQueryResponse { TotalCount = entries.Count, HasMore = false };
        response.Entries.AddRange(entries.Select(ToProto));
        return response;
    }

    /// <inheritdoc />
    public override async Task StreamLogs(Proto.LogQueryRequest request, IServerStreamWriter<ProtoLogEntry> responseStream, ServerCallContext context)
    {
        var query = ToCore(request) with { From = DateTimeOffset.UtcNow };
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var entries = await logs.QueryAsync(query, context.CancellationToken).ConfigureAwait(false);
            foreach (var entry in entries.OrderBy(e => e.Timestamp))
            {
                query = query with { From = entry.Timestamp.AddTicks(1) };
                await responseStream.WriteAsync(GrpcMappings.ToProto(entry), context.CancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task ExportAuditChain(Proto.ExportRequest request, IServerStreamWriter<Proto.ExportChunk> responseStream, ServerCallContext context)
    {
        // IAuditChain.ExportAsync exports to a file by date; read it back and stream it in chunks (previously an empty implementation).
        var date = request.DateUnix > 0
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(request.DateUnix).UtcDateTime)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var tempPath = Path.Combine(Path.GetTempPath(), $"fortos-audit-{Guid.CreateVersion7():N}.json");
        try
        {
            await chain.ExportAsync(date, tempPath, context.CancellationToken).ConfigureAwait(false);
            await using var stream = File.OpenRead(tempPath);
            var buffer = new byte[64 * 1024];
            int sequence = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, context.CancellationToken).ConfigureAwait(false)) > 0)
            {
                await responseStream.WriteAsync(new Proto.ExportChunk { Content = ByteString.CopyFrom(buffer, 0, read), Sequence = sequence++, Last = false }, context.CancellationToken).ConfigureAwait(false);
            }

            await responseStream.WriteAsync(new Proto.ExportChunk { Content = ByteString.Empty, Sequence = sequence, Last = true }, context.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
