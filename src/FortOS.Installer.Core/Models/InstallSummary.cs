namespace FortOS.Installer.Core.Models;

/// <summary>
/// 安装摘要。安装完成后写入目标系统
/// <c>/etc/fortos/install-summary.json</c>(设计稿 4/5.1)。
/// </summary>
public sealed class InstallSummary
{
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public bool Success { get; set; }

    /// <summary>FortOS 版本(取自 /etc/fortos/version)。</summary>
    public string? FortosVersion { get; set; }

    public string? SystemDisk { get; set; }

    public string? SystemRootFs { get; set; }

    public string? RootUuid { get; set; }

    public string? EfiUuid { get; set; }

    public string? DataDisk { get; set; }

    public string? DataFs { get; set; }

    public string? DataUuid { get; set; }

    /// <summary>实际引导方式:uefi / bios。</summary>
    public string? BootMode { get; set; }

    public string? Hostname { get; set; }

    public string? Username { get; set; }

    public string? Language { get; set; }

    public string? Timezone { get; set; }
}
