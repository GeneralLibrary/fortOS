using System.CommandLine;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace FortOS.Cli.Commands;

/// <summary>
/// Prints the FortOS welcome banner and quick-start tips. Used both as a
/// `fortos welcome` sub-command (login banner) and before entering the TUI.
/// </summary>
public static class WelcomeCommand
{
    /// <summary>Default FortOS API management port.</summary>
    public const int ManagementPort = 5000;

    /// <summary>Create the `welcome` sub-command.</summary>
    public static Command Create(CliOptions options)
    {
        var command = new Command("welcome", "Show the FortOS welcome banner and quick-start tips");
        command.SetAction((_, _) =>
        {
            AnsiConsole.Write(BuildPanel());
            return Task.FromResult(0);
        });
        return command;
    }

    /// <summary>Renders the welcome banner once before entering the interactive TUI.</summary>
    public static void PrintBanner() => AnsiConsole.Write(BuildPanel());

    internal static Panel BuildPanel()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";
        var local = $"http://localhost:{ManagementPort}";

        // 列出所有非虚拟网卡的 IPv4,避免多网卡时选错导致按横幅地址访问不到。
        var addresses = AllIPv4();
        var webLines = new List<string>();
        if (addresses.Count == 0)
        {
            webLines.Add($"管理界面 Management: [green underline]http://localhost:{ManagementPort}/dashboard/[/]");
        }
        else
        {
            foreach (var address in addresses)
            {
                webLines.Add($"管理界面 Management: [green underline]http://{Markup.Escape(address)}:{ManagementPort}/dashboard/[/]");
            }
        }

        var commands = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn(string.Empty).NoWrap())
            .AddColumn(new TableColumn(string.Empty));
        commands.AddRow("[cyan]fortos[/]", "交互监控界面 (TUI)");
        commands.AddRow("[cyan]fortos status[/]", "查看系统与服务状态");
        commands.AddRow("[cyan]fortos disk list[/]", "查看磁盘");
        commands.AddRow("[cyan]fortos share list[/]", "查看共享");
        commands.AddRow("[cyan]fortos service list[/]", "查看服务");
        commands.AddRow("[cyan]fortos --help[/]", "查看全部命令");

        var contentRows = new List<IRenderable>
        {
            new Markup($"[bold yellow]FortOS[/] [grey]v{Markup.Escape(version)}[/]"),
            new Markup("[bold]欢迎使用 FortOS!Welcome to FortOS![/]"),
            Text.Empty,
        };
        contentRows.AddRange(webLines.Select(l => new Markup(l)));
        contentRows.Add(new Markup($"本地访问 Local: [green]{Markup.Escape(local)}[/]"));
        contentRows.Add(Text.Empty);
        contentRows.Add(new Markup("[bold]快速上手 Quick start:[/]"));
        contentRows.Add(commands);

        return new Panel(new Rows(contentRows.ToArray()))
        {
            Header = new PanelHeader("FortOS"),
            Padding = new Padding(1, 1, 1, 1)
        };
    }

    /// <summary>
    /// 返回所有非虚拟网卡的 IPv4 地址(排除 lo/docker/veth/br-* 等),按接口
    /// 顺序去重。多网卡时全部列出,避免只取第一个而选错网卡。
    /// </summary>
    internal static List<string> AllIPv4()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                         .Where(ni => ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                         .Where(ni => !IsVirtualInterface(ni.Name)))
            {
                foreach (var address in ni.GetIPProperties().UnicastAddresses
                             .Select(u => u.Address)
                             .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                             .Where(a => !IPAddress.IsLoopback(a))
                             .Where(a => !IsLinkLocal(a))
                             .Select(a => a.ToString()))
                {
                    if (!result.Contains(address))
                    {
                        result.Add(address);
                    }
                }
            }
        }
        catch
        {
            // 网络接口枚举失败时返回空列表,由调用方回退到 localhost。
        }
        return result;
    }

    /// <summary>
    /// Detect the primary IPv4 address: first non-loopback, non-tunnel interface
    /// that is up, excluding common virtual bridges (docker0, veth*, br-*, virbr*)
    /// and link-local addresses.
    /// </summary>
    internal static string? PrimaryIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Where(ni => !IsVirtualInterface(ni.Name))
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .FirstOrDefault(a => !IPAddress.IsLoopback(a) && !IsLinkLocal(a))?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsVirtualInterface(string name)
        => name.Equals("lo", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("docker", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("veth", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("br-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("virbr", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("vnic", StringComparison.OrdinalIgnoreCase);

    private static bool IsLinkLocal(IPAddress address)
    {
        if (address.IsIPv6LinkLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
