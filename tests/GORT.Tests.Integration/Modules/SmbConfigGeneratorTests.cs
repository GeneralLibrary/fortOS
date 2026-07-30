using GORT.Core;
using GORT.Modules.Share.Services;

namespace GORT.Tests.Integration.Modules;

public sealed class SmbConfigGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Generate_ValidShare_ContainsExpectedLines()
    {
        var share = new ShareDefinition { ShareId = "media", Name = "media", Path = "/mnt/nas/data/media", ReadOnly = true, Protocols = ["smb"] };

        var conf = new SmbConfigGenerator().Generate([share]);

        Assert.Contains("[media]", conf);
        Assert.Contains("path = /mnt/nas/data/media", conf);
        Assert.Contains("read only = yes", conf);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("../bad", "/mnt/nas/data/media")]
    [InlineData("bad\nname", "/mnt/nas/data/media")]
    [InlineData("media", "/mnt/nas/../etc")]
    [InlineData("media", "/mnt/nas/data\npath")]
    public void Generate_InvalidShare_RejectsInjection(string name, string path)
    {
        var share = new ShareDefinition { ShareId = "x", Name = name, Path = path, Protocols = ["smb"] };

        Assert.Throws<ArgumentException>(() => new SmbConfigGenerator().Generate([share]));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidateShare_WebDavProtocol_IsRejectedUntilAuthenticationExists()
    {
        var share = new ShareDefinition
        {
            ShareId = "documents",
            Name = "documents",
            Path = "/srv/nas/documents",
            Protocols = ["webdav"],
        };

        Assert.Throws<ArgumentException>(() => ShareValidation.ValidateShare(share));
    }
}
