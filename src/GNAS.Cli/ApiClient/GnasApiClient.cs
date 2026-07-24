using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GNAS.Cli.ApiClient;

/// <summary>表示 REST API 调用失败。</summary>
public sealed class GnasApiException : Exception
{
    /// <summary>HTTP 状态码；连接失败时为空。</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>建立 API 例外。</summary>
    public GnasApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null) : base(message, inner) => StatusCode = statusCode;
}

/// <summary>封装 GNAS REST API 与认证重试逻辑。</summary>
public sealed class GnasApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string? _explicitToken;
    private readonly AuthStore _store;
    private string? _currentToken;

    /// <summary>建立 GNAS REST 客户端。</summary>
    public GnasApiClient(string? server = null, string? token = null, HttpClient? httpClient = null)
    {
        _store = AuthStore.Load();
        var baseUrl = server ?? Environment.GetEnvironmentVariable("GNAS_SERVER") ?? _store.Server ?? "http://localhost:5000";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        _explicitToken = token ?? Environment.GetEnvironmentVariable("GNAS_TOKEN");
        _currentToken = _explicitToken ?? _store.Token;
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>当前服务器根地址。</summary>
    public string Server => _http.BaseAddress!.ToString().TrimEnd('/');

    /// <summary>执行 GET 请求并返回 JSON。</summary>
    public Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Get, path, null, cancellationToken);

    /// <summary>执行 POST 请求并返回 JSON。</summary>
    public Task<JsonDocument> PostAsync(string path, object? body = null, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, path, body, cancellationToken);

    /// <summary>执行 PUT 请求并返回 JSON。</summary>
    public Task<JsonDocument> PutAsync(string path, object? body = null, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Put, path, body, cancellationToken);

    /// <summary>执行 DELETE 请求并返回 JSON。</summary>
    public Task<JsonDocument> DeleteAsync(string path, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Delete, path, null, cancellationToken);

    /// <summary>读取 SSE 资料流的 data 行。</summary>
    public async IAsyncEnumerable<string> GetSseStreamAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, path, null);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (IsConnectionFailure(ex))
        {
            throw new GnasApiException($"无法连接到 GNAS 服务器 {Server}，请确认服务已启动。", null, ex);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode) throw await BuildErrorAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) yield return line[5..].TrimStart();
        }
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, path, body, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized && _explicitToken is null && !string.IsNullOrWhiteSpace(_store.Token))
        {
            response.Dispose();
            if (await TryRefreshAsync(cancellationToken).ConfigureAwait(false))
            {
                response = await SendOnceAsync(method, path, body, cancellationToken).ConfigureAwait(false);
            }
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode) throw await BuildErrorAsync(response, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseJsonOrWrap(text);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        try
        {
            return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (IsConnectionFailure(ex))
        {
            throw new GnasApiException($"无法连接到 GNAS 服务器 {Server}，请确认服务已启动。", null, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GnasApiException($"连接 GNAS 服务器 {Server} 超时。", null, ex);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, NormalizePath(path));
        if (!string.IsNullOrWhiteSpace(_currentToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _currentToken);
        if (body is not null)
        {
            var json = body is string s ? s : JsonSerializer.Serialize(body, ApiJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return request;
    }

    private async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Post, "api/auth/refresh", new { token = _store.Token });
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = ParseJsonOrWrap(text);
            var token = FindString(doc.RootElement, "token") ?? FindString(doc.RootElement, "accessToken") ?? FindString(doc.RootElement, "jwt");
            if (string.IsNullOrWhiteSpace(token)) return false;
            _currentToken = token;
            AuthStore.SaveToken(Server, token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path) => path.StartsWith('/') ? path[1..] : path;

    private static JsonDocument ParseJsonOrWrap(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return JsonDocument.Parse("{}");
        try { return JsonDocument.Parse(text); }
        catch { return JsonDocument.Parse(JsonSerializer.Serialize(new { value = text }, ApiJson.Options)); }
    }

    private static async Task<GnasApiException> BuildErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = $"GNAS API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}";
        try
        {
            using var doc = JsonDocument.Parse(text);
            var error = FindString(doc.RootElement, "error") ?? FindString(doc.RootElement, "message");
            var code = FindString(doc.RootElement, "code");
            var traceId = FindString(doc.RootElement, "traceId");
            message = string.Join(' ', new[] { error ?? message, code is null ? null : $"({code})", traceId is null ? null : $"traceId={traceId}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(text)) message += $" - {text}";
        }
        return new GnasApiException(message, response.StatusCode);
    }

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
                var nested = FindString(property.Value, name);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static bool IsConnectionFailure(HttpRequestException ex) => ex.StatusCode is null;

    /// <summary>释放 HTTP 资源。</summary>
    public void Dispose() => _http.Dispose();
}
