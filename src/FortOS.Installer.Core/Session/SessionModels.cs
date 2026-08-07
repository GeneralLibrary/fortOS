namespace FortOS.Installer.Core.Session;

/// <summary>
/// Installation session state-machine phases (design doc 5.1). Confirmation (Confirm) is handled by the frontend before calling the engine,
/// so the engine phases cover the sequential execution after confirmation.
/// </summary>
public enum InstallerPhase
{
    /// <summary>Idle / not started.</summary>
    Idle,

    /// <summary>Collects environment info (detects boot mode, validates disks).</summary>
    CollectInfo,

    /// <summary>Confirmation page (frontend semantics; the engine does not run it).</summary>
    Confirm,

    /// <summary>Disk partitioning.</summary>
    Partitioning,

    /// <summary>Filesystem formatting and mounting.</summary>
    Formatting,

    /// <summary>System copy (rsync live rootfs → target).</summary>
    Copying,

    /// <summary>chroot configuration of the target system.</summary>
    Configuring,

    /// <summary>Bootloader installation.</summary>
    Bootloader,

    /// <summary>Finalization (unmount, write summary).</summary>
    Finalize,

    /// <summary>Installation complete.</summary>
    Done,

    /// <summary>Failed (retryable / reinstall after reboot).</summary>
    Failed,
}

/// <summary>A single installation log entry (in-memory ring buffer + to disk).</summary>
public sealed record InstallLogEntry(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>Step-level progress (for UI progress bars).</summary>
public sealed record InstallStepProgress(string Step, double Percent, string Message);

/// <summary>Installation result.</summary>
public sealed class InstallResult
{
    public required bool Success { get; init; }

    /// <summary>Name of the failed step; null when the failure happened before any step (e.g. CollectInfo validation).</summary>
    public string? FailedStep { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Installation summary (finally written to /etc/fortos/install-summary.json).</summary>
    public Models.InstallSummary? Summary { get; init; }
}
