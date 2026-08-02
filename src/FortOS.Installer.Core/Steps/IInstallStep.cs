using FortOS.Installer.Core.Session;

namespace FortOS.Installer.Core.Steps;

/// <summary>安装步骤接口。每步幂等、可重试(设计稿 5.1)。</summary>
public interface IInstallStep
{
    /// <summary>步骤显示名。</summary>
    string Name { get; }

    /// <summary>步骤对应的会话阶段。</summary>
    InstallerPhase Phase { get; }

    /// <summary>执行步骤。失败抛出异常由会话捕获并置 Failed 阶段。</summary>
    Task ExecuteAsync(InstallContext context, CancellationToken ct);
}
