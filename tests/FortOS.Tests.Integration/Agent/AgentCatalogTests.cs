using FortOS.Agent.Catalog;

namespace FortOS.Tests.Integration.Agent;

public class AgentCatalogTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListGetAndSearchTemplatesFromCatalogDirectory()
    {
        using var root = new AgentTestDataRoot(nameof(ListGetAndSearchTemplatesFromCatalogDirectory));
        var catalogDir = Path.Combine(root.Root, "agents", "catalog");
        Directory.CreateDirectory(catalogDir);
        await File.WriteAllTextAsync(Path.Combine(catalogDir, "custom-template.template.yaml"), ValidTemplateYaml("custom-template", "Custom Agent", "media automation"));
        var catalog = new AgentCatalog();

        var templates = await catalog.ListTemplatesAsync(CancellationToken.None);
        var template = await catalog.GetTemplateAsync("custom-template", CancellationToken.None);
        var search = await catalog.SearchTemplatesAsync("MEDIA", CancellationToken.None);

        Assert.Contains(templates, t => t.Id == "custom-template");
        Assert.NotNull(template);
        Assert.Equal("custom-template", template.Id);
        Assert.Contains("image", template.ComposeTemplate);
        Assert.Single(search);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InstallTemplateRejectsInvalidYaml()
    {
        using var root = new AgentTestDataRoot(nameof(InstallTemplateRejectsInvalidYaml));
        // 源文件必须位于 catalog 目录内（安全白名单：本地源禁止读取目录外文件）。
        var catalogDir = Path.Combine(root.Root, "agents", "catalog");
        Directory.CreateDirectory(catalogDir);
        var source = Path.Combine(catalogDir, "bad.template.yaml");
        await File.WriteAllTextAsync(source, "id: X\nname: Bad\nversion: nope\ncompose: [");
        var catalog = new AgentCatalog();

        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.InstallTemplateAsync(source, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmptyCatalog_SeedsBuiltInTemplates()
    {
        using var root = new AgentTestDataRoot(nameof(EmptyCatalog_SeedsBuiltInTemplates));
        var catalog = new AgentCatalog();

        var templates = await catalog.ListTemplatesAsync(CancellationToken.None);

        Assert.Contains(templates, t => t.Id == "nginx-basic");
        Assert.Contains(templates, t => t.Id == "alpine-worker");
        // Market templates ship with the catalog.
        Assert.Contains(templates, t => t.Id == "openclaw");
        Assert.Contains(templates, t => t.Id == "open-webui");
        Assert.Contains(templates, t => t.Id == "ollama");
        // P0-2: 24h AI 宿主模板。
        Assert.Contains(templates, t => t.Id == "opencode");
        Assert.Contains(templates, t => t.Id == "hermes");
        // P1-5: 影音中心模板。
        Assert.Contains(templates, t => t.Id == "jellyfin");
        // P2-7: 垂直应用模板(KTV/影院)。
        Assert.Contains(templates, t => t.Id == "kodi");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task MarketTemplates_CarryPortParametersAndAccessNotes()
    {
        using var root = new AgentTestDataRoot(nameof(MarketTemplates_CarryPortParametersAndAccessNotes));
        var catalog = new AgentCatalog();

        var templates = await catalog.ListTemplatesAsync(CancellationToken.None);

        var openclaw = Assert.Single(templates, t => t.Id == "openclaw");
        Assert.NotEmpty(openclaw.AccessNotes);
        Assert.Contains(openclaw.Parameters, p => p.Name == "HOST_PORT" && p.Default == "18789");
        Assert.Contains(openclaw.Parameters, p => p.Name == "TELEGRAM_BOT_TOKEN");
        Assert.Contains(openclaw.Parameters, p => p.Name == "data_dir" && p.Default == "/home/node/.openclaw");
        Assert.Contains(openclaw.Parameters, p => p.Name == "data_uid" && p.Default == "1000");
        Assert.Contains(openclaw.Parameters, p => p.Name == "config_file" && p.Default == "openclaw.json");

        var webui = Assert.Single(templates, t => t.Id == "open-webui");
        Assert.Contains(webui.Parameters, p => p.Name == "HOST_PORT" && p.Default == "3000");
        Assert.Contains(webui.Parameters, p => p.Name == "CONTAINER_PORT" && p.Default == "8080");
        Assert.NotEmpty(webui.AccessNotes);

        // P0-2: AI 宿主模板暴露端口/模型/数据目录参数与访问说明。
        var opencode = Assert.Single(templates, t => t.Id == "opencode");
        Assert.Contains(opencode.Parameters, p => p.Name == "OPENAI_BASE_URL");
        Assert.Contains(opencode.Parameters, p => p.Name == "OPENAI_MODEL");
        Assert.NotEmpty(opencode.AccessNotes);

        var hermes = Assert.Single(templates, t => t.Id == "hermes");
        Assert.Contains(hermes.Parameters, p => p.Name == "OPENAI_BASE_URL");
        Assert.Contains(hermes.Parameters, p => p.Name == "HERMES_WORKSPACE");
        Assert.NotEmpty(hermes.AccessNotes);
    }

    private static string ValidTemplateYaml(string id, string name, string description) => $@"id: {id}
name: {name}
version: 1.2.3
description: {description}
capabilities_required:
  - storage:share:media:read
parameters:
  - name: share
    type: string
    required: true
    default: media
compose:
  services:
    {id}:
      image: ""{{{{.ImageName}}}}""
";
}
