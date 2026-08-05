using Microsoft.Extensions.Logging;

namespace FortOS.Core;

/// <summary>Basic disk information.</summary>
public record DiskInfo
{
    /// <summary>Device path.</summary>
    public required string Path { get; init; }
    /// <summary>Disk model.</summary>
    public required string Model { get; init; }
    /// <summary>Disk serial number.</summary>
    public required string Serial { get; init; }
    /// <summary>Capacity in bytes.</summary>
    public long SizeBytes { get; init; }
    /// <summary>Interface type.</summary>
    public required string InterfaceType { get; init; }
    /// <summary>Whether it is an SSD.</summary>
    public bool IsSsd { get; init; }
    /// <summary>SMART status.</summary>
    public required string SmartStatus { get; init; }
    /// <summary>Temperature in Celsius.</summary>
    public int TemperatureCelsius { get; init; }
    /// <summary>Used percentage.</summary>
    public double UsedPercent { get; init; }
    /// <summary>Mount point if the disk (or a partition on it) is in use; null otherwise.</summary>
    public string? MountPoint { get; init; }
}

/// <summary>Block-device status for arbitrary devices (e.g. md RAID arrays), used to decide whether a device has been formatted/mounted.</summary>
public record DeviceStatus
{
    /// <summary>Device path.</summary>
    public required string Path { get; init; }
    /// <summary>Whether the device currently exists in the kernel block layer.</summary>
    public bool Exists { get; init; }
    /// <summary>Detected filesystem type (e.g. ext4, btrfs); null when unformatted.</summary>
    public string? FileSystem { get; init; }
    /// <summary>Mount point; null when not mounted.</summary>
    public string? MountPoint { get; init; }
    /// <summary>Capacity in bytes.</summary>
    public long SizeBytes { get; init; }
}

/// <summary>Service definition describing a service managed by the Service Bus.</summary>
public record ServiceDefinition
{
    /// <summary>Unique service ID.</summary>
    public required string ServiceId { get; init; }
    /// <summary>Service display name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Service type.</summary>
    public ServiceType Type { get; init; }
    /// <summary>List of dependent service IDs.</summary>
    public string[] DependsOn { get; init; } = [];
    /// <summary>Capabilities required by the service.</summary>
    public string[] RequiredCapabilities { get; init; } = [];
    /// <summary>Startup policy.</summary>
    public ServiceStartup Startup { get; init; }
    /// <summary>Restart policy.</summary>
    public RestartPolicy RestartPolicy { get; init; }
    /// <summary>Native process executable path.</summary>
    public string? Executable { get; init; }
    /// <summary>Native process command-line arguments.</summary>
    public string? Arguments { get; init; }
    /// <summary>systemd unit name.</summary>
    public string? SystemdUnit { get; init; }
    /// <summary>Container compose file path.</summary>
    public string? ComposeFile { get; init; }
    /// <summary>Health check configuration.</summary>
    public HealthCheckConfig? HealthCheck { get; init; }
    /// <summary>Resource quota.</summary>
    public ResourceQuota? Quota { get; init; }
}

/// <summary>Health check configuration.</summary>
public record HealthCheckConfig
{
    /// <summary>Check type.</summary>
    public HealthCheckType Type { get; init; }
    /// <summary>Check endpoint or command.</summary>
    public required string Endpoint { get; init; }
    /// <summary>Check interval in seconds.</summary>
    public int IntervalSeconds { get; init; } = 30;
    /// <summary>Timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 5;
    /// <summary>Number of retries on failure.</summary>
    public int Retries { get; init; } = 3;
    /// <summary>Startup grace period in seconds.</summary>
    public int StartPeriodSeconds { get; init; } = 10;
}

/// <summary>Service or container resource quota.</summary>
public record ResourceQuota
{
    /// <summary>CPU core limit.</summary>
    public double? CpuLimit { get; init; }
    /// <summary>Memory limit in bytes.</summary>
    public long? MemoryLimitBytes { get; init; }
    /// <summary>I/O weight.</summary>
    public int? IoWeight { get; init; }
}

