namespace GNAS.Core;

/// <summary>
/// NAS 数据分级。
/// </summary>
public enum NasDataLevel
{
    /// <summary>
    /// 公开数据。
    /// </summary>
    Public = 0,
    /// <summary>
    /// 内部数据。
    /// </summary>
    Internal = 1,
    /// <summary>
    /// 个人数据。
    /// </summary>
    Personal = 2,
    /// <summary>
    /// 敏感数据。
    /// </summary>
    Sensitive = 3,
    /// <summary>
    /// 系统数据。
    /// </summary>
    System = 4,
}
