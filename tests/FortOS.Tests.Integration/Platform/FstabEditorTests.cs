using FortOS.Platform.Linux;

namespace FortOS.Tests.Integration.Platform;

public class FstabEditorTests
{
    private const string BaseContent = "UUID=root / ext4 errors=remount-ro 0 1\nUUID=swap none swap sw 0 0\n";

    [Fact]
    [Trait("Category", "Unit")]
    public void UpsertEntry_AppendsManagedEntry_PreservingExistingLines()
    {
        var result = FstabEditor.UpsertEntry(BaseContent, "/dev/sdb1", "/srv/nas/data", "ext4");

        Assert.Contains("UUID=root / ext4 errors=remount-ro 0 1", result);
        Assert.Contains("UUID=swap none swap sw 0 0", result);
        Assert.Contains($"/dev/sdb1 /srv/nas/data ext4 defaults,nofail 0 2 {FstabEditor.ManagedMarker}", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UpsertEntry_IsIdempotent_ForSameMountPoint()
    {
        var once = FstabEditor.UpsertEntry(BaseContent, "/dev/sdb1", "/srv/nas/data", "ext4");
        var twice = FstabEditor.UpsertEntry(once, "/dev/sdc1", "/srv/nas/data", "xfs");

        Assert.DoesNotContain("/dev/sdb1", twice);
        Assert.Single(twice.Split('\n', StringSplitOptions.RemoveEmptyEntries), l => l.Contains("/srv/nas/data"));
        Assert.Contains($"/dev/sdc1 /srv/nas/data xfs defaults,nofail 0 2 {FstabEditor.ManagedMarker}", twice);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RemoveEntry_OnlyRemovesManagedEntry_ForMountPoint()
    {
        var content = FstabEditor.UpsertEntry(BaseContent, "/dev/sdb1", "/srv/nas/data", "ext4");
        content = FstabEditor.UpsertEntry(content, "/dev/sdc1", "/srv/nas/media", "btrfs");

        var result = FstabEditor.RemoveEntry(content, "/srv/nas/data");

        Assert.DoesNotContain("/srv/nas/data", result);
        Assert.Contains("/srv/nas/media", result);
        Assert.Contains("UUID=root / ext4 errors=remount-ro 0 1", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RemoveEntry_DoesNotTouchUnmanagedEntries()
    {
        var unmanaged = "/dev/sda2 /srv/nas/data ext4 defaults 0 2\n";

        var result = FstabEditor.RemoveEntry(unmanaged, "/srv/nas/data");

        Assert.Contains("/dev/sda2 /srv/nas/data ext4 defaults 0 2", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void UpsertEntry_OnEmptyContent_ProducesSingleEntry()
    {
        var result = FstabEditor.UpsertEntry(string.Empty, "/dev/sdb1", "/mnt/pool", "btrfs");

        Assert.Equal($"/dev/sdb1 /mnt/pool btrfs defaults,nofail 0 2 {FstabEditor.ManagedMarker}{Environment.NewLine}", result);
    }
}
