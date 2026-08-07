namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>chroot</c> adapter: executes commands inside /target during the target system configuration phase
/// (design draft 5.4). Chroots after bind-mounting /dev /proc /sys /run.
/// </summary>
public sealed class ChrootRunner : ITool
{
    private readonly IProcessRunner _runner;

    public ChrootRunner(IProcessRunner runner) => _runner = runner;

    public string Name => "chroot";

    /// <summary>Bind-mounts virtual file systems into the target rootfs; a precondition for chroot.</summary>
    public async Task BindMountsAsync(string target, CancellationToken ct)
    {
        foreach (var dir in new[] { "dev", "proc", "sys", "run" })
        {
            var host = $"/{dir}";
            var dst = $"{target}/{dir}";
            // The rsync exclude list does not copy these directories, so they must be created in the target first, otherwise mount --bind fails because the target does not exist.
            Directory.CreateDirectory(dst);
            await _runner
                .RunAsync("mount", ["--bind", host, dst], ct, throwOnNonZeroExit: false)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Executes a bash script inside the target rootfs.</summary>
    /// <param name="standardInput">Optional stdin (e.g. account lines for chpasswd), to avoid passwords entering the command line.</param>
    public async Task RunScriptAsync(string target, string script, CancellationToken ct, TimeSpan? timeout = null, string? standardInput = null)
    {
        // chroot <target> /bin/bash -euc "<script>" — passed via the ProcessRunner argument list, so there is no shell injection risk.
        await _runner
            .RunAsync(
                "chroot",
                [target, "/bin/bash", "-euc", script],
                ct,
                timeout: timeout ?? TimeSpan.FromMinutes(10),
                standardInput: standardInput)
            .ConfigureAwait(false);
    }

    /// <summary>Unmounts the bind mounts on the target rootfs (ignores failures to stay idempotent).</summary>
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
