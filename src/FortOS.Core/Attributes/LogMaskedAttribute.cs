namespace FortOS.Core;

/// <summary>
/// Marks that a property value must never be written to logs.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LogMaskedAttribute : Attribute
{
}
