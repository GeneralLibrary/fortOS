using System.CommandLine;
using System.Text.Json;
using GNAS.Cli.ApiClient;
using Spectre.Console;

namespace GNAS.Cli.Commands;

/// <summary>Register status command.</summary>
public static class StatusCommand
{
    /// <summary>Create status command.</summary>
    public static Command Create(CliOptions options)
    {
        var command = new Command("status", "Show system health and metrics summary");
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
                new Panel(new Text(doc.RootElement.GetProperty("health").ToString())) { Header = new PanelHeader("Health") },
                new Panel(new Text(doc.RootElement.GetProperty("metrics").ToString())) { Header = new PanelHeader("Metrics") });
            AnsiConsole.Write(grid);
        }, ct));
        return command;
    }
}
