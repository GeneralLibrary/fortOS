using System.CommandLine;
using GORT.Cli.ApiClient;
using GORT.Cli.Commands;
using GORT.Cli.Tui;

/// <summary>GORT CLI program entry point.</summary>
internal static class Program
{
    /// <summary>Starts CLI or interactive TUI.</summary>
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            await Console.Error.WriteLineAsync("GORT CLI only supports Linux.");
            return 1;
        }

        var options = new CliOptions();
        var root = BuildRoot(options);

        if (args.Length == 0)
        {
            if (!Console.IsOutputRedirected)
            {
                using var client = new GortApiClient();
                return await new TuiRenderer().RunAsync(client);
            }
            return await root.Parse("--help").InvokeAsync();
        }

        return await root.Parse(args).InvokeAsync();
    }

    private static RootCommand BuildRoot(CliOptions options)
    {
        var root = new RootCommand("GORT NAS command-line tool");
        options.Server.Recursive = true;
        options.Token.Recursive = true;
        options.Output.Recursive = true;
        options.NoColor.Recursive = true;
        options.Output.AcceptOnlyFromAmong("json", "table");
        root.Add(options.Server);
        root.Add(options.Token);
        root.Add(options.Output);
        root.Add(options.NoColor);
        root.Add(StatusCommand.Create(options));
        root.Add(DiskCommand.Create(options));
        root.Add(FileCommand.Create(options));
        root.Add(ShareCommand.Create(options));
        root.Add(SnapshotCommand.Create(options));
        root.Add(BackupCommand.Create(options));
        root.Add(RecycleCommand.Create(options));
        root.Add(ServiceCommand.Create(options));
        root.Add(AgentCommand.Create(options));
        root.Add(LogCommand.Create(options));
        root.Add(MiscCommands.Audit(options));
        root.Add(MiscCommands.Alert(options));
        root.Add(MiscCommands.Auth(options));
        root.Add(MiscCommands.Config(options));
        root.Add(MiscCommands.Ups(options));
        root.Add(MiscCommands.Recovery(options));
        return root;
    }
}
