namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>chroot</c> 适配器:目标系统配置阶段在 /target 内执行命令
/// (设计稿 5.4)。绑定挂载 /dev /proc /sys /run 后 chroot。
/// </summary>
public sealed class ChrootRunner : ITool
{
    private readonly IProcessRunner _runner;

    public ChrootRunner(IProcessRunner runner) => _runner = runner;

    public string Name => "chroot";

    /// <summary>绑定挂载虚拟文件系统到目标 rootfs,chroot 前置条件。</summary>
    public async Task BindMountsAsync(string target, CancellationToken ct)
    {
        foreach (var dir in new[] { "dev", "proc", "sys", "run" })
        {
            var host = $"/{dir}";
            var dst = $"{target}/{dir}";
            // rsync 排除清单不复制这些目录,必须先在目标上创建,否则 mount --bind 目标不存在。
            Directory.CreateDirectory(dst);
            await _runner
                .RunAsync("mount", ["--bind", host, dst], ct, throwOnNonZeroExit: false)
                .ConfigureAwait(false);
        }
    }

    /// <summary>在目标 rootfs 内执行 bash 脚本。</summary>
    /// <param name="standardInput">可选 stdin(如 chpasswd 的账户行),避免密码进命令行。</param>
    public async Task RunScriptAsync(string target, string script, CancellationToken ct, TimeSpan? timeout = null, string? standardInput = null)
    {
        // chroot <target> /bin/bash -euc "<script>" — ProcessRunner 参数列表传递,无 shell 注入风险。
        await _runner
            .RunAsync(
                "chroot",
                [target, "/bin/bash", "-euc", script],
                ct,
                timeout: timeout ?? TimeSpan.FromMinutes(10),
                standardInput: standardInput)
            .ConfigureAwait(false);
    }

    /// <summary>卸载目标 rootfs 上的绑定挂载(忽略失败,保证幂等)。</summary>
    public async Task UnmountAllAsync(string target, CancellationToken ct)
    {
        foreach (var dir in new[] { "dev", "proc", "sys", "run" })
        {
            await _runner
                .RunAsync("umount", ["-R", $"{target}/{dir}"], ct, throwOnNonZeroExit: false)
                .ConfigureAwait(false);
        }
    }
}
