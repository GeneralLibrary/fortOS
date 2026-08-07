using Grpc.Core;
using FortOS.Core;
using FortOS.Modules.Share;
using static FortOS.Api.Grpc.GrpcMappings;
using Proto = FortOS.Proto;

namespace FortOS.Api.Grpc;

/// <summary>Share gRPC service.</summary>
public sealed class ShareGrpcService : Proto.ShareService.ShareServiceBase
{
    private readonly ShareModule shares;
    private readonly IEventBus events;

    /// <summary>Initializes the share gRPC service.</summary>
    public ShareGrpcService(ShareModule shares, IEventBus events) { this.shares = shares; this.events = events; }

    /// <inheritdoc />
    public override async Task<Proto.ShareResult> CreateShare(Proto.CreateShareRequest request, ServerCallContext context)
    {
        var created = await shares.CreateShareAsync(ToCore(request.Share), context.CancellationToken).ConfigureAwait(false);
        return new Proto.ShareResult { Success = true, ShareId = created.ShareId, Message = "created", ErrorCode = Proto.ErrorCode.Ok };
    }

    /// <inheritdoc />
    public override async Task<Proto.ListSharesResponse> ListShares(Proto.ListSharesRequest request, ServerCallContext context)
    {
        var list = await shares.ListSharesAsync(context.CancellationToken).ConfigureAwait(false);
        var response = new Proto.ListSharesResponse { Page = new Proto.PageInfo { TotalCount = list.Count } };
        response.Shares.AddRange(list.Select(ToProto));
        return response;
    }

    /// <inheritdoc />
    public override async Task<Proto.ShareResult> DeleteShare(Proto.DeleteShareRequest request, ServerCallContext context)
    {
        await shares.DeleteShareAsync(request.ShareId, context.CancellationToken).ConfigureAwait(false);
        return new Proto.ShareResult { Success = true, ShareId = request.ShareId, Message = "deleted", ErrorCode = Proto.ErrorCode.Ok };
    }

    /// <inheritdoc />
    public override Task GetConnectedClients(Proto.ClientsRequest request, IServerStreamWriter<Proto.ConnectedClient> responseStream, ServerCallContext context)
        => StreamEventsAsync(events, $"share.{request.ShareId}.client.*", async e =>
        {
            // Parse the real source info from the event payload (if carried); invalid JSON only skips that event, never interrupting the whole stream.
            string clientIp = string.Empty;
            string protocol = "unknown";
            if (!string.IsNullOrWhiteSpace(e.DataJson))
            {
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(e.DataJson);
                    if (document.RootElement.TryGetProperty("clientIp", out var ip)) clientIp = ip.GetString() ?? string.Empty;
                    if (document.RootElement.TryGetProperty("protocol", out var proto)) protocol = proto.GetString() ?? "unknown";
                }
                catch (System.Text.Json.JsonException)
                {
                    // Event payload is not JSON: keep the placeholder values and continue streaming.
                }
            }

            await responseStream.WriteAsync(new Proto.ConnectedClient { ClientId = e.EventId.ToString(), ClientIp = clientIp, Protocol = protocol, ConnectedAtUnix = e.Timestamp.ToUnixTimeSeconds() }, context.CancellationToken).ConfigureAwait(false);
        }, context.CancellationToken);

    private static ShareDefinition ToCore(Proto.ShareDefinition share) => new() { ShareId = share.ShareId, Name = share.Name, Path = share.Path, Description = share.Description, ReadOnly = share.ReadOnly, Protocols = share.Protocols.Select(p => p.ToString().Replace("ShareProtocol", string.Empty).ToLowerInvariant()).ToArray() };
    private static Proto.ShareDefinition ToProto(ShareDefinition share)
    {
        var result = new Proto.ShareDefinition { ShareId = share.ShareId, Name = share.Name, Path = share.Path, Description = share.Description ?? string.Empty, ReadOnly = share.ReadOnly };
        result.Protocols.AddRange(share.Protocols.Select(ToProtoProtocol));
        return result;
    }
    private static Proto.ShareProtocol ToProtoProtocol(string value) => value.ToLowerInvariant() switch { "smb" => Proto.ShareProtocol.Smb, "nfs" => Proto.ShareProtocol.Nfs, "ftp" => Proto.ShareProtocol.Ftp, "sftp" => Proto.ShareProtocol.Sftp, "webdav" => Proto.ShareProtocol.Webdav, _ => Proto.ShareProtocol.Unknown };
}
