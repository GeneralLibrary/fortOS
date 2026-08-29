using System.Net;
using System.Text;
using System.Text.Json;
using FortOS.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortOS.Tests.Integration.Api;

/// <summary>
/// AI assistant service tests: endpoint selection, payload shape, non-streaming parse,
/// streaming delta aggregation, and the enable/disable switch. Uses a stubbed
/// HttpMessageHandler so no real model server is contacted.
/// </summary>
public sealed class AiAssistantServiceTests
{
    [Fact]
    public async Task Chat_Disabled_ReturnsExplicitError()
    {
        var service = CreateService(enabled: false);

        var result = await service.ChatAsync(new ChatRequest("hello"));

        Assert.Null(result.Reply);
        Assert.Contains("未启用", result.Error);
    }

    [Fact]
    public async Task Chat_NonStreaming_ParsesReply()
    {
        var (service, handler) = CreateServiceWithHandler(
            StubResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"你好,我是 AI 助手。"}}]}"""));

        var result = await service.ChatAsync(new ChatRequest("帮我看看磁盘"));

        Assert.Equal("你好,我是 AI 助手。", result.Reply);
        Assert.Null(result.Error);
        // 请求体包含系统提示词与用户消息,端点指向默认本地 ollama。
        Assert.Contains("/v1/chat/completions", handler.LastRequestUrl);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Contains("帮我看看磁盘", messages.EnumerateArray().Last().GetProperty("content").GetString());
        Assert.Equal("qwen2.5:7b", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Chat_Streaming_AggregatesDeltas()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"你"}}]}

            data: {"choices":[{"delta":{"content":"好"}}]}

            data: [DONE]

            """;
        var (service, _) = CreateServiceWithHandler(StubResponse(HttpStatusCode.OK, sse, "text/event-stream"));
        var deltas = new List<string>();

        var result = await service.ChatAsync(new ChatRequest("hi", Stream: true), onDelta: deltas.Add);

        Assert.Equal("你好", result.Reply);
        Assert.Equal(["你", "好"], deltas);
    }

    [Fact]
    public async Task Chat_UpstreamError_ReturnsError()
    {
        var (service, _) = CreateServiceWithHandler(StubResponse(HttpStatusCode.BadGateway, "bad gateway"));

        var result = await service.ChatAsync(new ChatRequest("hi"));

        Assert.Null(result.Reply);
        Assert.Contains("502", result.Error);
    }

    [Fact]
    public async Task Chat_EndpointModelFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AiAssistantService.EndpointKey] = "http://192.168.1.50:11434/v1",
                [AiAssistantService.ModelKey] = "deepseek-r1:8b",
            })
            .Build();
        var (service, handler) = CreateServiceWithHandler(
            StubResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}"""),
            config);

        await service.ChatAsync(new ChatRequest("hi"));

        Assert.StartsWith("http://192.168.1.50:11434/v1/chat/completions", handler.LastRequestUrl);
        Assert.Contains("deepseek-r1:8b", handler.LastRequestBody);
    }

    private static AiAssistantService CreateService(bool enabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AiAssistantService.EnabledKey] = enabled ? "true" : "false",
            })
            .Build();
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        return new AiAssistantService(http, config);
    }

    private static (AiAssistantService Service, StubHandler Handler) CreateServiceWithHandler(
        HttpResponseMessage response,
        IConfiguration? config = null)
    {
        var handler = new StubHandler(_ => response);
        var cfg = config ?? new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var http = new HttpClient(handler);
        return (new AiAssistantService(http, cfg), handler);
    }

    private static HttpResponseMessage StubResponse(HttpStatusCode status, string body, string contentType = "application/json")
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string? LastRequestUrl { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUrl = request.RequestUri?.ToString();
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
