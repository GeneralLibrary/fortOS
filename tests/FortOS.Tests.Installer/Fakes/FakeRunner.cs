using FortOS.Installer.Core.Tools;

namespace FortOS.Tests.Installer.Fakes;

/// <summary>可编程的 IProcessRunner:记录调用,按文件名/参数返回预设 stdout。</summary>
public sealed class FakeRunner : IProcessRunner
{
    public List<(string File, List<string> Args, string? StandardInput)> Calls { get; } = [];

    /// <summary>按 (fileName, args) 返回 stdout;未命中返回空串。</summary>
    public Func<string, IReadOnlyList<string>, string>? StdoutResolver { get; set; }

    public int ExitCode { get; set; }

    public Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        TimeSpan? timeout = null,
        bool throwOnNonZeroExit = true,
        string? workingDirectory = null,
        string? standardInput = null)
    {
        Calls.Add((fileName, arguments.ToList(), standardInput));
        var stdout = StdoutResolver?.Invoke(fileName, arguments) ?? string.Empty;
        return Task.FromResult(new CommandResult(ExitCode, stdout, string.Empty));
    }
}
