using System.CommandLine;
using GNAS.Cli.ApiClient;
using GNAS.Cli.Commands;
using GNAS.Cli.Tui;

/// <summary>GNAS CLI 程序入口。</summary>
internal static class Program
{
    /// <summary>启动 CLI 或互动 TUI。</summary>
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            await Console.Error.WriteLineAsync("GNAS CLI 仅支持 Linux。");
            return 1;
        }

        var options = new CliOptions();
        var root = BuildRoot(options);

        if (args.Length == 0)
        {
            if (!Console.IsOutputRedirected)
            {
                using var client = new GnasApiClient();
                return await new TuiRenderer().RunAsync(client);
            }
            return await root.Parse("--help").InvokeAsync();
        }

        return await root.Parse(args).InvokeAsync();
    }

    private static RootCommand BuildRoot(CliOptions options)
    {
        var root = new RootCommand("GNAS NAS 桌面命令列工具");
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
