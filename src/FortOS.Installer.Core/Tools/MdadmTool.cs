namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>mdadm</c> 适配器:RAID 组装与清理(设计稿 6)。
/// 已接入默认安装流程(PartitionStep 创建、FinalizeStep 停止)。
/// </summary>
public sealed class MdadmTool : ITool
{
    private readonly IProcessRunner _runner;

    public MdadmTool(IProcessRunner runner) => _runner = runner;

    public string Name => "mdadm";

    /// <summary>
    /// 创建 RAID 设备。level 如 <c>1</c>/<c>5</c>/<c>10</c>;
    /// devices 为成员盘路径(整盘参与)。
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

    /// <summary>停止 RAID 数组(失败容忍——设备可能未创建,保证「可重跑」)。</summary>
    public Task StopAsync(string device, CancellationToken ct)
        => _runner.RunAsync("mdadm", ["--stop", device], ct, throwOnNonZeroExit: false);
}
