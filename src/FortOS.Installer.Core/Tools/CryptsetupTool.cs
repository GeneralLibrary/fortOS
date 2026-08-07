namespace FortOS.Installer.Core.Tools;

/// <summary>
/// <c>cryptsetup</c> adapter: LUKS2 encryption (design draft 6).
/// Wired into the default installation flow (PartitionStep creates/opens, FinalizeStep closes).
/// Passphrases are always passed via stdin, never on the command line.
/// </summary>
public sealed class CryptsetupTool : ITool
{
    private readonly IProcessRunner _runner;

    public CryptsetupTool(IProcessRunner runner) => _runner = runner;

    public string Name => "cryptsetup";

    /// <summary>Creates a LUKS2 container. The passphrase is passed via stdin, never on the command line.</summary>
    public async Task LuksFormatAsync(string device, string passphrase, CancellationToken ct)
    {
        await _runner.RunAsync(
            "cryptsetup",
            ["luksFormat", "--type=luks2", "--batch-mode", "--key-file=-", device],
            ct,
            standardInput: passphrase + "\n").ConfigureAwait(false);
    }

    /// <summary>Opens the encrypted container, mapping it to <paramref name="name"/>.</summary>
    public async Task LuksOpenAsync(string device, string name, string passphrase, CancellationToken ct)
        => await _runner.RunAsync("cryptsetup", ["open", device, name], ct, standardInput: passphrase + "\n").ConfigureAwait(false);

    /// <summary>Closes the mapping.</summary>
    public Task LuksCloseAsync(string name, CancellationToken ct)
        => _runner.RunAsync("cryptsetup", ["close", name], ct, throwOnNonZeroExit: false);
}
