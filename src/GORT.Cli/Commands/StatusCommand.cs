using System.CommandLine;
using System.Text.Json;
using GORT.Cli.ApiClient;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GORT.Cli.Commands;

/// <summary>Register status command.</summary>
public static class StatusCommand
{
    /// <summary>Create status command.</summary>
    public static Command Create(CliOptions options)
    {
        var watch = new Option<bool>("--watch") { Description = "Continuously refresh system metrics" };
        var interval = new Option<int>("--interval") { Description = "Refresh interval in seconds", DefaultValueFactory = _ => 5 };
        var command = new Command("status", "Show system health and metrics summary") { watch, interval };
        command.SetAction((parse, ct) => CommandRuntime.RunWithClientAsync(parse, options, async (client, token) =>
        {
            do
            {
                using var document = await FetchAsync(client, token).ConfigureAwait(false);
                if (parse.GetValue(watch) && !CommandRuntime.IsJson(parse, options) && !Console.IsOutputRedirected)
                {
                    AnsiConsole.Clear();
                }
                if (CommandRuntime.IsJson(parse, options)) CommandRuntime.PrintJson(document);
                else Render(document);
                if (!parse.GetValue(watch)) break;
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(parse.GetValue(interval), 1, 300)), token).ConfigureAwait(false);
            }
            while (!token.IsCancellationRequested);
            return 0;
        }, ct));
        return command;
    }

    private static async Task<JsonDocument> FetchAsync(GortApiClient client, CancellationToken ct)
    {
        using var health = await client.GetAsync("api/health", ct).ConfigureAwait(false);
        using var metrics = await client.GetAsync("api/metrics/system", ct).ConfigureAwait(false);
        return JsonDocument.Parse(JsonSerializer.Serialize(new { health = health.RootElement, metrics = metrics.RootElement }));
    }

    private static void Render(JsonDocument document)
    {
        var metrics = document.RootElement.GetProperty("metrics");
        var host = metrics.GetProperty("host");
        var cpu = metrics.GetProperty("cpu");
        var memory = metrics.GetProperty("memory");
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow(
            new Panel(new Markup(
                $"[bold]Uptime[/] {FormatDuration(host.GetProperty("uptime"))}\n" +
                $"[bold]Load[/] {Number(host, "loadAverage1"):0.00} / {Number(host, "loadAverage5"):0.00} / {Number(host, "loadAverage15"):0.00}"))
            { Header = new PanelHeader("Host") },
            new Panel(new Markup(
                $"[bold]Usage[/] {Number(cpu, "usagePercent"):0.0}%\n" +
                $"[bold]System[/] {Number(cpu, "systemPercent"):0.0}%  [bold]I/O wait[/] {Number(cpu, "ioWaitPercent"):0.0}%"))
            { Header = new PanelHeader("CPU") });
        grid.AddRow(
            new Panel(new Markup(
                $"[bold]Used[/] {FormatBytes(Number(memory, "usedBytes"))} / {FormatBytes(Number(memory, "totalBytes"))} ({Number(memory, "usedPercent"):0.0}%)\n" +
                $"[bold]Swap[/] {FormatBytes(Number(memory, "swapUsedBytes"))} ({Number(memory, "swapUsedPercent"):0.0}%)"))
            { Header = new PanelHeader("Memory") },
            new Panel(BuildResourceSummary(metrics)) { Header = new PanelHeader("Resources") });
        AnsiConsole.Write(grid);
    }

    private static IRenderable BuildResourceSummary(JsonElement metrics)
    {
        var disks = metrics.GetProperty("disks").EnumerateArray().ToArray();
        var networks = metrics.GetProperty("networks").EnumerateArray().Where(item => item.GetProperty("isUp").GetBoolean()).ToArray();
        var fileSystems = metrics.GetProperty("fileSystems").EnumerateArray().ToArray();
        var alerts = metrics.GetProperty("diagnostics").GetArrayLength();
        var diskRead = disks.Sum(item => Number(item, "readBytesPerSecond"));
        var diskWrite = disks.Sum(item => Number(item, "writeBytesPerSecond"));
        var networkReceive = networks.Sum(item => Number(item, "receiveBytesPerSecond"));
        var networkTransmit = networks.Sum(item => Number(item, "transmitBytesPerSecond"));
        var hottest = disks.Select(item => item.TryGetProperty("temperatureCelsius", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0).DefaultIfEmpty().Max();
        var fullest = fileSystems.Select(item => Number(item, "usedPercent")).DefaultIfEmpty().Max();
        return new Markup(
            $"[bold]Disk I/O[/] R {FormatBytes(diskRead)}/s  W {FormatBytes(diskWrite)}/s\n" +
            $"[bold]Network[/] RX {FormatBytes(networkReceive)}/s  TX {FormatBytes(networkTransmit)}/s\n" +
            $"[bold]Hottest disk[/] {(hottest > 0 ? $"{hottest:0}°C" : "N/A")}  [bold]Fullest FS[/] {fullest:0.0}%\n" +
            $"[bold]Collector diagnostics[/] {alerts}");
    }

    private static double Number(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static string FormatDuration(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String && TimeSpan.TryParse(element.GetString(), out var value))
        {
            return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";
        }
        return element.ToString();
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
