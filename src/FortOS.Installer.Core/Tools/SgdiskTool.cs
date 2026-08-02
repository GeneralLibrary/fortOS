using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>sgdisk</c> 适配器:GPT 分区表创建/校验。
/// </summary>
public sealed class SgdiskTool : ITool
{
    private readonly IProcessRunner _runner;

    public SgdiskTool(IProcessRunner runner) => _runner = runner;

    public string Name => "sgdisk";

    /// <summary>清空磁盘上的所有分区表与数据(安装目标盘,先于布局创建)。</summary>
    public Task ZapAsync(string disk, CancellationToken ct)
        => RunAsync(disk, ["--zap-all", disk], ct);

    /// <summary>按模板创建分区。每个 spec 生成 --new / --typecode / --change-name 三连。</summary>
    public async Task CreatePartitionsAsync(string disk, IReadOnlyList<PartitionSpec> specs, CancellationToken ct)
    {
        var args = new List<string>();
        foreach (var spec in specs)
        {
            var sizeArg = spec.SizeMiB > 0 ? $"+{spec.SizeMiB}M" : "0";
            args.Add($"--new={spec.Number}:0:{sizeArg}");
            args.Add($"--typecode={spec.Number}:{spec.TypeCode}");
            if (!string.IsNullOrEmpty(spec.Label))
            {
                args.Add($"--change-name={spec.Number}:{spec.Label}");
            }
        }
        args.Add(disk);
        await RunAsync(disk, args, ct).ConfigureAwait(false);
    }

    /// <summary>校验分区表(自检)。</summary>
    public Task VerifyAsync(string disk, CancellationToken ct)
        => RunAsync(disk, ["--verify", disk], ct);

    private Task RunAsync(string disk, IReadOnlyList<string> args, CancellationToken ct)
        => _runner.RunAsync("sgdisk", args, ct);
}
