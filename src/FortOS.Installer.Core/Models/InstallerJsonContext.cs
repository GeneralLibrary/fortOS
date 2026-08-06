using System.Text.Json.Serialization;

namespace FortOS.Installer.Core.Models;

/// <summary>
/// install-summary.json 的源生成序列化上下文(trim 安全)。
/// FinalizeStep 使用它替代反射式 JsonSerializer,避免
/// PublishTrimmed 下类型被裁剪导致序列化失败(IL2026)。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InstallSummary))]
[JsonSerializable(typeof(string[]))]
internal partial class InstallerJsonContext : JsonSerializerContext
{
}