/// <summary>Unified log entry.</summary>
public record LogEntry
{
    /// <summary>Unique log ID.</summary>
    public string LogId { get; init; } = Guid.CreateVersion7().ToString();
    /// <summary>Log timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Log category.</summary>
    public LogCategory Category { get; init; }
    /// <summary>Log level.</summary>
    public LogLevel Level { get; init; }
    /// <summary>Source component.</summary>
    public required string SourceComponent { get; init; }
    /// <summary>Source layer.</summary>
    public string? SourceLayer { get; init; }
    /// <summary>Host name.</summary>
    public string? HostName { get; init; }
    /// <summary>Host architecture.</summary>
    public string? HostArch { get; init; }
    /// <summary>Related user.</summary>
    public string? UserId { get; init; }
    /// <summary>Related Agent.</summary>
    public string? AgentId { get; init; }
    /// <summary>Related service.</summary>
    public string? ServiceId { get; init; }
    /// <summary>Trace ID.</summary>
    public string? TraceId { get; init; }
    /// <summary>Span ID.</summary>
    public string? SpanId { get; init; }
    /// <summary>Log message.</summary>
    public required string Message { get; init; }
    /// <summary>Message template.</summary>
    public string? Template { get; init; }
    /// <summary>Structured properties.</summary>
    public Dictionary<string, object> Properties { get; init; } = [];
    /// <summary>Log tags.</summary>
    public string[] Tags { get; init; } = [];
    /// <summary>Audit extension.</summary>
    public AuditDetail? Audit { get; init; }
    /// <summary>Metric extension.</summary>
    public MetricData? Metric { get; init; }
}

/// <summary>Audit log extension information.</summary>
public record AuditDetail
{
    /// <summary>Action name.</summary>
    public required string Action { get; init; }
    /// <summary>Resource path or ID.</summary>
    public required string Resource { get; init; }
    /// <summary>Resource type.</summary>
    public required string ResourceType { get; init; }
    /// <summary>Required permission.</summary>
    public string? PermissionRequired { get; init; }
    /// <summary>Whether access is granted.</summary>
    public bool Granted { get; init; }
    /// <summary>Client IP.</summary>
    public string? ClientIp { get; init; }
    /// <summary>User agent.</summary>
    public string? UserAgent { get; init; }
    /// <summary>Session ID.</summary>
    public string? SessionId { get; init; }
    /// <summary>State before change (JSON).</summary>
    public string? BeforeState { get; init; }
    /// <summary>State after change (JSON).</summary>
    public string? AfterState { get; init; }
    /// <summary>Previous audit hash.</summary>
    public string? PreviousHash { get; init; }
    /// <summary>Current audit hash.</summary>
    public required string CurrentHash { get; init; }
    /// <summary>Audit chain signature.</summary>
    public required string ChainSignature { get; init; }
}

