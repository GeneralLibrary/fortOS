using System.CommandLine;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Commands;

/// <summary>注册状态命令。</summary>
public static class StatusCommand
{
    /// <summary>建立 status 命令。</summary>
    public static Command Create(CliOptions options)
    {
        var command = new Command("status", "显示系统健康与指标摘要");
        command.SetAction(async (parse, ct) => await CommandRuntime.RunAsync(parse, options, async (client, token) =>
        {
            using var health = await client.GetAsync("api/health", token);
            JsonDocument metrics;
            try
            {
                metrics = await client.GetAsync("api/metrics/current", token);
            }
            catch (GnasApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                metrics = await client.GetAsync("api/metrics", token);
            }
            return JsonDocument.Parse(JsonSerializer.Serialize(new { health = health.RootElement, metrics = metrics.RootElement }));
        }, doc =>
        {
            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow(
                new Panel(new Text(doc.RootElement.GetProperty("health").ToString())) { Header = new PanelHeader("健康") },
                new Panel(new Text(doc.RootElement.GetProperty("metrics").ToString())) { Header = new PanelHeader("指标") });
            AnsiConsole.Write(grid);
        }, ct));
        return command;
    }
}
