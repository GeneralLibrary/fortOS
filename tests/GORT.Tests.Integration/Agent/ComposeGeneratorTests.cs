using GORT.Agent.Compose;
using GORT.Core;
using YamlDotNet.RepresentationModel;

namespace GORT.Tests.Integration.Agent;

public class ComposeGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task GenerateWritesValidComposeAndPrivateEnvFileWithoutTokenLeak()
    {
        using var root = new AgentTestDataRoot(nameof(GenerateWritesValidComposeAndPrivateEnvFileWithoutTokenLeak));
        var rawToken = "raw-agent-token-value";
        var generator = new ComposeGenerator(new FixedTokenBroker(rawToken));
        var template = new AgentTemplate
        {
            Id = "openclaw",
            Name = "OpenClaw",
            Version = "1.0.0",
            ComposeTemplate = """
                services:
                  {{.AgentId}}:
                    image: "{{.ImageName}}"
                """,
        };
        var config = new AgentConfig
        {
            AgentId = "openclaw",
            DisplayName = "OpenClaw",
            ImageName = "openclaw/agent:latest",
            Capabilities = ["storage:share:media:read"],
            VolumeMapping = [new VolumeMapping { HostPath = "/mnt/nas/media", ContainerPath = "/data/media", ReadOnly = true }],
            PortMapping = [new PortMapping { HostPort = 18080, ContainerPort = 8080, Protocol = "tcp" }],
            ResourceQuota = new ResourceQuota { CpuLimit = 1, MemoryLimitBytes = 512 * 1024 * 1024 },
        };

        var result = await generator.GenerateAsync(template, config, "owner", CancellationToken.None);
        var compose = await File.ReadAllTextAsync(result.ComposeFilePath);
        var env = await File.ReadAllTextAsync(result.EnvFilePath);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(compose));

        Assert.Contains("/mnt/nas/media:/data/media:ro", compose);
        Assert.Contains("18080:8080/tcp", compose);
        Assert.Contains("cpus", compose);
        Assert.Contains("512M", compose);
        Assert.Contains(rawToken, env);
        Assert.DoesNotContain(rawToken, compose);
        Assert.Contains("NAS_TOKEN: ${NAS_TOKEN}", compose);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(result.ComposeFilePath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(result.EnvFilePath));
    }
}
