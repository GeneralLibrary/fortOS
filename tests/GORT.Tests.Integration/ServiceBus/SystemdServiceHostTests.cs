using GORT.Core;
using GORT.ServiceBus.Hosts;

namespace GORT.Tests.Integration.ServiceBus;

public sealed class SystemdServiceHostTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task StartAndStop_UseConfiguredSystemdUnit()
    {
        var process = new RecordingProcessManager();
        var events = new RecordingEventBus();
        var host = new SystemdServiceHost(process, events);
        var definition = Definition();

        await host.StartAsync(definition, CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        Assert.Collection(
            process.Commands,
            command => Assert.Equal("start \"smbd.service\"", command.Arguments),
            command => Assert.Equal("stop \"smbd.service\"", command.Arguments));
        Assert.Contains(events.Topics, topic => topic == "service.smb.started");
        Assert.Contains(events.Topics, topic => topic == "service.smb.stopped");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_ParsesSystemdProperties()
    {
        var process = new RecordingProcessManager
        {
            Result = new CommandResult
            {
                ExitCode = 0,
                Stdout = "ActiveState=active\nMainPID=123\nMemoryCurrent=4096\n",
            },
        };
        var host = new SystemdServiceHost(process, new RecordingEventBus());
        await host.StartAsync(Definition(), CancellationToken.None);
        process.Commands.Clear();

        var status = await host.GetStatusAsync(CancellationToken.None);

        Assert.Equal(ServiceStatus.Running, status.Status);
        Assert.Equal(ServiceType.Systemd, status.Type);
        Assert.Equal(123, status.Pid);
        Assert.Equal(4096, status.MemoryBytes);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Start_InvalidUnit_RejectsCommandInjection()
    {
        var host = new SystemdServiceHost(new RecordingProcessManager(), new RecordingEventBus());
        var definition = Definition() with { SystemdUnit = "smbd.service; reboot" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => host.StartAsync(definition, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Start_SystemctlFailure_IsPropagated()
    {
        var process = new RecordingProcessManager
        {
            Result = new CommandResult { ExitCode = 1, Stderr = "unit failed" },
        };
        var host = new SystemdServiceHost(process, new RecordingEventBus());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync(Definition(), CancellationToken.None));

        Assert.Contains("unit failed", error.Message, StringComparison.Ordinal);
    }

    private static ServiceDefinition Definition() => new()
    {
        ServiceId = "smb",
        DisplayName = "Samba",
        Type = ServiceType.Systemd,
        Startup = ServiceStartup.Automatic,
        RestartPolicy = RestartPolicy.Never,
        SystemdUnit = "smbd.service",
    };

    private sealed class RecordingProcessManager : IProcessManager
    {
        public List<ProcessStartConfig> Commands { get; } = [];
        public CommandResult Result { get; init; } = new() { ExitCode = 0 };

        public Task<CommandResult> ExecuteCommandAsync(ProcessStartConfig config, CancellationToken ct)
        {
            Commands.Add(config);
            return Task.FromResult(Result);
        }

        public Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config, CancellationToken ct) => throw new NotSupportedException();
        public Task StopProcessAsync(int pid, TimeSpan gracefulTimeout, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProcessInfo?> GetProcessAsync(int pid, CancellationToken ct) => throw new NotSupportedException();
        public Task EnableServiceAsync(string serviceName, CancellationToken ct) => throw new NotSupportedException();
        public Task DisableServiceAsync(string serviceName, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<string> Topics { get; } = [];

        public Task PublishAsync(EventEnvelope envelope, CancellationToken ct)
        {
            Topics.Add(envelope.Topic);
            return Task.CompletedTask;
        }

        public Task PublishAsync(string topic, string type, string dataJson, CancellationToken ct)
        {
            Topics.Add(topic);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe(string topicPattern, Func<EventEnvelope, CancellationToken, Task> handler)
            => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
