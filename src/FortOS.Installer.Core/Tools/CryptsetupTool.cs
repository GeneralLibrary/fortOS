namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>cryptsetup</c> 适配器:LUKS2 加密(设计稿 6)。
/// 已接入默认安装流程(PartitionStep 创建/打开、FinalizeStep 关闭)。
/// 口令一律经 stdin 传入,不进命令行。
/// </summary>
public sealed class CryptsetupTool : ITool
{
    private readonly IProcessRunner _runner;

    public CryptsetupTool(IProcessRunner runner) => _runner = runner;

    public string Name => "cryptsetup";

    /// <summary>创建 LUKS2 容器。passphrase 经 stdin 传入,不进命令行。</summary>
    public async Task LuksFormatAsync(string device, string passphrase, CancellationToken ct)
    {
        await _runner.RunAsync(
            "cryptsetup",
            ["luksFormat", "--type=luks2", "--batch-mode", "--key-file=-", device],
            ct,
            standardInput: passphrase + "\n").ConfigureAwait(false);
    }

    /// <summary>打开加密容器,映射到 <paramref name="name"/>。</summary>
    public async Task LuksOpenAsync(string device, string name, string passphrase, CancellationToken ct)
        => await _runner.RunAsync("cryptsetup", ["open", device, name], ct, standardInput: passphrase + "\n").ConfigureAwait(false);

    /// <summary>关闭映射。</summary>
    public Task LuksCloseAsync(string name, CancellationToken ct)
        => _runner.RunAsync("cryptsetup", ["close", name], ct, throwOnNonZeroExit: false);
}
