using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortOS.Api.Services;

/// <summary>
/// AI 助手服务(P0-1):对接 OpenAI 兼容的 chat/completions 端点(默认本地 ollama),
/// 为移动端 AI 对话入口提供自然语言 → 操作建议/执行的中转。
/// 仅做协议中转与上下文拼装,不内置模型;端点、模型、密钥均可配置。
/// </summary>
public sealed class AiAssistantService(HttpClient http, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>配置键:LLM 端点(OpenAI 兼容,默认本地 ollama)。</summary>
    public const string EndpointKey = "ai:endpoint";
    /// <summary>配置键:模型名。</summary>
    public const string ModelKey = "ai:model";
    /// <summary>配置键:API 密钥(本地 ollama 可为空)。</summary>
    public const string ApiKeyKey = "ai:api_key";
    /// <summary>配置键:AI 对话开关。</summary>
    public const string EnabledKey = "ai:enabled";

    private const string DefaultEndpoint = "http://127.0.0.1:11434/v1";
    private const string DefaultModel = "qwen2.5:7b";

    /// <summary>系统提示词:约束 AI 以 fortOS 管理助手身份回答,给出可执行建议。</summary>
    private const string SystemPrompt =
        "你是 FortOS 个人 NAS 的管理助手(运行在用户的家庭服务器上)。" +
        "你的职责是帮助用户通过自然语言管理 NAS:文件、共享、备份、Agent 容器、系统状态。" +
        "回答要求:1) 简洁、面向行动,优先给出可直接执行的建议;2) 涉及具体操作时说明调用哪个管理功能" +
        "(如:文件在「文件」页、备份在「备份」页、容器在「Agent」页);" +
        "3) 涉及删除、格式化、清空等危险操作时,明确提示需要用户二次确认;" +
        "4) 不要编造 fortOS 不存在的功能。";

    /// <summary>
    /// 是否启用 AI 对话(默认启用;ai:enabled=false 时接口返回明确错误,便于部署方关闭)。
    /// </summary>
    private bool IsEnabled()
        => !string.Equals(configuration[EnabledKey], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>发送对话请求,返回模型回复;流式时经 <paramref name="onDelta"/> 逐段推送。</summary>
    public async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        Action<string>? onDelta = null,
        CancellationToken ct = default)
    {
        if (!IsEnabled())
        {
            return new ChatResponse(null, null, "AI 对话未启用(ai:enabled=false)。");
        }

        var endpoint = configuration[EndpointKey] ?? DefaultEndpoint;
        var model = configuration[ModelKey] ?? DefaultModel;
        var apiKey = configuration[ApiKeyKey];

        var messages = new List<object> { new { role = "system", content = SystemPrompt } };
        if (request.History is not null)
        {
            // 历史消息原样透传(客户端已按 role 分组);最多保留 20 条防止上下文膨胀。
            foreach (var m in request.History.TakeLast(20))
            {
                messages.Add(new { role = m.Role, content = m.Content });
            }
        }

        messages.Add(new { role = "user", content = request.Message });

        var payload = new
        {
            model,
            messages,
            stream = request.Stream,
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint.TrimEnd('/') + "/"), "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new ChatResponse(null, model, $"AI 服务返回 {(int)response.StatusCode}: {Truncate(body)}");
        }

        if (request.Stream)
        {
            return await ReadStreamingAsync(response, model, onDelta, ct).ConfigureAwait(false);
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseNonStreaming(json, model);
    }

    /// <summary>解析非流式响应 `{ choices: [{ message: { content } }] }`。</summary>
    private static ChatResponse ParseNonStreaming(string json, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return new ChatResponse(content, model, null);
        }
        catch (Exception ex)
        {
            return new ChatResponse(null, model, $"AI 响应解析失败:{ex.Message}");
        }
    }

    /// <summary>读取 SSE 流式响应,逐段回调 delta,并拼装完整回复。</summary>
    private static async Task<ChatResponse> ReadStreamingAsync(
        HttpResponseMessage response,
        string model,
        Action<string>? onDelta,
        CancellationToken ct)
    {
        var builder = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                break;
            }

            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString();
                if (string.IsNullOrEmpty(delta))
                {
                    continue;
                }

                builder.Append(delta);
                onDelta?.Invoke(delta);
            }
            catch (JsonException)
            {
                // 忽略无法解析的 SSE 行(部分网关会混入注释/心跳)。
            }
        }

        return new ChatResponse(builder.ToString(), model, null);
    }

    private static string Truncate(string text)
        => text.Length > 200 ? text[..200] + "…" : text;
}

/// <summary>对话消息(对齐 OpenAI chat 协议)。</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>AI 对话请求(服务层契约)。</summary>
public sealed record ChatRequest(string Message, IReadOnlyList<ChatMessage>? History = null, bool Stream = false);

/// <summary>AI 对话响应(非流式)。</summary>
public sealed record ChatResponse(string? Reply, string? Model, string? Error);
