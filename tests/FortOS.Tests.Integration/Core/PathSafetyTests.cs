using FortOS.Core;

namespace FortOS.Tests.Integration.Core;

/// <summary>
/// PathSafety 安全路径工具回归测试：覆盖 .. 穿越、边界分隔符、Windows 盘符
/// 处理与根路径防护（历史上有 4 份行为不一致的实现，此处锁定统一语义）。
/// </summary>
public sealed class PathSafetyTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void NormalizePath_ResolvesParentSegments()
    {
        Assert.Equal("/srv/etc", PathSafety.NormalizePath("/srv/nas/../etc"));
        Assert.Equal("/srv/nas/docs", PathSafety.NormalizePath("/srv/nas/./docs"));
        Assert.Equal("/srv", PathSafety.NormalizePath("/srv/nas/.."));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NormalizePath_DoesNotEscapeAboveRoot()
    {
        // 根之上的 .. 被忽略，不会逃逸到 / 之上。
        Assert.Equal("/etc", PathSafety.NormalizePath("/../../etc"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NormalizePath_PreservesWindowsDrive()
    {
        // 盘符语义仅存在于 Windows；在 Linux 上 C:\... 是普通相对路径，无法验证。
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 盘符路径保留盘符（回归：早期实现把 C:\x 错误规范化为 /C:/x）。
        Assert.Equal("C:/github/docs", PathSafety.NormalizePath("C:\\github\\nas\\..\\docs"));
        Assert.StartsWith("C:/", PathSafety.NormalizePath("C:/data"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsPathUnderRoot_RejectsTraversal()
    {
        Assert.False(PathSafety.IsPathUnderRoot("/srv/nas/../etc", "/srv/nas"));
        Assert.False(PathSafety.IsPathUnderRoot("/srv/nas/../../etc", "/srv/nas"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsPathUnderRoot_RespectsSegmentBoundary()
    {
        // /data/share2 不是 /data/share 的子路径（前缀匹配必须带分隔符边界）。
        Assert.True(PathSafety.IsPathUnderRoot("/data/share/docs", "/data/share"));
        Assert.False(PathSafety.IsPathUnderRoot("/data/share2/docs", "/data/share"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsPathUnderRoot_AcceptsRootItself()
    {
        Assert.True(PathSafety.IsPathUnderRoot("/data/share", "/data/share"));
        Assert.True(PathSafety.IsPathUnderRoot("/data/share/", "/data/share"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsPathUnderRoot_RejectsRootFilesystemAsAllowedRoot()
    {
        // "/" 作为允许根会放行一切路径，安全上必须拒绝。
        Assert.False(PathSafety.IsPathUnderRoot("/etc/passwd", "/"));
    }
}
