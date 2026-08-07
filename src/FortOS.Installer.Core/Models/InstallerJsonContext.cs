using System.Text.Json.Serialization;

namespace FortOS.Installer.Core.Models;

/// <summary>
/// Source-generated serialization context for install-summary.json (trim safe).
/// FinalizeStep uses it instead of the reflection-based JsonSerializer to avoid
/// serialization failures under PublishTrimmed caused by trimmed types (IL2026).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InstallSummary))]
[JsonSerializable(typeof(string[]))]
internal partial class InstallerJsonContext : JsonSerializerContext
{
}
