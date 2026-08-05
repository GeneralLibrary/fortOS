using FortOS.Core;

namespace FortOS.Api.Controllers;

/// <summary>Path request.</summary>
public sealed record PathRequest(string Path);
/// <summary>Create RAID request. <see cref="CreateRaidRequest.Confirm"/> acknowledges that disk data is erased.</summary>
public sealed record CreateRaidRequest(RaidLevel Level, string[] DiskPaths, bool Confirm);
/// <summary>Snapshot request.</summary>
public sealed record SnapshotRequest(string Target, string? Name);
/// <summary>Restore snapshot request.</summary>
public sealed record RestoreSnapshotRequest(string Target);
/// <summary>Restore recycle bin request.</summary>
public sealed record RestoreRecycleRequest(string TargetPath);
/// <summary>Deploy agent request.</summary>
public sealed record DeployAgentRequest(string TemplateId, AgentConfig Config);
/// <summary>Asynchronous agent deployment status.</summary>
public sealed record AgentDeploymentStatus(
    string Status,
    string? Error,
    DateTimeOffset? StartedAt,
    string? ServiceId = null,
    DateTimeOffset? FinishedAt = null,
    string Stage = "queued",
    string? Message = null);
/// <summary>Deploy request compatible with legacy CLI.</summary>
public sealed record LegacyDeployAgentRequest(string Template, Dictionary<string, string>? Parameters);
/// <summary>Install agent template request.</summary>
public sealed record InstallAgentTemplateRequest(string Source);
/// <summary>Recovery request.</summary>
public sealed record RecoveryRequest(string Target, string? Mode, string? Source, string? SnapshotId, bool DryRun = false);
/// <summary>Login request.</summary>
public sealed record LoginRequest(string Username, string Password, string? Totp);
/// <summary>Register user request.</summary>
public sealed record RegisterRequest(string Username, string Password, string? DisplayName, string? Email);
/// <summary>Config value.</summary>
public sealed record ConfigValue(string? Value);
