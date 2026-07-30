using GORT.Core;
using GORT.Modules.Share.Services;

namespace GORT.Tests.Integration.Modules;

public sealed class NfsExportsGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Generate_ReadWriteShare_ContainsSquashAndRwOptions()
    {
        var share = new ShareDefinition { ShareId = "docs", Name = "docs", Path = "/mnt/nas/data/docs", ReadOnly = false, Protocols = ["nfs"] };

        var exports = new NfsExportsGenerator().Generate([share]);

        Assert.Contains("/mnt/nas/data/docs", exports);
        Assert.Contains("rw", exports);
        Assert.Contains("all_squash", exports);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generate_PathTraversal_Throws()
    {
        var share = new ShareDefinition { ShareId = "bad", Name = "bad", Path = "/mnt/nas/../bad", Protocols = ["nfs"] };

        Assert.Throws<ArgumentException>(() => new NfsExportsGenerator().Generate([share]));
    }
}
