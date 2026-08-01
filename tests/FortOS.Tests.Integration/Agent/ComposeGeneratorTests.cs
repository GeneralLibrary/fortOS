using FortOS.Agent.Compose;
using FortOS.Core;
using YamlDotNet.RepresentationModel;

namespace FortOS.Tests.Integration.Agent;

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

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GenerateWritesTemplateParameterDefaultsAndUserEnvironmentToEnvFile()
    {
        using var root = new AgentTestDataRoot(nameof(GenerateWritesTemplateParameterDefaultsAndUserEnvironmentToEnvFile));
        var generator = new ComposeGenerator(new FixedTokenBroker("raw-agent-token-value"));
        var template = new AgentTemplate
        {
            Id = "openclaw",
            Name = "OpenClaw",
            Version = "1.0.0",
            Parameters =
            [
                new AgentTemplateParameter { Name = "HOST_PORT", Type = "int", Default = "18789" },
                new AgentTemplateParameter { Name = "TELEGRAM_BOT_TOKEN", Type = "string", Default = "" },
                new AgentTemplateParameter { Name = "OPENAI_API_KEY", Type = "string", Default = "" },
            ],
            ComposeTemplate = """
                services:
                  {{.AgentId}}:
                    image: "{{.ImageName}}"
                    ports:
                      - "${HOST_PORT}:18789"
                """,
        };
        var config = new AgentConfig
        {
            AgentId = "openclaw-test",
            DisplayName = "OpenClaw",
            ImageName = "ghcr.io/openclaw/openclaw:latest",
            Environment = new Dictionary<string, string>
            {
                ["OPENAI_API_KEY"] = "sk-test",
                ["EXTRA_VAR"] = "x",
            },
        };

        var result = await generator.GenerateAsync(template, config, "owner", CancellationToken.None);
        var env = await File.ReadAllTextAsync(result.EnvFilePath);
        var compose = await File.ReadAllTextAsync(result.ComposeFilePath);

        // Template parameter default is written to .env so ${HOST_PORT} resolves in compose.
        Assert.Contains("HOST_PORT=18789", env);
        // User environment overrides template defaults and adds custom variables.
        Assert.Contains("OPENAI_API_KEY=sk-test", env);
        Assert.Contains("EXTRA_VAR=x", env);
        // Empty parameter defaults are skipped.
        Assert.DoesNotContain("TELEGRAM_BOT_TOKEN=", env);
        Assert.Contains("${HOST_PORT}:18789", compose);
        // The raw agent token must never leak into compose.
        Assert.DoesNotContain("raw-agent-token-value", compose);
    }
}
