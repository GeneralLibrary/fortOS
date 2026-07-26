using System.Diagnostics;
using GNAS.Agent.Compose;
using GNAS.Core;
using GNAS.Tests.Integration.Agent;

namespace GNAS.Tests.Integration.E2E;

public sealed class DeployAndRunAgentE2ETests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ComposeGeneratorDeploysNginxContainerAndRemovesIt()
    {
        if (!await DockerAvailableAsync())
        {
            Console.WriteLine("Docker unavailable, skipping container E2E.");
            return;
        }

        using var root = new AgentTestDataRoot(nameof(ComposeGeneratorDeploysNginxContainerAndRemovesIt));
        var projectName = "gnas-e2e-" + Guid.NewGuid().ToString("N")[..12];
        var generator = new ComposeGenerator(new FixedTokenBroker("e2e-agent-token"));
        var template = new AgentTemplate
        {
            Id = "alpine-e2e",
            Name = "Alpine E2E",
            Version = "1.0.0",
            ComposeTemplate = """
                services:
                  {{.AgentId}}:
                    image: "{{.ImageName}}"
                    command: ["tail", "-f", "/dev/null"]
                """
        };
        var config = new AgentConfig
        {
            AgentId = "alpine-e2e-" + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "Alpine E2E",
            ImageName = "alpine:3.21",
            Capabilities = ["data:level:internal"]
        };
        var result = await generator.GenerateAsync(template, config, "owner-token", CancellationToken.None);

        try
        {
            var up = await RunDockerAsync($"compose -p {projectName} -f {Quote(result.ComposeFilePath)} up -d", TimeSpan.FromMinutes(2));
            Assert.Equal(0, up.ExitCode);

            var ps = await RunDockerAsync($"compose -p {projectName} -f {Quote(result.ComposeFilePath)} ps --format json", TimeSpan.FromSeconds(30));
            Assert.Equal(0, ps.ExitCode);
            Assert.Contains("running", ps.Stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await RunDockerAsync($"compose -p {projectName} -f {Quote(result.ComposeFilePath)} down --remove-orphans", TimeSpan.FromMinutes(1));
        }
    }

    private static async Task<bool> DockerAvailableAsync()
    {
        var result = await RunDockerAsync("version", TimeSpan.FromSeconds(15));
        return result.ExitCode == 0;
    }

    private static async Task<ProcessResult> RunDockerAsync(string arguments, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var startInfo = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ProcessResult(127, string.Empty, "docker unavailable");
        }

        if (process is null)
        {
            return new ProcessResult(127, string.Empty, "Failed to start docker process.");
        }

        using (process)
        {
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
