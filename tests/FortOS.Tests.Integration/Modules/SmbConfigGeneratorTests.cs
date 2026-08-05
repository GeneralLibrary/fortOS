using FortOS.Core;
using FortOS.Modules.Share.Services;

namespace FortOS.Tests.Integration.Modules;

public sealed class SmbConfigGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Generate_ValidShare_ContainsExpectedLines()
    {
        var share = new ShareDefinition { ShareId = "media", Name = "media", Path = "/srv/nas/data/media", ReadOnly = true, Protocols = ["smb"] };

        var conf = new SmbConfigGenerator().Generate([share]);

        Assert.Contains("[media]", conf);
        Assert.Contains("path = /srv/nas/data/media", conf);
        Assert.Contains("read only = yes", conf);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("../bad", "/srv/nas/data/media")]
    [InlineData("bad\nname", "/srv/nas/data/media")]
    [InlineData("media", "/srv/nas/../etc")]
    [InlineData("media", "/srv/nas/data\npath")]
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
