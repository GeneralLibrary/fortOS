namespace GNAS.Core;

/// <summary>
/// 文件访问权限位。
/// </summary>
[Flags]
public enum FilePermission
{
    /// <summary>无权限。</summary>
    None = 0,
    /// <summary>读取权限。</summary>
    Read = 1,
    /// <summary>写入权限。</summary>
    Write = 2,
    /// <summary>读写权限。</summary>
    ReadWrite = 3,
    /// <summary>完全控制权限。</summary>
    FullControl = 7
}
