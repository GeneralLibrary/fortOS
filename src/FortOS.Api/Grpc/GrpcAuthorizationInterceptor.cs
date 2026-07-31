using Grpc.Core;
using Grpc.Core.Interceptors;
using FortOS.Security.Models;

namespace FortOS.Api.Grpc;

/// <summary>Single deny-by-default authorization boundary for all gRPC services.</summary>
public sealed class GrpcAuthorizationInterceptor : Interceptor
{
    private static void Authorize(ServerCallContext context)
    {
        var payload = context.GetHttpContext().Items["NasTokenPayload"] as NasTokenPayload;
        if (payload is null) throw new RpcException(new Status(StatusCode.Unauthenticated, "NAS token is required."));
        if (!payload.Capabilities.Satisfies("admin:**")) throw new RpcException(new Status(StatusCode.PermissionDenied, "gRPC requires admin capability."));
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    { Authorize(context); return continuation(request, context); }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    { Authorize(context); return continuation(request, responseStream, context); }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    { Authorize(context); return continuation(requestStream, context); }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    { Authorize(context); return continuation(requestStream, responseStream, context); }
}
