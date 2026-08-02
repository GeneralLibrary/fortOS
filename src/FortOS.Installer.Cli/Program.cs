using System.CommandLine;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using FortOS.Installer.Core.Tools;
using Spectre.Console;

namespace FortOS.Installer.Cli;

public static class Program
{
    private static readonly string Version =
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("FortOS headless installer — drive a full installation from install.yaml.");

        var configOption = new Option<string?>("--config", ["-c"]) { Description = "Path to install.yaml driving a headless install." };
        var yesOption = new Option<bool>("--yes", ["-y"]) { Description = "Skip the confirmation prompt." };
        var listDisksOption = new Option<bool>("--list-disks") { Description = "List detected disks and exit." };
        var versionOption = new Option<bool>("--version") { Description = "Print version and exit." };

        root.Add(configOption);
        root.Add(yesOption);
        root.Add(listDisksOption);
        root.Add(versionOption);

        root.SetAction(async (parseResult, ct) =>
        {
            try
            {
                if (parseResult.GetValue(versionOption))
                {
                    AnsiConsole.WriteLine($"fortos-installer {Version}");
                    return 0;
                }

                if (parseResult.GetValue(listDisksOption))
                {
                    return await ListDisksAsync(ct).ConfigureAwait(false);
                }

                var configPath = parseResult.GetValue(configOption);
                if (configPath is null)
                {
                    AnsiConsole.MarkupLine("[red]No action given.[/] Use --config <install.yaml> or --list-disks.");
                    return 1;
                }

                var config = InstallYamlLoader.ToConfig(InstallYamlLoader.LoadYaml(configPath));
                PrintConfigSummary(config);

                // 非交互环境(stdin 重定向/CI)必须显式 --yes,否则报错而非卡死/误确认。
                var interactive = !Console.IsInputRedirected;
                if (!parseResult.GetValue(yesOption) && (!interactive || !AnsiConsole.Confirm("Begin installation? This will ERASE the target disk(s).", defaultValue: false)))
                {
                    AnsiConsole.MarkupLine(interactive
                        ? "[yellow]Aborted.[/]"
                        : "[red]Non-interactive input: pass --yes to confirm installation.[/]");
                    return 1;
                }

                return await RunInstallAsync(config, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 用户可见文本必须 Escape,防止被当作 Spectre markup 解析。
                AnsiConsole.MarkupLineInterpolated($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                return 1;
            }
        });

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    private static async Task<int> ListDisksAsync(CancellationToken ct)
    {
        var disks = await new LsblkTool(new ProcessRunner()).ListDisksAsync(ct).ConfigureAwait(false);
        var table = new Table().AddColumn("Device").AddColumn("Size").AddColumn("Model").AddColumn("Transport");
        foreach (var disk in disks)
        {
            // 硬件 Model/Transport 可能含方括号,必须 Escape(Table 单元格按 markup 渲染)。
            table.AddRow(Markup.Escape(disk.Path), Markup.Escape(disk.SizeHuman), Markup.Escape(disk.Model ?? "-"), Markup.Escape(disk.Transport ?? "-"));
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private static void PrintConfigSummary(InstallConfig config)
    {
        var data = config.Data.Mode switch
        {
            DataDiskMode.Single => $"{config.Data.Disk} ({config.Data.FileSystem})",
            DataDiskMode.Raid => $"RAID{config.Data.RaidLevel} [{string.Join(", ", config.Data.RaidDisks)}] ({config.Data.FileSystem})",
            DataDiskMode.Luks => $"{config.Data.Disk} LUKS2 ({config.Data.FileSystem})",
            _ => "not configured (post-install)",
        };

        var summary = new Table().Title("Installation plan")
            .AddColumn("Item").AddColumn("Value");
        // Table 单元格按 markup 渲染:动态文本必须 Escape,防止方括号等被解析。
        summary.AddRow("System disk", Markup.Escape($"{config.SystemDisk} — {config.RootFs}{(config.SwapMode == SwapMode.Off ? " (no swap)" : "")}"));
        summary.AddRow("Data disk", Markup.Escape(data));
        summary.AddRow("Network", Markup.Escape(config.Network.Mode == NetworkMode.Dhcp
            ? $"DHCP (hostname {config.Network.Hostname})"
            : $"static {config.Network.Address} (hostname {config.Network.Hostname})"));
        summary.AddRow("Admin user", Markup.Escape($"{config.Account.Username} ({config.Account.Timezone})"));
        summary.AddRow("Bootloader", Markup.Escape(config.Bootloader.ToString()));
        AnsiConsole.Write(summary);
    }

    private static async Task<int> RunInstallAsync(InstallConfig config, CancellationToken ct)
    {
        var session = InstallerSession.CreateDefault();

        session.PhaseChanged += phase =>
            AnsiConsole.MarkupLineInterpolated($"[bold cyan]>>> {Markup.Escape(phase.ToString())}[/]");

        session.StepProgress += progress =>
        {
            if (progress.Percent is 0 or 100)
            {
                AnsiConsole.MarkupLineInterpolated($"    [green]{Markup.Escape(progress.Step)}[/]: {Markup.Escape(progress.Message)}");
            }
        };

        AnsiConsole.MarkupLine("[yellow]Starting FortOS installation. Logs are written to /target/var/log/fortos-install.log on completion.[/]");
        // 透传取消令牌:headless 安装支持 Ctrl+C(引擎已实现取消清理路径)。
        var result = await session.RunAsync(config, ct).ConfigureAwait(false);

        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]FortOS installation completed successfully.[/]");
            AnsiConsole.MarkupLine("Reboot into the installed system to run the first-boot wizard.");
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated($"[red]Installation failed at step '{Markup.Escape(result.FailedStep ?? "-")}': {Markup.Escape(result.ErrorMessage ?? "Unknown error.")}[/]");
        AnsiConsole.MarkupLine("[yellow]The installer is safe to re-run (partitioning is idempotent).[/]");
        return 1;
    }
}
