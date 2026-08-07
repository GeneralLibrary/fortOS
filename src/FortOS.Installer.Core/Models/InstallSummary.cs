namespace FortOS.Installer.Core.Models;

/// <summary>
/// Installation summary. Written to the target system after installation completes
/// at <c>/etc/fortos/install-summary.json</c> (design doc 4/5.1).
/// </summary>
public sealed class InstallSummary
{
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public bool Success { get; set; }

    /// <summary>FortOS version (taken from /etc/fortos/version).</summary>
    public string? FortosVersion { get; set; }

    public string? SystemDisk { get; set; }

    public string? SystemRootFs { get; set; }

    public string? RootUuid { get; set; }

    public string? EfiUuid { get; set; }

    public string? DataDisk { get; set; }

    public string? DataFs { get; set; }

    public string? DataUuid { get; set; }

    /// <summary>Actual boot mode: uefi / bios.</summary>
    public string? BootMode { get; set; }

    public string? Hostname { get; set; }

    public string? Username { get; set; }

    public string? Language { get; set; }

    public string? Timezone { get; set; }
}
