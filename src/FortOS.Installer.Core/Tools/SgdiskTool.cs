using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>sgdisk</c> adapter: GPT partition table creation/verification.
/// </summary>
public sealed class SgdiskTool : ITool
{
    private readonly IProcessRunner _runner;

    public SgdiskTool(IProcessRunner runner) => _runner = runner;

    public string Name => "sgdisk";

    /// <summary>Zaps all partition tables and data on the disk (the install target disk, before layout creation).</summary>
    public Task ZapAsync(string disk, CancellationToken ct)
        => RunAsync(disk, ["--zap-all", disk], ct);

    /// <summary>Creates partitions according to the template. Each spec generates the --new / --typecode / --change-name triplet.</summary>
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

    /// <summary>Verifies the partition table (self-check).</summary>
    public Task VerifyAsync(string disk, CancellationToken ct)
        => RunAsync(disk, ["--verify", disk], ct);

    private Task RunAsync(string disk, IReadOnlyList<string> args, CancellationToken ct)
        => _runner.RunAsync("sgdisk", args, ct);
}
