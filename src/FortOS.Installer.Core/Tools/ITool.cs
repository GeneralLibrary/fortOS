namespace FortOS.Installer.Core.Tools;

/// <summary>
/// Marker interface for system tool adapters. All adapters use structured output (JSON) or deterministic argument construction as their boundary;
/// fuzzy text parsing is prohibited (design draft 6).
/// </summary>
public interface ITool
{
    /// <summary>Tool name, used for logging and UI display.</summary>
    string Name { get; }
}