/// <summary>Metric data.</summary>
public record MetricData
{
    /// <summary>Metric name.</summary>
    public required string MetricName { get; init; }
    /// <summary>Metric value.</summary>
    public double Value { get; init; }
    /// <summary>Metric unit.</summary>
    public required string Unit { get; init; }
    /// <summary>Metric dimensions.</summary>
    public Dictionary<string, string> Dimensions { get; init; } = [];
    /// <summary>Collection time.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Log query filter.</summary>
public record LogQuery
{
    /// <summary>Log category filter.</summary>
    public LogCategory? Category { get; init; }
    /// <summary>Minimum log level.</summary>
    public LogLevel? MinLevel { get; init; }
    /// <summary>Start time.</summary>
    public DateTimeOffset? From { get; init; }
    /// <summary>End time.</summary>
    public DateTimeOffset? To { get; init; }
    /// <summary>Search text.</summary>
    public string? SearchText { get; init; }
    /// <summary>Tag filter.</summary>
    public string[]? Tags { get; init; }
    /// <summary>Service ID filter.</summary>
    public string? ServiceId { get; init; }
    /// <summary>Agent ID filter.</summary>
    public string? AgentId { get; init; }
    /// <summary>Trace ID filter.</summary>
    public string? TraceId { get; init; }
    /// <summary>Return count.</summary>
    public int Limit { get; init; } = 100;
    /// <summary>Offset.</summary>
    public int Offset { get; init; }
}

/// <summary>Module execution context.</summary>
public record ModuleContext
{
    /// <summary>Service provider.</summary>
    public required IServiceProvider Services { get; init; }
    /// <summary>Event bus.</summary>
    public required IEventBus EventBus { get; init; }
    /// <summary>Logger factory.</summary>
    public required ILoggerFactory LoggerFactory { get; init; }
    /// <summary>Module data directory.</summary>
    public required string DataDirectory { get; init; }
}

/// <summary>Service status information.</summary>
public record ServiceStatusInfo
{
    /// <summary>Service ID.</summary>
    public required string ServiceId { get; init; }
    /// <summary>Running status.</summary>
    public ServiceStatus Status { get; init; }
    /// <summary>Service type.</summary>
    public ServiceType Type { get; init; }
    /// <summary>Process ID.</summary>
    public int? Pid { get; init; }
    /// <summary>CPU usage percentage.</summary>
    public double CpuPercent { get; init; }
    /// <summary>Memory in bytes.</summary>
    public long MemoryBytes { get; init; }
    /// <summary>Uptime.</summary>
    public TimeSpan Uptime { get; init; }
    /// <summary>Last error.</summary>
    public string? LastError { get; init; }
}

/// <summary>Event bus message envelope.</summary>
public record EventEnvelope
{
    /// <summary>Event ID.</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    /// <summary>Topic.</summary>
    public required string Topic { get; init; }
    /// <summary>Event type.</summary>
    public required string Type { get; init; }
    /// <summary>JSON data.</summary>
    public required string DataJson { get; init; }
    /// <summary>Event time.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Trace ID.</summary>
    public string? TraceId { get; init; }
    /// <summary>Source service ID.</summary>
    public string? SourceServiceId { get; init; }
}

/// <summary>Health check result.</summary>
public record HealthCheckResult
{
    /// <summary>Service ID.</summary>
    public required string ServiceId { get; init; }
    /// <summary>Health status.</summary>
    public HealthStatus Status { get; init; }
    /// <summary>Response time.</summary>
    public TimeSpan ResponseTime { get; init; }
    /// <summary>Error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Check time.</summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Consecutive failures.</summary>
    public int ConsecutiveFailures { get; init; }
    /// <summary>Consecutive successes.</summary>
    public int ConsecutiveSuccesses { get; init; }
}

/// <summary>Process startup configuration.</summary>
public record ProcessStartConfig
{
    /// <summary>Executable file path.</summary>
    public required string ExecutablePath { get; init; }
    /// <summary>Command-line arguments.</summary>
    public string? Arguments { get; init; }
    /// <summary>Working directory.</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>Environment variables.</summary>
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
    /// <summary>Standard input content; used to securely pass sensitive data to commands like chpasswd, smbpasswd, etc.</summary>
    public string? StandardInput { get; init; }
    /// <summary>Timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>Process information.</summary>
public record ProcessInfo
{
    /// <summary>Process ID.</summary>
    public int Pid { get; init; }
    /// <summary>Process name.</summary>
    public required string ProcessName { get; init; }
    /// <summary>Command line.</summary>
    public string? CommandLine { get; init; }
    /// <summary>CPU usage percentage.</summary>
    public double CpuPercent { get; init; }
    /// <summary>Memory in bytes.</summary>
    public long MemoryBytes { get; init; }
    /// <summary>Start time.</summary>
    public DateTimeOffset StartTime { get; init; }
}

/// <summary>Storage quota definition.</summary>
public record StorageQuota
{
    /// <summary>Quota target.</summary>
    public required string TargetId { get; init; }
    /// <summary>Quota type.</summary>
    public QuotaType Type { get; init; }
    /// <summary>Hard limit in bytes.</summary>
    public long? HardLimitBytes { get; init; }
    /// <summary>Soft limit in bytes.</summary>
    public long? SoftLimitBytes { get; init; }
    /// <summary>Soft limit grace period in seconds.</summary>
    public long GracePeriodSeconds { get; init; } = 604800;
    /// <summary>Hard limit on inodes.</summary>
    public long? HardLimitInodes { get; init; }
    /// <summary>Current bytes used.</summary>
    public long? UsedBytes { get; init; }
    /// <summary>Current inodes used.</summary>
    public long? UsedInodes { get; init; }
    /// <summary>Usage percentage.</summary>
    public double UsedPercent => HardLimitBytes.HasValue && HardLimitBytes.Value > 0 ? (double)(UsedBytes ?? 0) / HardLimitBytes.Value * 100 : 0;
}

/// <summary>Authentication result.</summary>
public record AuthResult
{
    /// <summary>Whether successful.</summary>
    public bool Success { get; init; }
    /// <summary>NAS token.</summary>
    public string? NasToken { get; init; }
    /// <summary>Error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Token payload.</summary>
    public object? TokenPayload { get; init; }
}

/// <summary>Permission check result.</summary>
public record PermissionResult
{
    /// <summary>Whether authorized.</summary>
    public bool Granted { get; init; }
    /// <summary>Denial reason.</summary>
    public string? DenyReason { get; init; }
    /// <summary>Matched capability.</summary>
    public string? MatchedCapability { get; init; }
    /// <summary>Required data level.</summary>
    public NasDataLevel RequiredDataLevel { get; init; }
}

/// <summary>Agent template.</summary>
public record AgentTemplate
{
    /// <summary>Template ID.</summary>
    public required string Id { get; init; }
    /// <summary>Template name.</summary>
    public required string Name { get; init; }
    /// <summary>Template version.</summary>
    public required string Version { get; init; }
    /// <summary>Template description.</summary>
    public string? Description { get; init; }
    /// <summary>Template logo: emoji or image URL (http/https).</summary>
    public string? Logo { get; init; }
    /// <summary>Required capabilities.</summary>
    public string[] CapabilitiesRequired { get; init; } = [];
    /// <summary>Template parameters.</summary>
    public AgentTemplateParameter[] Parameters { get; init; } = [];
    /// <summary>External access / integration notes shown after deployment (web UI URL hints, chat channel setup, ...).</summary>
    public string[] AccessNotes { get; init; } = [];
    /// <summary>Raw Compose template.</summary>
    public required string ComposeTemplate { get; init; }
}

/// <summary>Agent template parameter.</summary>
public record AgentTemplateParameter
{
    /// <summary>Parameter name.</summary>
    public required string Name { get; init; }
    /// <summary>Parameter type.</summary>
    public required string Type { get; init; }
    /// <summary>Whether required.</summary>
    public bool Required { get; init; }
    /// <summary>Default value.</summary>
    public string? Default { get; init; }
}

/// <summary>Agent deployment configuration.</summary>
public record AgentConfig
{
    /// <summary>Agent ID.</summary>
    public required string AgentId { get; init; }
    /// <summary>Display name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Image name.</summary>
    public required string ImageName { get; init; }
    /// <summary>Granted capabilities.</summary>
    public string[] Capabilities { get; init; } = [];
    /// <summary>Volume mappings.</summary>
    public VolumeMapping[] VolumeMapping { get; init; } = [];
    /// <summary>Port mappings.</summary>
    public PortMapping[] PortMapping { get; init; } = [];
    /// <summary>Resource quota.</summary>
    public ResourceQuota? ResourceQuota { get; init; }
    /// <summary>Additional environment variables merged over template defaults (written to the agent .env file).</summary>
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Volume mapping.</summary>
public record VolumeMapping
{
    /// <summary>Host path.</summary>
    public required string HostPath { get; init; }
    /// <summary>Container path.</summary>
    public required string ContainerPath { get; init; }
    /// <summary>Whether read-only.</summary>
    public bool ReadOnly { get; init; }
}

/// <summary>Port mapping.</summary>
public record PortMapping
{
    /// <summary>Host port.</summary>
    public int HostPort { get; init; }
    /// <summary>Container port.</summary>
    public int ContainerPort { get; init; }
    /// <summary>Protocol.</summary>
    public string Protocol { get; init; } = "tcp";
}

/// <summary>Deployed agent external-access manifest: ports, environment variable names, and access notes.</summary>
public record AgentAccessInfo
{
    /// <summary>Agent ID.</summary>
    public required string AgentId { get; init; }
    /// <summary>Source template ID.</summary>
    public required string TemplateId { get; init; }
    /// <summary>Deployed image name.</summary>
    public required string ImageName { get; init; }
    /// <summary>Display name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Published ports.</summary>
    public AgentPortInfo[] Ports { get; init; } = [];
    /// <summary>Environment variable names available for external integration (values live in the agent .env file).</summary>
    public AgentEnvInfo[] Env { get; init; } = [];
    /// <summary>External access / integration notes from the template.</summary>
    public string[] AccessNotes { get; init; } = [];
}

/// <summary>Published agent port.</summary>
public record AgentPortInfo(int HostPort, int ContainerPort, string Protocol = "tcp");

/// <summary>Agent environment variable entry (value is never exposed, only whether it is configured).</summary>
public record AgentEnvInfo(string Name, bool Set);

/// <summary>Agent token issuance result.</summary>
public record AgentTokenResult
{
    /// <summary>Token text.</summary>
    [LogMasked]
    public required string Token { get; init; }
    /// <summary>Agent ID.</summary>
    public required string AgentId { get; init; }
    /// <summary>Token capabilities.</summary>
    public string[] Capabilities { get; init; } = [];
    /// <summary>Expiration time.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Issued time.</summary>
    public DateTimeOffset IssuedAt { get; init; }
}

/// <summary>Compose generation result.</summary>
public record ComposeGenerationResult
{
    /// <summary>Compose file path.</summary>
    public required string ComposeFilePath { get; init; }
    /// <summary>Environment variable file path.</summary>
    public required string EnvFilePath { get; init; }
    /// <summary>Agent ID.</summary>
    public required string AgentId { get; init; }
    /// <summary>Injected token.</summary>
    [LogMasked]
    public required string Token { get; init; }
}

/// <summary>Alert rule.</summary>
public record AlertRule
{
    /// <summary>Rule ID.</summary>
    public required string RuleId { get; init; }
    /// <summary>Name.</summary>
    public required string Name { get; init; }
    /// <summary>Description.</summary>
    public required string Description { get; init; }
    /// <summary>Severity level.</summary>
    public required string Severity { get; init; }
    /// <summary>Trigger condition.</summary>
    public required AlertCondition Condition { get; init; }
    /// <summary>Action list.</summary>
    public string[] Actions { get; init; } = [];
    /// <summary>Cooldown time in seconds.</summary>
    public int CooldownSeconds { get; init; }
    /// <summary>Suppression window.</summary>
    public AlertSuppress? Suppress { get; init; }
}

/// <summary>Alert condition.</summary>
public record AlertCondition
{
    /// <summary>Condition type.</summary>
    public required string Type { get; init; }
    /// <summary>Event topic.</summary>
    public string? Topic { get; init; }
    /// <summary>Metric name.</summary>
    public string? Metric { get; init; }
    /// <summary>Comparison operator.</summary>
    public string? Operator { get; init; }
    /// <summary>Threshold value.</summary>
    public double? Value { get; init; }
    /// <summary>Duration in seconds.</summary>
    public int? DurationSeconds { get; init; }
    /// <summary>Count.</summary>
    public int? Count { get; init; }
    /// <summary>Window in seconds.</summary>
    public int? WithinSeconds { get; init; }
}

/// <summary>Alert suppression configuration.</summary>
public record AlertSuppress
{
    /// <summary>Suppression window expression.</summary>
    public string? Window { get; init; }
}

/// <summary>Active alert.</summary>
public record ActiveAlert
{
    /// <summary>Alert ID.</summary>
    public required string AlertId { get; init; }
    /// <summary>Rule ID.</summary>
    public required string RuleId { get; init; }
    /// <summary>Severity level.</summary>
    public required string Severity { get; init; }
    /// <summary>Alert message.</summary>
    public required string Message { get; init; }
    /// <summary>Trigger time.</summary>
    public DateTimeOffset TriggeredAt { get; init; }
    /// <summary>
    /// Metric dimensions that identify the affected resource. These prevent one healthy disk,
    /// interface, service, or container from resolving another resource's alert.
    /// </summary>
    public Dictionary<string, string> Dimensions { get; init; } = [];
}

/// <summary>Backup task definition.</summary>
public record BackupTask
{
    /// <summary>Task ID.</summary>
    public required string TaskId { get; init; }
    /// <summary>Task name.</summary>
    public required string Name { get; init; }
    /// <summary>Source path.</summary>
    public required string SourcePath { get; init; }
    /// <summary>Backup target.</summary>
    public required BackupTarget Target { get; init; }
    /// <summary>Cron schedule expression.</summary>
    public required string CronExpression { get; init; }
    /// <summary>Whether enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Backup method.</summary>
    public BackupMethod Method { get; init; }
    /// <summary>Retention days.</summary>
    public int RetentionDays { get; init; } = 30;
    /// <summary>Retention count.</summary>
    public int RetentionCount { get; init; } = 10;
    /// <summary>Whether compression is enabled.</summary>
    public bool Compression { get; init; } = true;
    /// <summary>Whether encryption is enabled.</summary>
    public bool Encryption { get; init; } = true;
    /// <summary>Exclude patterns.</summary>
    public string[] ExcludePatterns { get; init; } = [];
    /// <summary>Maximum retries after failure.</summary>
    public int RetryCount { get; init; } = 2;
    /// <summary>Retry initial backoff in seconds.</summary>
    public int RetryBackoffSeconds { get; init; } = 5;
    /// <summary>Maximum allowed staleness in hours.</summary>
    public int FreshnessSlaHours { get; init; } = 24;
}

/// <summary>Backup target.</summary>
public record BackupTarget
{
    /// <summary>Target type.</summary>
    public BackupTargetType Type { get; init; }
    /// <summary>Connection string.</summary>
    public required string ConnectionString { get; init; }
    /// <summary>Bucket name or path.</summary>
    public required string BucketOrPath { get; init; }
    /// <summary>Access key.</summary>
    [LogMasked]
    public string? AccessKey { get; init; }
    /// <summary>Secret key.</summary>
    [LogMasked]
    public string? SecretKey { get; init; }
}

/// <summary>Backup target type.</summary>
public enum BackupTargetType
{
    /// <summary>Local target.</summary>
    Local,
    /// <summary>Remote NAS.</summary>
    RemoteNas,
    /// <summary>S3-compatible storage.</summary>
    S3,
    /// <summary>Backblaze B2.</summary>
    B2,
    /// <summary>WebDAV.</summary>
    WebDAV,
    /// <summary>SFTP.</summary>
    SFTP
}

/// <summary>Backup method.</summary>
public enum BackupMethod
{
    /// <summary>Incremental backup.</summary>
    Incremental,
    /// <summary>Full backup.</summary>
    Full,
    /// <summary>Mirror sync.</summary>
    Mirror
}

/// <summary>Share definition.</summary>
public record ShareDefinition
{
    /// <summary>Share ID.</summary>
    public required string ShareId { get; init; }
    /// <summary>Share name.</summary>
    public required string Name { get; init; }
    /// <summary>Share path.</summary>
    public required string Path { get; init; }
    /// <summary>Protocol list.</summary>
    public string[] Protocols { get; init; } = [];
    /// <summary>Whether read-only.</summary>
    public bool ReadOnly { get; init; }
    /// <summary>Description.</summary>
    public string? Description { get; init; }
}

/// <summary>Snapshot information.</summary>
public record SnapshotInfo
{
    /// <summary>Snapshot ID.</summary>
    public required string SnapshotId { get; init; }
    /// <summary>Snapshot target.</summary>
    public required string Target { get; init; }
    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Size in bytes.</summary>
    public long? SizeBytes { get; init; }
    /// <summary>Description.</summary>
    public string? Description { get; init; }
}

/// <summary>Token validation result.</summary>
public record TokenValidationResult
{
    /// <summary>Whether valid.</summary>
    public bool IsValid { get; init; }
    /// <summary>Payload object.</summary>
    public object? Payload { get; init; }
    /// <summary>Error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Token ID (JTI).</summary>
    public string? Jti { get; init; }
    /// <summary>Subject.</summary>
    public string? Subject { get; init; }
    /// <summary>Token type.</summary>
    public TokenType? TokenType { get; init; }
    /// <summary>Capability list.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    /// <summary>Expiration time.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Audit chain verification result.</summary>
public record ChainVerificationResult
{
    /// <summary>Whether fully valid.</summary>
    public bool IsValid { get; init; }
    /// <summary>Total entries.</summary>
    public long TotalEntries { get; init; }
    /// <summary>First broken sequence number.</summary>
    public long? BrokenAtSequence { get; init; }
    /// <summary>Verification message.</summary>
    public string? Message { get; init; }
}

/// <summary>Command execution result.</summary>
public record CommandResult
{
    /// <summary>Exit code.</summary>
    public int ExitCode { get; init; }
    /// <summary>Standard output.</summary>
    public string Stdout { get; init; } = string.Empty;
    /// <summary>Standard error.</summary>
    public string Stderr { get; init; } = string.Empty;
}

/// <summary>Partition specification.</summary>
public record PartitionSpec
{
    /// <summary>Partition name.</summary>
    public required string Name { get; init; }
    /// <summary>File system type.</summary>
    public string? FileSystem { get; init; }
    /// <summary>Start bytes.</summary>
    public long? StartBytes { get; init; }
    /// <summary>Size in bytes.</summary>
    public long? SizeBytes { get; init; }
}

/// <summary>Partition operation result.</summary>
public record PartitionResult
{
    /// <summary>Whether successful.</summary>
    public bool Success { get; init; }
    /// <summary>Partition path.</summary>
    public string? PartitionPath { get; init; }
    /// <summary>Message.</summary>
    public string? Message { get; init; }
}

/// <summary>RAID level.</summary>
public enum RaidLevel
{
    /// <summary>Unknown level.</summary>
    Unknown,
    /// <summary>RAID 0.</summary>
    Raid0,
    /// <summary>RAID 1.</summary>
    Raid1,
    /// <summary>RAID 5.</summary>
    Raid5,
    /// <summary>RAID 6.</summary>
    Raid6,
    /// <summary>RAID 10.</summary>
    Raid10
}

/// <summary>RAID operation result.</summary>
public record RaidResult
{
    /// <summary>Whether successful.</summary>
    public bool Success { get; init; }
    /// <summary>Storage pool ID.</summary>
    public string? PoolId { get; init; }
    /// <summary>Message.</summary>
    public string? Message { get; init; }
    /// <summary>Error code.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>SMART data.</summary>
public record SmartData
{
    /// <summary>Disk path.</summary>
    public required string DiskPath { get; init; }
    /// <summary>Health status.</summary>
    public required string Health { get; init; }
    /// <summary>Temperature in Celsius.</summary>
    public int? TemperatureCelsius { get; init; }
    /// <summary>Raw JSON.</summary>
    public string? RawJson { get; init; }
}

/// <summary>File system information.</summary>
public record FsInfo
{
    /// <summary>Mount point.</summary>
    public required string MountPoint { get; init; }
    /// <summary>File system type.</summary>
    public required string FileSystemType { get; init; }
    /// <summary>Total bytes.</summary>
    public long TotalBytes { get; init; }
    /// <summary>Available bytes.</summary>
    public long AvailableBytes { get; init; }
    /// <summary>Used bytes.</summary>
    public long UsedBytes { get; init; }
}

/// <summary>Network interface information.</summary>
public record NetworkInterfaceInfo
{
    /// <summary>Interface name.</summary>
    public required string Name { get; init; }
    /// <summary>MAC address.</summary>
    public string? MacAddress { get; init; }
    /// <summary>IP addresses.</summary>
    public string[] Addresses { get; init; } = [];
    /// <summary>Whether enabled.</summary>
    public bool IsUp { get; init; }
    /// <summary>Speed in Mbps.</summary>
    public long? SpeedMbps { get; init; }
}

/// <summary>Network configuration.</summary>
public record NetConfig
{
    /// <summary>Whether DHCP is enabled.</summary>
    public bool Dhcp { get; init; }
    /// <summary>Static address.</summary>
    public string? Address { get; init; }
    /// <summary>Gateway.</summary>
    public string? Gateway { get; init; }
    /// <summary>DNS servers.</summary>
    public string[] DnsServers { get; init; } = [];
}

/// <summary>Firewall rule.</summary>
public record FirewallRule
{
    /// <summary>Rule ID.</summary>
    public required string RuleId { get; init; }
    /// <summary>Action.</summary>
    public required string Action { get; init; }
    /// <summary>Protocol.</summary>
    public string? Protocol { get; init; }
    /// <summary>Port.</summary>
    public int? Port { get; init; }
    /// <summary>Source address.</summary>
    public string? Source { get; init; }
}
