namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>rsync</c> adapter: copies the live rootfs to the target system (design draft 5.3).
/// </summary>
public sealed class RsyncTool : ITool
{
    /// <summary>Copy timeout: a full system copy can take a long time, so it is relaxed to 2 hours.</summary>
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromHours(2);

    /// <summary>Paths excluded during the copy (live runtime environment and target mount points).</summary>
    private static readonly string[] Excludes =
    [
        "/proc", "/sys", "/dev", "/run", "/tmp", "/mnt", "/target", "/live",
        "/media", "/var/cache/apt/archives",
        "/var/lib/docker", "/var/lib/containerd", "/var/cache/fortos-packages",
    ];

    private readonly IProcessRunner _runner;

    public RsyncTool(IProcessRunner runner) => _runner = runner;

    public string Name => "rsync";

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="target"/>.
    /// When source is <c>/</c>, excludes the live virtual file systems and target mount points.
    /// </summary>
    public async Task CopyAsync(string source, string target, CancellationToken ct)
    {
        var args = new List<string>
        {
            // Note: -S/--sparse must not be used. btrfs enables the no_holes feature by default, so sparse
            // copies leave extent holes in the target; when the GRUB 2.06 btrfs driver
            // traverses holes it reports "extent not found" (only fixed upstream in 7f4e017a, effective
            // from 2.12), causing initrd reads to fail → the kernel has no initramfs → on reboot it
            // panics with "VFS: Unable to mount root fs on unknown-block(0,0)".
            "-aHAX", "--one-file-system", "--numeric-ids", "--info=progress2",
        };
        foreach (var exclude in Excludes)
        {
            args.Add($"--exclude={exclude}");
        }

        // The source directory ends with a slash: copy the contents rather than the directory itself.
        var sourceArg = source.EndsWith('/') ? source : source + "/";
        args.Add(sourceArg);
        args.Add(target + "/");

        await _runner.RunAsync("rsync", args, ct, timeout: CopyTimeout).ConfigureAwait(false);
    }
}
