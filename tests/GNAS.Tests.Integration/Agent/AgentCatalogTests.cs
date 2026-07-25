using GNAS.Agent.Catalog;

namespace GNAS.Tests.Integration.Agent;

public class AgentCatalogTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListGetAndSearchTemplatesFromCatalogDirectory()
    {
        using var root = new AgentTestDataRoot(nameof(ListGetAndSearchTemplatesFromCatalogDirectory));
        var catalogDir = Path.Combine(root.Root, "agents", "catalog");
        Directory.CreateDirectory(catalogDir);
        await File.WriteAllTextAsync(Path.Combine(catalogDir, "openclaw.template.yaml"), ValidTemplateYaml("openclaw", "OpenClaw Agent", "media automation"));
        var catalog = new AgentCatalog();

        var templates = await catalog.ListTemplatesAsync(CancellationToken.None);
        var template = await catalog.GetTemplateAsync("openclaw", CancellationToken.None);
        var search = await catalog.SearchTemplatesAsync("MEDIA", CancellationToken.None);

        Assert.Single(templates);
        Assert.NotNull(template);
        Assert.Equal("openclaw", template.Id);
        Assert.Contains("image", template.ComposeTemplate);
        Assert.Single(search);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InstallTemplateRejectsInvalidYaml()
    {
        using var root = new AgentTestDataRoot(nameof(InstallTemplateRejectsInvalidYaml));
        var source = Path.Combine(root.Root, "bad.template.yaml");
        await File.WriteAllTextAsync(source, "id: X\nname: Bad\nversion: nope\ncompose: [");
        var catalog = new AgentCatalog();

        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.InstallTemplateAsync(source, CancellationToken.None));
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
