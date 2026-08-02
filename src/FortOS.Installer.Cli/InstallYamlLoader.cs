using FortOS.Installer.Core.Exceptions;
using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Session;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FortOS.Installer.Cli;

/// <summary>install.yaml 的宽松模型(可设置属性),映射为引擎强类型 InstallConfig。</summary>
public sealed class InstallYaml
{
    public SystemYaml System { get; set; } = new();

    public DataYaml Data { get; set; } = new();

    public NetworkYaml Network { get; set; } = new();

    public AccountYaml Account { get; set; } = new();

    public LocaleYaml Locale { get; set; } = new();

    public string? Source { get; set; }

    public string? Bootloader { get; set; }
}

public sealed class SystemYaml
{
    public string? Disk { get; set; }

    public string? RootFs { get; set; }

    public string? Swap { get; set; }
}

public sealed class DataYaml
{
    public string? Mode { get; set; }

    public string? Disk { get; set; }

    public string? Fs { get; set; }

    public string? Label { get; set; }

    /// <summary>RAID 级别(1/5/10)。</summary>
    public int? RaidLevel { get; set; }

    /// <summary>RAID 成员盘列表。</summary>
    public List<string> RaidDisks { get; set; } = [];

    /// <summary>LUKS 口令(headless/自动化路径使用)。</summary>
    public string? LuksPassphrase { get; set; }

    public string? LuksMapperName { get; set; }

    public string? RaidDeviceName { get; set; }
}

public sealed class NetworkYaml
{
    public string? Mode { get; set; }

    public string? Hostname { get; set; }

    public string? Address { get; set; }

    public string? Gateway { get; set; }

    public List<string> Dns { get; set; } = [];
}

public sealed class AccountYaml
{
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? SshKey { get; set; }

    public string? Timezone { get; set; }
}

public sealed class LocaleYaml
{
    public string? Lang { get; set; }

    public string? Keyboard { get; set; }
}

/// <summary>install.yaml 加载与校验。</summary>
public static class InstallYamlLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static InstallYaml LoadYaml(string path)
    {
        try
        {
            var yaml = Deserializer.Deserialize<InstallYaml>(File.ReadAllText(path));
            return yaml ?? throw new ConfigException("install.yaml is empty.");
        }
        catch (ConfigException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConfigException($"Failed to parse {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// yaml 宽松模型 → 引擎强类型配置,并立即执行完整校验
    /// (让用户在看确认提示前就发现配置错误,而非在破坏磁盘后)。
    /// </summary>
    public static InstallConfig ToConfig(InstallYaml yaml)
    {
        // 子节判空:YamlDotNet 对 "data:" / "data: null" 会把属性置 null。
        var system = yaml.System ?? throw new ConfigException("install.yaml is missing the 'system' section.");
        var data = yaml.Data ?? new DataYaml();
        var network = yaml.Network ?? new NetworkYaml();
        var account = yaml.Account ?? throw new ConfigException("install.yaml is missing the 'account' section.");
        var locale = yaml.Locale ?? new LocaleYaml();

        var config = new InstallConfig
        {
            SystemDisk = system.Disk ?? throw new ConfigException("system.disk is required."),
            RootFs = ParseEnum<RootFileSystem>(system.RootFs, "system.rootFs") ?? RootFileSystem.Btrfs,
            SwapMode = ParseSwapMode(system.Swap),
            SwapSizeMiB = ParseSwapSize(system.Swap),
            Data = new DataDiskConfig
            {
                Mode = ParseEnum<DataDiskMode>(data.Mode, "data.mode") ?? DataDiskMode.None,
                Disk = data.Disk,
                FileSystem = ParseEnum<DataFileSystem>(data.Fs, "data.fs") ?? DataFileSystem.Btrfs,
                Label = string.IsNullOrWhiteSpace(data.Label) ? "FORTOS_DATA" : data.Label,
                RaidLevel = data.RaidLevel ?? 1,
                RaidDisks = data.RaidDisks ?? [],
                RaidDeviceName = string.IsNullOrWhiteSpace(data.RaidDeviceName) ? "md127" : data.RaidDeviceName,
                LuksPassphrase = data.LuksPassphrase ?? string.Empty,
                LuksMapperName = string.IsNullOrWhiteSpace(data.LuksMapperName) ? "fortos-data" : data.LuksMapperName,
            },
            Network = new NetworkConfig
            {
                Mode = ParseEnum<NetworkMode>(network.Mode, "network.mode") ?? NetworkMode.Dhcp,
                Hostname = string.IsNullOrWhiteSpace(network.Hostname) ? "fortos" : network.Hostname,
                Address = network.Address,
                Gateway = network.Gateway,
                Dns = network.Dns ?? [],
            },
            Account = new AccountConfig
            {
                Username = account.Username ?? throw new ConfigException("account.username is required."),
                Password = account.Password ?? string.Empty,
                SshPublicKey = account.SshKey ?? string.Empty,
                Timezone = string.IsNullOrWhiteSpace(account.Timezone) ? "UTC" : account.Timezone,
            },
            Locale = new LocaleConfig
            {
                Language = locale.Lang ?? "en_US.UTF-8",
                Keyboard = locale.Keyboard ?? "us",
            },
            SourcePath = string.IsNullOrWhiteSpace(yaml.Source) ? "/" : yaml.Source,
            Bootloader = ParseEnum<BootloaderMode>(yaml.Bootloader, "bootloader") ?? BootloaderMode.Auto,
        };

        InstallerSession.ValidateConfig(config);
        return config;
    }

    private static T? ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        // 白名单校验:只接受枚举名(忽略大小写),拒绝数字字符串("1")与未知值。
        if (Enum.GetNames<T>().Any(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Enum.Parse<T>(trimmed, ignoreCase: true);
        }
        throw new ConfigException($"Invalid value '{value}' for {field}. Allowed: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    /// <summary>
    /// 解析 swap 字段:合法值仅 <c>auto</c> / <c>off</c> / 正整数(MiB)。
    /// 其他写法("8G"、"4GiB"、负数)直接报错,避免静默回落 Auto。
    /// </summary>
    private static SwapMode ParseSwapMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SwapMode.Auto;
        }
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return SwapMode.Auto;
        }
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return SwapMode.Off;
        }
        if (long.TryParse(value, out var size) && size > 0)
        {
            return SwapMode.Fixed;
        }
        throw new ConfigException("system.swap must be 'auto', 'off' or a positive integer (MiB).");
    }

    private static long? ParseSwapSize(string? value)
        => long.TryParse(value, out var size) && size > 0 ? size : null;
}
