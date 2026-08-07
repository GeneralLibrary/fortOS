using FortOS.Installer.Core.Models;

namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>mkfs.*</c> / <c>mkswap</c> adapter: file system formatting.
/// </summary>
public sealed class MkfsTool : ITool
{
    private static readonly TimeSpan FormatTimeout = TimeSpan.FromMinutes(10);
    private readonly IProcessRunner _runner;

    public MkfsTool(IProcessRunner runner) => _runner = runner;

    public string Name => "mkfs";

    /// <summary>Formats a device with the specified file system. When label is empty, the label parameter is omitted.</summary>
    public async Task FormatAsync(string device, PartitionFs fs, string? label, CancellationToken ct)
    {
        (var fileName, var args) = BuildCommand(device, fs, label);
        await _runner.RunAsync(fileName, args, ct, timeout: FormatTimeout).ConfigureAwait(false);
    }

    private static (string FileName, List<string> Args) BuildCommand(string device, PartitionFs fs, string? label)
    {
        var args = new List<string>();
        var fileName = fs switch
        {
            PartitionFs.Vfat => "mkfs.fat",
            PartitionFs.Ext4 => "mkfs.ext4",
            PartitionFs.Btrfs => "mkfs.btrfs",
            PartitionFs.Xfs => "mkfs.xfs",
            PartitionFs.Swap => "mkswap",
            PartitionFs.None => throw new InvalidOperationException("PartitionFs.None cannot be formatted."),
            _ => throw new InvalidOperationException($"Unsupported file system: {fs}"),
        };

        switch (fs)
        {
            case PartitionFs.Vfat:
                args.Add("-F");
                args.Add("32");
                break;
            case PartitionFs.Ext4:
                // mkfs.ext4's force option is the uppercase -F (lowercase -f belongs to mkfs.xfs;
                // passing -f to ext4 fails immediately with "invalid option").
                args.Add("-F");
                break;
            case PartitionFs.Xfs:
            case PartitionFs.Btrfs:
                args.Add("-f"); // force, overwriting leftover signatures
                break;
            case PartitionFs.Swap:
                break;
        }

        if (!string.IsNullOrEmpty(label))
        {
            // dosfstools uses -n to set the label; ext4/btrfs/xfs/swap use -L.
            args.Add(fs == PartitionFs.Vfat ? "-n" : "-L");
            args.Add(label);
        }

        args.Add(device);
        return (fileName, args);
    }
}
