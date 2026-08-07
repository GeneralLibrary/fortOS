using FortOS.Installer.Core.Session;

namespace FortOS.Installer.Core.Steps;

/// <summary>Install step interface. Every step is idempotent and retryable (design doc 5.1).</summary>
public interface IInstallStep
{
    /// <summary>Step display name.</summary>
    string Name { get; }

    /// <summary>Session phase this step belongs to.</summary>
    InstallerPhase Phase { get; }

    /// <summary>Execute the step. On failure the exception is caught by the session and the Failed phase is set.</summary>
    Task ExecuteAsync(InstallContext context, CancellationToken ct);
}
