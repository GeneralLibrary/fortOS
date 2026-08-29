using FortOS.Api.Services;
using FortOS.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortOS.Tests.Integration.Api;

/// <summary>
/// Remote access (Tailscale) service tests: status parsing, enable/disable command
/// construction, and the enabled switch. Uses a stubbed IProcessManager — no real
/// tailscale binary is invoked.
/// </summary>
public sealed class RemoteAccessServiceTests
{
    [Fact]
    public async Task Status_Disabled_ReturnsDisabled()
    {
        var (service, _) = Create(enabled: false);

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.False(status.Enabled);
        Assert.Contains("未启用", status.Message);
    }

    [Fact]
    public async Task Status_TailscaleNotInstalled_ReturnsHint()
    {
        var (service, process) = Create(enabled: true);
        process.Results.Enqueue(CommandResult(exitCode: 1));

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.False(status.TailscaleInstalled);
        Assert.Contains("安装", status.Message);
    }

    [Fact]
    public async Task Status_LoggedIn_ParsesHostAndIp()
    {
        var (service, process) = Create(enabled: true);
        process.Results.Enqueue(CommandResult(exitCode: 0, stdout: "v1.0"));
        process.Results.Enqueue(CommandResult(exitCode: 0, stdout: """
            {"BackendState":"Running","Self":{"HostName":"nas-1","TailscaleIPs":["100.64.0.5"]}}
            """));

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.LoggedIn);
        Assert.Equal("nas-1", status.HostName);
        Assert.Equal("100.64.0.5", status.Ip);
    }

    [Fact]
    public async Task Enable_WithoutAuthKey_RunsUpAndReturnsStatus()
    {
        var (service, process) = Create(enabled: true);
        // up 成功 → 后续 GetStatusAsync 读到已登录。
        process.Results.Enqueue(CommandResult(exitCode: 0));
        process.Results.Enqueue(CommandResult(exitCode: 0, stdout: "v1.0"));
        process.Results.Enqueue(CommandResult(exitCode: 0, stdout: """{"BackendState":"Running","Self":{"HostName":"fortos","TailscaleIPs":["100.64.0.9"]}}"""));

        var status = await service.EnableAsync(CancellationToken.None);

        Assert.True(status.LoggedIn);
        Assert.Equal("100.64.0.9", status.Ip);
        // 首次调用应为 tailscale up(hostname 参数)。
        Assert.Equal("tailscale", process.Calls[0].ExecutablePath);
        Assert.Contains("up", process.Calls[0].Arguments);
    }

    [Fact]
    public async Task Disable_RunsTailscaleDown()
    {
        var (service, process) = Create(enabled: true);
        process.Results.Enqueue(CommandResult(exitCode: 0));

        var status = await service.DisableAsync(CancellationToken.None);

        Assert.Contains("已断开", status.Message);
        var call = Assert.Single(process.Calls);
        Assert.Equal("down", call.Arguments);
    }

    private static (RemoteAccessService Service, StubProcessManager Process) Create(bool enabled)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RemoteAccessService.EnabledKey] = enabled ? "true" : "false",
            })
            .Build();
        var process = new StubProcessManager();
        var service = new RemoteAccessService(process, config);
        return (service, process);
    }

    private static CommandResult CommandResult(int exitCode, string? stdout = null, string? stderr = null)
        => new() { ExitCode = exitCode, Stdout = stdout ?? string.Empty, Stderr = stderr ?? string.Empty };

    /// <summary>记录调用并按序返回预设结果的 IProcessManager 桩。</summary>
    private sealed class StubProcessManager : IProcessManager
    {
        public Queue<CommandResult> Results { get; } = new();
        public List<ProcessStartConfig> Calls { get; } = [];

        public Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct)
        {
            Calls.Add(config);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new CommandResult { ExitCode = 0 });
        }

        public Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct)
            => throw new NotSupportedException();

        public Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct)
            => throw new NotSupportedException();

        public Task EnableServiceAsync(string serviceName, CancellationToken ct)
            => throw new NotSupportedException();

        public Task DisableServiceAsync(string serviceName, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
