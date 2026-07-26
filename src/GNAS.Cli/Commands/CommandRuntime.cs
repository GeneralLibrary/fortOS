using System.CommandLine;
using System.Net;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Commands;

/// <summary>保存全局命令选项。</summary>
public sealed class CliOptions
{
    /// <summary>服务器 URL 选项。</summary>
    public Option<string?> Server { get; } = new("--server") { Description = "GNAS 服务器 URL" };
    /// <summary>访问令牌选项。</summary>
    public Option<string?> Token { get; } = new("--token") { Description = "******" };
    /// <summary>输出格式选项。</summary>
    public Option<string> Output { get; } = new("--output") { Description = "输出格式：table 或 json", DefaultValueFactory = _ => "table" };
    /// <summary>关闭颜色选项。</summary>
    public Option<bool> NoColor { get; } = new("--no-color") { Description = "禁用彩色输出" };
}

/// <summary>提供命令处理共用工具。</summary>
public static class CommandRuntime
{
    /// <summary>建立 API 客户端。</summary>
    public static GnasApiClient Client(ParseResult result, CliOptions options) => new(result.GetValue(options.Server), result.GetValue(options.Token));

    /// <summary>判断是否 JSON 输出。</summary>
    public static bool IsJson(ParseResult result, CliOptions options) => string.Equals(result.GetValue(options.Output), "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>套用颜色设置。</summary>
    public static void ApplyConsole(ParseResult result, CliOptions options)
    {
        if (result.GetValue(options.NoColor) || IsJson(result, options)) AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
    }

    /// <summary>执行 API 操作并统一错误输出。</summary>
    public static async Task<int> RunAsync(ParseResult result, CliOptions options, Func<GnasApiClient, CancellationToken, Task<JsonDocument>> action, Action<JsonDocument>? render = null, CancellationToken cancellationToken = default)
    {
        ApplyConsole(result, options);
        var hasRetriedAfterLogin = false;

    retry:
        try
        {
            using var client = Client(result, options);
            using var doc = await action(client, cancellationToken);
            if (IsJson(result, options)) PrintJson(doc);
            else (render ?? RenderGeneric)(doc);
            return 0;
        }
        catch (GnasApiException ex)
        {
            if (!hasRetriedAfterLogin && ShouldPromptLogin(ex, result, options))
            {
                hasRetriedAfterLogin = true;
                if (await TryInteractiveLoginAsync(result, options, cancellationToken).ConfigureAwait(false))
                {
                    goto retry;
                }
            }
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"执行失败：{ex.Message}");
            return 1;
        }
    }

    private static bool ShouldPromptLogin(GnasApiException ex, ParseResult result, CliOptions options)
    {
        if (Console.IsInputRedirected)
        {
            return false;
        }

        var explicitToken = result.GetValue(options.Token);
        if (!string.IsNullOrWhiteSpace(explicitToken) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GNAS_TOKEN")))
        {
            return false;
        }

        if (ex.StatusCode != HttpStatusCode.Unauthorized)
        {
            return false;
        }

        return true;
    }

    private static async Task<bool> TryInteractiveLoginAsync(ParseResult result, CliOptions options, CancellationToken cancellationToken)
    {
        var username = AnsiConsole.Ask<string>("用户名：");
        var password = AnsiConsole.Prompt(new TextPrompt<string>("密码：").Secret());

        try
        {
            // Avoid sending stale stored token while calling the anonymous login endpoint.
            using var client = new GnasApiClient(result.GetValue(options.Server), string.Empty);
            using var doc = await client.PostAsync("api/auth/login", new { username, password }, cancellationToken).ConfigureAwait(false);
            var token = FindString(doc.RootElement, "token")
                ?? FindString(doc.RootElement, "accessToken")
                ?? FindString(doc.RootElement, "jwt");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.Error.WriteLine("登入失败：服务器未返回可用令牌。");
                return false;
            }

            AuthStore.Save(client.Server, token);
            AnsiConsole.MarkupLine("[green]登入成功，后续命令将默认使用本次令牌。[/]");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"登入失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>执行无需 JSON 响应的 API 操作。</summary>
    public static async Task<int> RunMessageAsync(ParseResult result, CliOptions options, Func<GnasApiClient, CancellationToken, Task<JsonDocument>> action, string success, CancellationToken cancellationToken = default)
        => await RunAsync(result, options, action, doc => AnsiConsole.MarkupLine($"[green]{Markup.Escape(success)}[/]"), cancellationToken);

    /// <summary>确认破坏性操作。</summary>
    public static bool RequireConfirm(bool confirm)
    {
        if (confirm) return true;
        Console.Error.WriteLine("此操作具有破坏性，请追加 --confirm 确认。");
        return false;
    }

    /// <summary>输出缩排 JSON。</summary>
    public static void PrintJson(JsonDocument doc)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) doc.RootElement.WriteTo(writer);
        Console.Out.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>以通用表格或 JSON 预览渲染不确定形状。</summary>
    public static void RenderGeneric(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            RenderArrayTable(doc.RootElement);
            return;
        }
        if (TryFindArray(doc.RootElement, out var array))
        {
            RenderArrayTable(array);
            return;
        }
        var panel = new Panel(new Text(doc.RootElement.ToString())) { Header = new PanelHeader("GNAS") };
        AnsiConsole.Write(panel);
    }

    /// <summary>渲染 JSON 数组为表格。</summary>
    public static void RenderArrayTable(JsonElement array)
    {
        var items = array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToArray() : [];
        if (items.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]无资料[/]");
            return;
        }
        var names = items.Where(i => i.ValueKind == JsonValueKind.Object).SelectMany(i => i.EnumerateObject().Select(p => p.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (names.Length == 0)
        {
            foreach (var item in items) AnsiConsole.WriteLine(item.ToString());
            return;
        }
        var table = new Table().RoundedBorder();
        foreach (var name in names) table.AddColumn(Markup.Escape(name));
        foreach (var item in items)
        {
            table.AddRow(names.Select(n => Markup.Escape(GetPropertyText(item, n))).ToArray());
        }
        AnsiConsole.Write(table);
    }

    /// <summary>渲染简单键值表。</summary>
    public static void RenderKeyValues(string title, params (string Key, string Value)[] values)
    {
        var table = new Table().Title(title).RoundedBorder().AddColumn("项目").AddColumn("值");
        foreach (var (key, value) in values) table.AddRow(Markup.Escape(key), Markup.Escape(value));
        AnsiConsole.Write(table);
    }

    /// <summary>从对象中读取属性文本。</summary>
    public static string GetPropertyText(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object) return item.ToString();
        foreach (var property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.ToString(),
                JsonValueKind.Null => string.Empty,
                _ => property.Value.ToString()
            };
        }
        return string.Empty;
    }

    /// <summary>尝试在 JSON 中找到第一层资料数组。</summary>
    public static bool TryFindArray(JsonElement element, out JsonElement array)
    {
        if (element.ValueKind == JsonValueKind.Array) { array = element; return true; }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array) { array = property.Value; return true; }
            }
        }
        array = default;
        return false;
    }

    /// <summary>解析 k=v 参数。</summary>
    public static Dictionary<string, string> ParsePairs(IEnumerable<string>? values)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            var index = value.IndexOf('=');
            if (index <= 0) continue;
            dict[value[..index]] = value[(index + 1)..];
        }
        return dict;
    }

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindString(property.Value, name);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
