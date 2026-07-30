using System.CommandLine;
using System.Net;
using System.Text.Json;
using GORT.Cli.ApiClient;
using Spectre.Console;

namespace GORT.Cli.Commands;

/// <summary>Stores global command options.</summary>
public sealed class CliOptions
{
    /// <summary>Server URL option.</summary>
    public Option<string?> Server { get; } = new("--server") { Description = "GORT server URL" };
    /// <summary>Access token option.</summary>
    public Option<string?> Token { get; } = new("--token") { Description = "******" };
    /// <summary>Output format option.</summary>
    public Option<string> Output { get; } = new("--output") { Description = "Output format: table or json", DefaultValueFactory = _ => "table" };
    /// <summary>Disable color option.</summary>
    public Option<bool> NoColor { get; } = new("--no-color") { Description = "Disable colored output" };
}

/// <summary>Provides common command processing utilities.</summary>
public static class CommandRuntime
{
    /// <summary>Create API client.</summary>
    public static GortApiClient Client(ParseResult result, CliOptions options) => new(result.GetValue(options.Server), result.GetValue(options.Token));

    /// <summary>Determine if JSON output.</summary>
    public static bool IsJson(ParseResult result, CliOptions options) => string.Equals(result.GetValue(options.Output), "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Apply color settings.</summary>
    public static void ApplyConsole(ParseResult result, CliOptions options)
    {
        if (result.GetValue(options.NoColor) || IsJson(result, options)) AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
    }

    /// <summary>Execute API operation with unified error output.</summary>
    public static async Task<int> RunAsync(ParseResult result, CliOptions options, Func<GortApiClient, CancellationToken, Task<JsonDocument>> action, Action<JsonDocument>? render = null, CancellationToken cancellationToken = default)
        => await RunWithClientAsync(result, options, async (client, token) =>
        {
            using var doc = await action(client, token).ConfigureAwait(false);
            if (IsJson(result, options)) PrintJson(doc);
            else (render ?? RenderGeneric)(doc);
            return 0;
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Execute a command that owns a client for multiple requests while preserving the shared
    /// interactive login and one-time retry behavior.
    /// </summary>
    public static async Task<int> RunWithClientAsync(
        ParseResult result,
        CliOptions options,
        Func<GortApiClient, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken = default)
    {
        ApplyConsole(result, options);
        var hasRetriedAfterLogin = false;

    retry:
        try
        {
            using var client = Client(result, options);
            return await action(client, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (GortApiException ex)
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
            Console.Error.WriteLine($"Execution failed: {ex.Message}");
            return 1;
        }
    }

    private static bool ShouldPromptLogin(GortApiException ex, ParseResult result, CliOptions options)
    {
        if (Console.IsInputRedirected)
        {
            return false;
        }

        var explicitToken = result.GetValue(options.Token);
        if (!string.IsNullOrWhiteSpace(explicitToken) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GORT_TOKEN")))
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
        var username = AnsiConsole.Ask<string>("Username:");
        var password = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());

        try
        {
            // Avoid sending stale stored token while calling the anonymous login endpoint.
            using var client = new GortApiClient(result.GetValue(options.Server), string.Empty);
            using var doc = await client.PostAsync("api/auth/login", new { username, password }, cancellationToken).ConfigureAwait(false);
            var token = FindString(doc.RootElement, "token")
                ?? FindString(doc.RootElement, "accessToken")
                ?? FindString(doc.RootElement, "jwt");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.Error.WriteLine("Login failed: server did not return a usable token.");
                return false;
            }

            AuthStore.Save(client.Server, token);
            AnsiConsole.MarkupLine("[green]Login successful, subsequent commands will use this token by default.[/]");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Login failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Execute API operation without JSON response.</summary>
    public static async Task<int> RunMessageAsync(ParseResult result, CliOptions options, Func<GortApiClient, CancellationToken, Task<JsonDocument>> action, string success, CancellationToken cancellationToken = default)
        => await RunAsync(result, options, action, doc => AnsiConsole.MarkupLine($"[green]{Markup.Escape(success)}[/]"), cancellationToken);

    /// <summary>Confirm destructive operation.</summary>
    public static bool RequireConfirm(bool confirm)
    {
        if (confirm) return true;
        Console.Error.WriteLine("This operation is destructive, please append --confirm to proceed.");
        return false;
    }

    /// <summary>Print indented JSON.</summary>
    public static void PrintJson(JsonDocument doc)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) doc.RootElement.WriteTo(writer);
        Console.Out.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>Render unknown shapes as generic table or JSON preview.</summary>
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
        var panel = new Panel(new Text(doc.RootElement.ToString())) { Header = new PanelHeader("GORT") };
        AnsiConsole.Write(panel);
    }

    /// <summary>Render JSON array as table.</summary>
    public static void RenderArrayTable(JsonElement array)
    {
        var items = array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToArray() : [];
        if (items.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]No data[/]");
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

    /// <summary>Render simple key-value table.</summary>
    public static void RenderKeyValues(string title, params (string Key, string Value)[] values)
    {
        var table = new Table().Title(title).RoundedBorder().AddColumn("Item").AddColumn("Value");
        foreach (var (key, value) in values) table.AddRow(Markup.Escape(key), Markup.Escape(value));
        AnsiConsole.Write(table);
    }

    /// <summary>Read property text from object.</summary>
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

    /// <summary>Try to find the first-level data array in JSON.</summary>
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

    /// <summary>Parse k=v parameters.</summary>
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
