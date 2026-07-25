namespace GNAS.Core;

/// <summary>
/// 标记属性值绝不能写入日志。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LogMaskedAttribute : Attribute
{
}
