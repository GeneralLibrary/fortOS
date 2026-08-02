namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>rsync</c> 适配器:live rootfs → 目标系统复制(设计稿 5.3)。
/// </summary>
public sealed class RsyncTool : ITool
{
    /// <summary>复制超时:完整系统复制可能耗时很长,放宽到 2 小时。</summary>
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromHours(2);

    /// <summary>复制时排除的路径(live 运行环境与目标挂载点)。</summary>
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
    /// 复制 <paramref name="source"/> 到 <paramref name="target"/>。
    /// source 为 <c>/</c> 时排除 live 虚拟文件系统与目标挂载点。
    /// </summary>
    public async Task CopyAsync(string source, string target, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-aHAXS", "--one-file-system", "--numeric-ids", "--info=progress2",
        };
        foreach (var exclude in Excludes)
        {
            args.Add($"--exclude={exclude}");
        }

        // 源目录以斜杠结尾:复制内容而非目录本身。
        var sourceArg = source.EndsWith('/') ? source : source + "/";
        args.Add(sourceArg);
        args.Add(target + "/");

        await _runner.RunAsync("rsync", args, ct, timeout: CopyTimeout).ConfigureAwait(false);
    }
}
