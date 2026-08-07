namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>mdadm</c> adapter: RAID assembly and cleanup (design draft 6).
/// Wired into the default installation flow (PartitionStep creates, FinalizeStep stops).
/// </summary>
public sealed class MdadmTool : ITool
{
    private readonly IProcessRunner _runner;

    public MdadmTool(IProcessRunner runner) => _runner = runner;

    public string Name => "mdadm";

    /// <summary>
    /// Creates a RAID device. level is e.g. <c>1</c>/<c>5</c>/<c>10</c>;
    /// devices are the member disk paths (whole disks participate).
    /// </summary>
    public async Task CreateAsync(string device, int level, string name, IReadOnlyList<string> devices, CancellationToken ct)
    {
        var args = new List<string>
        {
            "--create", device,
            "--level=" + level,
            "--raid-devices=" + devices.Count,
            "--name=" + name,
        };
        args.AddRange(devices);
        await _runner.RunAsync("mdadm", args, ct, timeout: TimeSpan.FromMinutes(5)).ConfigureAwait(false);
    }

    /// <summary>Stops the RAID array (failure-tolerant — the device may not have been created, keeping it "re-runnable").</summary>
    public Task StopAsync(string device, CancellationToken ct)
        => _runner.RunAsync("mdadm", ["--stop", device], ct, throwOnNonZeroExit: false);
}
