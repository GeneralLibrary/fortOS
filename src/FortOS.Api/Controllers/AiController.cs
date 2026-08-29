using System.Text.Json;
using FortOS.Api.Authorization;
using FortOS.Api.Middleware;
using FortOS.Api.Services;
using FortOS.Core;
using FortOS.Security.Models;
using Microsoft.AspNetCore.Mvc;

namespace FortOS.Api.Controllers;

/// <summary>AI 助手控制器(P0-1 手机端 AI 对话入口的服务端)。</summary>
[Route("api/ai")]
public sealed class AiController : FortOSControllerBase
{
    private readonly AiAssistantService _ai;

    /// <summary>初始化。</summary>
    public AiController(AiAssistantService ai) => _ai = ai;

    /// <summary>
    /// AI 对话:自然语言提问,返回模型回复。
    /// stream=true 时以 SSE(text/event-stream)逐段推送回复,便于移动端打字机效果。
    /// </summary>
    [RequiresCapability("ai:chat", NasDataLevel.Personal)]
    [HttpPost("chat")]
    public async Task Chat(
        [FromBody] AiChatRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await ApiProblem.WriteAsync(HttpContext, StatusCodes.Status400BadRequest, "AI_MESSAGE_EMPTY", "消息不能为空。").ConfigureAwait(false);
            return;
        }

        var history = request.History?
            .Where(h => h.Role is "user" or "assistant")
            .Select(h => new ChatMessage(h.Role, h.Content))
            .ToList();
        var aiRequest = new ChatRequest(request.Message, history, request.Stream);
        if (!request.Stream)
        {
            var result = await _ai.ChatAsync(aiRequest, ct: ct).ConfigureAwait(false);
            await HttpContext.Response.WriteAsJsonAsync(new { reply = result.Reply, model = result.Model, error = result.Error }, ct).ConfigureAwait(false);
            return;
        }

        // SSE 流式:每段 delta 以 data: 帧推送,结束时发送 [DONE]。
        HttpContext.Response.Headers.ContentType = "text/event-stream";
        await HttpContext.Response.WriteAsync("data: {\"start\":true}\n\n", ct).ConfigureAwait(false);
        var streamed = await _ai.ChatAsync(
            aiRequest,
            // 复用请求取消令牌:客户端断开时停止推送,避免向已断开连接继续写。
            delta => _ = HttpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { delta })}\n\n", ct),
            ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(streamed.Error))
        {
            await HttpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { error = streamed.Error })}\n\n", ct).ConfigureAwait(false);
        }

        await HttpContext.Response.WriteAsync("data: [DONE]\n\n", ct).ConfigureAwait(false);
    }
}

/// <summary>AI 对话请求体。</summary>
public sealed record AiChatRequest(string Message, IReadOnlyList<AiChatHistoryItem>? History, bool Stream = false);

/// <summary>对话历史条目(role: user / assistant)。</summary>
public sealed record AiChatHistoryItem(string Role, string Content);
