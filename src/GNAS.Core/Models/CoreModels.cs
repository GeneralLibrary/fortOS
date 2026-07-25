using Microsoft.Extensions.Logging;

namespace GNAS.Core;

/// <summary>磁盘基础信息。</summary>
public record DiskInfo
{
    /// <summary>设备路径。</summary>
    public required string Path { get; init; }
    /// <summary>磁盘型号。</summary>
    public required string Model { get; init; }
    /// <summary>磁盘序列号。</summary>
    public required string Serial { get; init; }
    /// <summary>容量字节数。</summary>
    public long SizeBytes { get; init; }
    /// <summary>接口类型。</summary>
    public required string InterfaceType { get; init; }
    /// <summary>是否为固态硬盘。</summary>
    public bool IsSsd { get; init; }
    /// <summary>SMART 状态。</summary>
    public required string SmartStatus { get; init; }
    /// <summary>温度摄氏度。</summary>
    public int TemperatureCelsius { get; init; }
    /// <summary>已使用百分比。</summary>
    public double UsedPercent { get; init; }
}

/// <summary>服务定义，描述受 Service Bus 管理的服务。</summary>
public record ServiceDefinition
{
    /// <summary>服务唯一标识。</summary>
    public required string ServiceId { get; init; }
    /// <summary>服务显示名称。</summary>
    public required string DisplayName { get; init; }
    /// <summary>服务类型。</summary>
    public ServiceType Type { get; init; }
    /// <summary>依赖服务标识列表。</summary>
    public string[] DependsOn { get; init; } = [];
    /// <summary>服务所需能力表达式。</summary>
    public string[] RequiredCapabilities { get; init; } = [];
    /// <summary>启动策略。</summary>
    public ServiceStartup Startup { get; init; }
    /// <summary>重启策略。</summary>
    public RestartPolicy RestartPolicy { get; init; }
    /// <summary>原生进程可执行文件路径。</summary>
    public string? Executable { get; init; }
    /// <summary>原生进程命令行参数。</summary>
    public string? Arguments { get; init; }
    /// <summary>systemd 单元名称。</summary>
    public string? SystemdUnit { get; init; }
    /// <summary>容器 compose 文件路径。</summary>
    public string? ComposeFile { get; init; }
    /// <summary>健康检查配置。</summary>
    public HealthCheckConfig? HealthCheck { get; init; }
    /// <summary>资源配额。</summary>
    public ResourceQuota? Quota { get; init; }
}

/// <summary>健康检查配置。</summary>
public record HealthCheckConfig
{
    /// <summary>检查类型。</summary>
    public HealthCheckType Type { get; init; }
    /// <summary>检查端点或命令。</summary>
    public required string Endpoint { get; init; }
    /// <summary>检查间隔秒数。</summary>
    public int IntervalSeconds { get; init; } = 30;
    /// <summary>超时秒数。</summary>
    public int TimeoutSeconds { get; init; } = 5;
    /// <summary>失败重试次数。</summary>
    public int Retries { get; init; } = 3;
    /// <summary>启动宽限期秒数。</summary>
    public int StartPeriodSeconds { get; init; } = 10;
}

/// <summary>服务或容器资源配额。</summary>
public record ResourceQuota
{
    /// <summary>CPU 核数上限。</summary>
    public double? CpuLimit { get; init; }
    /// <summary>内存字节上限。</summary>
    public long? MemoryLimitBytes { get; init; }
    /// <summary>IO 权重。</summary>
    public int? IoWeight { get; init; }
}

/// <summary>统一日志条目。</summary>
public record LogEntry
{
    /// <summary>日志唯一标识。</summary>
    public string LogId { get; init; } = Guid.CreateVersion7().ToString();
    /// <summary>日志时间。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>日志类别。</summary>
    public LogCategory Category { get; init; }
    /// <summary>日志级别。</summary>
    public LogLevel Level { get; init; }
    /// <summary>来源组件。</summary>
    public required string SourceComponent { get; init; }
    /// <summary>来源层级。</summary>
    public string? SourceLayer { get; init; }
    /// <summary>主机名。</summary>
    public string? HostName { get; init; }
    /// <summary>主机架构。</summary>
    public string? HostArch { get; init; }
    /// <summary>关联用户。</summary>
    public string? UserId { get; init; }
    /// <summary>关联 Agent。</summary>
    public string? AgentId { get; init; }
    /// <summary>关联服务。</summary>
    public string? ServiceId { get; init; }
    /// <summary>链路追踪标识。</summary>
    public string? TraceId { get; init; }
    /// <summary>调用跨度标识。</summary>
    public string? SpanId { get; init; }
    /// <summary>日志消息。</summary>
    public required string Message { get; init; }
    /// <summary>消息模板。</summary>
    public string? Template { get; init; }
    /// <summary>结构化属性。</summary>
    public Dictionary<string, object> Properties { get; init; } = [];
    /// <summary>日志标签。</summary>
    public string[] Tags { get; init; } = [];
    /// <summary>审计扩展。</summary>
    public AuditDetail? Audit { get; init; }
    /// <summary>指标扩展。</summary>
    public MetricData? Metric { get; init; }
}

/// <summary>审计日志扩展信息。</summary>
public record AuditDetail
{
    /// <summary>操作名称。</summary>
    public required string Action { get; init; }
    /// <summary>资源路径或标识。</summary>
    public required string Resource { get; init; }
    /// <summary>资源类型。</summary>
    public required string ResourceType { get; init; }
    /// <summary>所需权限。</summary>
    public string? PermissionRequired { get; init; }
    /// <summary>是否放行。</summary>
    public bool Granted { get; init; }
    /// <summary>客户端 IP。</summary>
    public string? ClientIp { get; init; }
    /// <summary>用户代理。</summary>
    public string? UserAgent { get; init; }
    /// <summary>会话标识。</summary>
    public string? SessionId { get; init; }
    /// <summary>变更前状态 JSON。</summary>
    public string? BeforeState { get; init; }
    /// <summary>变更后状态 JSON。</summary>
    public string? AfterState { get; init; }
    /// <summary>前一条审计哈希。</summary>
    public string? PreviousHash { get; init; }
    /// <summary>当前审计哈希。</summary>
    public required string CurrentHash { get; init; }
    /// <summary>审计链签名。</summary>
    public required string ChainSignature { get; init; }
}

/// <summary>指标数据。</summary>
public record MetricData
{
    /// <summary>指标名称。</summary>
    public required string MetricName { get; init; }
    /// <summary>指标数值。</summary>
    public double Value { get; init; }
    /// <summary>指标单位。</summary>
    public required string Unit { get; init; }
    /// <summary>指标维度。</summary>
    public Dictionary<string, string> Dimensions { get; init; } = [];
    /// <summary>采集时间。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>日志查询条件。</summary>
public record LogQuery
{
    /// <summary>日志类别过滤。</summary>
    public LogCategory? Category { get; init; }
    /// <summary>最低日志级别。</summary>
    public LogLevel? MinLevel { get; init; }
    /// <summary>起始时间。</summary>
    public DateTimeOffset? From { get; init; }
    /// <summary>结束时间。</summary>
    public DateTimeOffset? To { get; init; }
    /// <summary>搜索文本。</summary>
    public string? SearchText { get; init; }
    /// <summary>标签过滤。</summary>
    public string[]? Tags { get; init; }
    /// <summary>服务标识过滤。</summary>
    public string? ServiceId { get; init; }
    /// <summary>Agent 标识过滤。</summary>
    public string? AgentId { get; init; }
    /// <summary>Trace 标识过滤。</summary>
    public string? TraceId { get; init; }
    /// <summary>返回数量。</summary>
    public int Limit { get; init; } = 100;
    /// <summary>偏移量。</summary>
    public int Offset { get; init; }
}

/// <summary>模块运行上下文。</summary>
public record ModuleContext
{
    /// <summary>服务提供器。</summary>
    public required IServiceProvider Services { get; init; }
    /// <summary>事件总线。</summary>
    public required IEventBus EventBus { get; init; }
    /// <summary>日志工厂。</summary>
    public required ILoggerFactory LoggerFactory { get; init; }
    /// <summary>模块数据目录。</summary>
    public required string DataDirectory { get; init; }
}

/// <summary>服务状态信息。</summary>
public record ServiceStatusInfo
{
    /// <summary>服务标识。</summary>
    public required string ServiceId { get; init; }
    /// <summary>运行状态。</summary>
    public ServiceStatus Status { get; init; }
    /// <summary>服务类型。</summary>
    public ServiceType Type { get; init; }
    /// <summary>进程标识。</summary>
    public int? Pid { get; init; }
    /// <summary>CPU 使用率。</summary>
    public double CpuPercent { get; init; }
    /// <summary>内存字节数。</summary>
    public long MemoryBytes { get; init; }
    /// <summary>运行时长。</summary>
    public TimeSpan Uptime { get; init; }
    /// <summary>最近错误。</summary>
    public string? LastError { get; init; }
}

/// <summary>事件总线消息信封。</summary>
public record EventEnvelope
{
    /// <summary>事件标识。</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    /// <summary>主题。</summary>
    public required string Topic { get; init; }
    /// <summary>事件类型。</summary>
    public required string Type { get; init; }
    /// <summary>JSON 数据。</summary>
    public required string DataJson { get; init; }
    /// <summary>事件时间。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>链路追踪标识。</summary>
    public string? TraceId { get; init; }
    /// <summary>来源服务标识。</summary>
    public string? SourceServiceId { get; init; }
}

/// <summary>健康检查结果。</summary>
public record HealthCheckResult
{
    /// <summary>服务标识。</summary>
    public required string ServiceId { get; init; }
    /// <summary>健康状态。</summary>
    public HealthStatus Status { get; init; }
    /// <summary>响应耗时。</summary>
    public TimeSpan ResponseTime { get; init; }
    /// <summary>错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>检查时间。</summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>连续失败次数。</summary>
    public int ConsecutiveFailures { get; init; }
    /// <summary>连续成功次数。</summary>
    public int ConsecutiveSuccesses { get; init; }
}

/// <summary>进程启动配置。</summary>
public record ProcessStartConfig
{
    /// <summary>可执行文件路径。</summary>
    public required string ExecutablePath { get; init; }
    /// <summary>命令行参数。</summary>
    public string? Arguments { get; init; }
    /// <summary>工作目录。</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>环境变量。</summary>
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
    /// <summary>标准输入内容；用于向 chpasswd、smbpasswd 等命令安全传递敏感数据。</summary>
    public string? StandardInput { get; init; }
    /// <summary>超时秒数。</summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>进程信息。</summary>
public record ProcessInfo
{
    /// <summary>进程标识。</summary>
    public int Pid { get; init; }
    /// <summary>进程名称。</summary>
    public required string ProcessName { get; init; }
    /// <summary>命令行。</summary>
    public string? CommandLine { get; init; }
    /// <summary>CPU 使用率。</summary>
    public double CpuPercent { get; init; }
    /// <summary>内存字节数。</summary>
    public long MemoryBytes { get; init; }
    /// <summary>启动时间。</summary>
    public DateTimeOffset StartTime { get; init; }
}

/// <summary>存储配额定义。</summary>
public record StorageQuota
{
    /// <summary>配额目标。</summary>
    public required string TargetId { get; init; }
    /// <summary>配额类型。</summary>
    public QuotaType Type { get; init; }
    /// <summary>硬限制字节数。</summary>
    public long? HardLimitBytes { get; init; }
    /// <summary>软限制字节数。</summary>
    public long? SoftLimitBytes { get; init; }
    /// <summary>软限制宽限期秒数。</summary>
    public long GracePeriodSeconds { get; init; } = 604800;
    /// <summary>文件数硬限制。</summary>
    public long? HardLimitInodes { get; init; }
    /// <summary>当前使用字节数。</summary>
    public long? UsedBytes { get; init; }
    /// <summary>当前使用 inode 数。</summary>
    public long? UsedInodes { get; init; }
    /// <summary>使用率百分比。</summary>
    public double UsedPercent => HardLimitBytes.HasValue && HardLimitBytes.Value > 0 ? (double)(UsedBytes ?? 0) / HardLimitBytes.Value * 100 : 0;
}

/// <summary>认证结果。</summary>
public record AuthResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>NAS 令牌。</summary>
    public string? NasToken { get; init; }
    /// <summary>错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>令牌载荷。</summary>
    public object? TokenPayload { get; init; }
}

/// <summary>权限检查结果。</summary>
public record PermissionResult
{
    /// <summary>是否授权。</summary>
    public bool Granted { get; init; }
    /// <summary>拒绝原因。</summary>
    public string? DenyReason { get; init; }
    /// <summary>匹配的能力。</summary>
    public string? MatchedCapability { get; init; }
    /// <summary>要求的数据级别。</summary>
    public NasDataLevel RequiredDataLevel { get; init; }
}

/// <summary>Agent 模板。</summary>
public record AgentTemplate
{
    /// <summary>模板标识。</summary>
    public required string Id { get; init; }
    /// <summary>模板名称。</summary>
    public required string Name { get; init; }
    /// <summary>模板版本。</summary>
    public required string Version { get; init; }
    /// <summary>模板描述。</summary>
    public string? Description { get; init; }
    /// <summary>要求的能力。</summary>
    public string[] CapabilitiesRequired { get; init; } = [];
    /// <summary>模板参数。</summary>
    public AgentTemplateParameter[] Parameters { get; init; } = [];
    /// <summary>原始 Compose 模板。</summary>
    public required string ComposeTemplate { get; init; }
}

/// <summary>Agent 模板参数。</summary>
public record AgentTemplateParameter
{
    /// <summary>参数名称。</summary>
    public required string Name { get; init; }
    /// <summary>参数类型。</summary>
    public required string Type { get; init; }
    /// <summary>是否必填。</summary>
    public bool Required { get; init; }
    /// <summary>默认值。</summary>
    public string? Default { get; init; }
}

/// <summary>Agent 部署配置。</summary>
public record AgentConfig
{
    /// <summary>Agent 标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>显示名称。</summary>
    public required string DisplayName { get; init; }
    /// <summary>镜像名称。</summary>
    public required string ImageName { get; init; }
    /// <summary>授权能力。</summary>
    public string[] Capabilities { get; init; } = [];
    /// <summary>卷映射。</summary>
    public VolumeMapping[] VolumeMapping { get; init; } = [];
    /// <summary>端口映射。</summary>
    public PortMapping[] PortMapping { get; init; } = [];
    /// <summary>资源配额。</summary>
    public ResourceQuota? ResourceQuota { get; init; }
}

/// <summary>卷映射。</summary>
public record VolumeMapping
{
    /// <summary>宿主机路径。</summary>
    public required string HostPath { get; init; }
    /// <summary>容器路径。</summary>
    public required string ContainerPath { get; init; }
    /// <summary>是否只读。</summary>
    public bool ReadOnly { get; init; }
}

/// <summary>端口映射。</summary>
public record PortMapping
{
    /// <summary>宿主机端口。</summary>
    public int HostPort { get; init; }
    /// <summary>容器端口。</summary>
    public int ContainerPort { get; init; }
    /// <summary>协议。</summary>
    public string Protocol { get; init; } = "tcp";
}

/// <summary>Agent 令牌签发结果。</summary>
public record AgentTokenResult
{
    /// <summary>令牌文本。</summary>
    [LogMasked]
    public required string Token { get; init; }
    /// <summary>Agent 标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>令牌能力。</summary>
    public string[] Capabilities { get; init; } = [];
    /// <summary>过期时间。</summary>
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>签发时间。</summary>
    public DateTimeOffset IssuedAt { get; init; }
}

/// <summary>Compose 生成结果。</summary>
public record ComposeGenerationResult
{
    /// <summary>Compose 文件路径。</summary>
    public required string ComposeFilePath { get; init; }
    /// <summary>环境变量文件路径。</summary>
    public required string EnvFilePath { get; init; }
    /// <summary>Agent 标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>注入令牌。</summary>
    [LogMasked]
    public required string Token { get; init; }
}

/// <summary>告警规则。</summary>
public record AlertRule
{
    /// <summary>规则标识。</summary>
    public required string RuleId { get; init; }
    /// <summary>名称。</summary>
    public required string Name { get; init; }
    /// <summary>描述。</summary>
    public required string Description { get; init; }
    /// <summary>严重级别。</summary>
    public required string Severity { get; init; }
    /// <summary>触发条件。</summary>
    public required AlertCondition Condition { get; init; }
    /// <summary>动作列表。</summary>
    public string[] Actions { get; init; } = [];
    /// <summary>冷却时间秒数。</summary>
    public int CooldownSeconds { get; init; }
    /// <summary>抑制窗口。</summary>
    public AlertSuppress? Suppress { get; init; }
}

/// <summary>告警条件。</summary>
public record AlertCondition
{
    /// <summary>条件类型。</summary>
    public required string Type { get; init; }
    /// <summary>事件主题。</summary>
    public string? Topic { get; init; }
    /// <summary>指标名称。</summary>
    public string? Metric { get; init; }
    /// <summary>比较操作符。</summary>
    public string? Operator { get; init; }
    /// <summary>阈值。</summary>
    public double? Value { get; init; }
    /// <summary>持续秒数。</summary>
    public int? DurationSeconds { get; init; }
    /// <summary>次数。</summary>
    public int? Count { get; init; }
    /// <summary>窗口秒数。</summary>
    public int? WithinSeconds { get; init; }
}

/// <summary>告警抑制配置。</summary>
public record AlertSuppress
{
    /// <summary>抑制窗口表达式。</summary>
    public string? Window { get; init; }
}

/// <summary>活跃告警。</summary>
public record ActiveAlert
{
    /// <summary>告警标识。</summary>
    public required string AlertId { get; init; }
    /// <summary>规则标识。</summary>
    public required string RuleId { get; init; }
    /// <summary>严重级别。</summary>
    public required string Severity { get; init; }
    /// <summary>告警消息。</summary>
    public required string Message { get; init; }
    /// <summary>触发时间。</summary>
    public DateTimeOffset TriggeredAt { get; init; }
}

/// <summary>备份任务定义。</summary>
public record BackupTask
{
    /// <summary>任务标识。</summary>
    public required string TaskId { get; init; }
    /// <summary>任务名称。</summary>
    public required string Name { get; init; }
    /// <summary>源路径。</summary>
    public required string SourcePath { get; init; }
    /// <summary>备份目标。</summary>
    public required BackupTarget Target { get; init; }
    /// <summary>Cron 调度表达式。</summary>
    public required string CronExpression { get; init; }
    /// <summary>是否启用。</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>备份方法。</summary>
    public BackupMethod Method { get; init; }
    /// <summary>保留天数。</summary>
    public int RetentionDays { get; init; } = 30;
    /// <summary>保留份数。</summary>
    public int RetentionCount { get; init; } = 10;
    /// <summary>是否压缩。</summary>
    public bool Compression { get; init; } = true;
    /// <summary>是否加密。</summary>
    public bool Encryption { get; init; } = true;
    /// <summary>排除模式。</summary>
    public string[] ExcludePatterns { get; init; } = [];
    /// <summary>失败后的最大重试次数。</summary>
    public int RetryCount { get; init; } = 2;
    /// <summary>重试初始退避秒数。</summary>
    public int RetryBackoffSeconds { get; init; } = 5;
    /// <summary>备份允许的最大陈旧时间小时。</summary>
    public int FreshnessSlaHours { get; init; } = 24;
}

/// <summary>备份目标。</summary>
public record BackupTarget
{
    /// <summary>目标类型。</summary>
    public BackupTargetType Type { get; init; }
    /// <summary>连接字符串。</summary>
    public required string ConnectionString { get; init; }
    /// <summary>桶名或路径。</summary>
    public required string BucketOrPath { get; init; }
    /// <summary>访问密钥。</summary>
    [LogMasked]
    public string? AccessKey { get; init; }
    /// <summary>秘密密钥。</summary>
    [LogMasked]
    public string? SecretKey { get; init; }
}

/// <summary>备份目标类型。</summary>
public enum BackupTargetType
{
    /// <summary>本地目标。</summary>
    Local,
    /// <summary>远程 NAS。</summary>
    RemoteNas,
    /// <summary>S3 兼容存储。</summary>
    S3,
    /// <summary>Backblaze B2。</summary>
    B2,
    /// <summary>WebDAV。</summary>
    WebDAV,
    /// <summary>SFTP。</summary>
    SFTP
}

/// <summary>备份方法。</summary>
public enum BackupMethod
{
    /// <summary>增量备份。</summary>
    Incremental,
    /// <summary>完整备份。</summary>
    Full,
    /// <summary>镜像同步。</summary>
    Mirror
}

/// <summary>共享定义。</summary>
public record ShareDefinition
{
    /// <summary>共享标识。</summary>
    public required string ShareId { get; init; }
    /// <summary>共享名称。</summary>
    public required string Name { get; init; }
    /// <summary>共享路径。</summary>
    public required string Path { get; init; }
    /// <summary>协议列表。</summary>
    public string[] Protocols { get; init; } = [];
    /// <summary>是否只读。</summary>
    public bool ReadOnly { get; init; }
    /// <summary>描述。</summary>
    public string? Description { get; init; }
}

/// <summary>快照信息。</summary>
public record SnapshotInfo
{
    /// <summary>快照标识。</summary>
    public required string SnapshotId { get; init; }
    /// <summary>快照目标。</summary>
    public required string Target { get; init; }
    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>大小字节数。</summary>
    public long? SizeBytes { get; init; }
    /// <summary>描述。</summary>
    public string? Description { get; init; }
}

/// <summary>令牌验证结果。</summary>
public record TokenValidationResult
{
    /// <summary>是否有效。</summary>
    public bool IsValid { get; init; }
    /// <summary>载荷对象。</summary>
    public object? Payload { get; init; }
    /// <summary>错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>令牌标识。</summary>
    public string? Jti { get; init; }
    /// <summary>主体。</summary>
    public string? Subject { get; init; }
    /// <summary>令牌类型。</summary>
    public TokenType? TokenType { get; init; }
    /// <summary>能力列表。</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    /// <summary>过期时间。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>审计链验证结果。</summary>
public record ChainVerificationResult
{
    /// <summary>是否完整有效。</summary>
    public bool IsValid { get; init; }
    /// <summary>总条目数。</summary>
    public long TotalEntries { get; init; }
    /// <summary>首个断裂序号。</summary>
    public long? BrokenAtSequence { get; init; }
    /// <summary>验证消息。</summary>
    public string? Message { get; init; }
}

/// <summary>命令执行结果。</summary>
public record CommandResult
{
    /// <summary>退出码。</summary>
    public int ExitCode { get; init; }
    /// <summary>标准输出。</summary>
    public string Stdout { get; init; } = string.Empty;
    /// <summary>标准错误。</summary>
    public string Stderr { get; init; } = string.Empty;
}

/// <summary>分区规格。</summary>
public record PartitionSpec
{
    /// <summary>分区名称。</summary>
    public required string Name { get; init; }
    /// <summary>文件系统类型。</summary>
    public string? FileSystem { get; init; }
    /// <summary>起始字节。</summary>
    public long? StartBytes { get; init; }
    /// <summary>大小字节数。</summary>
    public long? SizeBytes { get; init; }
}

/// <summary>分区操作结果。</summary>
public record PartitionResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>分区路径。</summary>
    public string? PartitionPath { get; init; }
    /// <summary>消息。</summary>
    public string? Message { get; init; }
}

/// <summary>RAID 等级。</summary>
public enum RaidLevel
{
    /// <summary>未知等级。</summary>
    Unknown,
    /// <summary>RAID 0。</summary>
    Raid0,
    /// <summary>RAID 1。</summary>
    Raid1,
    /// <summary>RAID 5。</summary>
    Raid5,
    /// <summary>RAID 6。</summary>
    Raid6,
    /// <summary>RAID 10。</summary>
    Raid10
}

/// <summary>RAID 操作结果。</summary>
public record RaidResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>存储池标识。</summary>
    public string? PoolId { get; init; }
    /// <summary>消息。</summary>
    public string? Message { get; init; }
    /// <summary>错误码。</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>SMART 数据。</summary>
public record SmartData
{
    /// <summary>磁盘路径。</summary>
    public required string DiskPath { get; init; }
    /// <summary>健康状态。</summary>
    public required string Health { get; init; }
    /// <summary>温度摄氏度。</summary>
    public int? TemperatureCelsius { get; init; }
    /// <summary>原始 JSON。</summary>
    public string? RawJson { get; init; }
}

/// <summary>文件系统信息。</summary>
public record FsInfo
{
    /// <summary>挂载点。</summary>
    public required string MountPoint { get; init; }
    /// <summary>文件系统类型。</summary>
    public required string FileSystemType { get; init; }
    /// <summary>总字节数。</summary>
    public long TotalBytes { get; init; }
    /// <summary>可用字节数。</summary>
    public long AvailableBytes { get; init; }
    /// <summary>已用字节数。</summary>
    public long UsedBytes { get; init; }
}

/// <summary>网络接口信息。</summary>
public record NetworkInterfaceInfo
{
    /// <summary>接口名称。</summary>
    public required string Name { get; init; }
    /// <summary>MAC 地址。</summary>
    public string? MacAddress { get; init; }
    /// <summary>IP 地址列表。</summary>
    public string[] Addresses { get; init; } = [];
    /// <summary>是否启用。</summary>
    public bool IsUp { get; init; }
    /// <summary>速率 Mbps。</summary>
    public long? SpeedMbps { get; init; }
}

/// <summary>网络配置。</summary>
public record NetConfig
{
    /// <summary>是否启用 DHCP。</summary>
    public bool Dhcp { get; init; }
    /// <summary>静态地址。</summary>
    public string? Address { get; init; }
    /// <summary>网关。</summary>
    public string? Gateway { get; init; }
    /// <summary>DNS 服务器。</summary>
    public string[] DnsServers { get; init; } = [];
}

/// <summary>防火墙规则。</summary>
public record FirewallRule
{
    /// <summary>规则标识。</summary>
    public required string RuleId { get; init; }
    /// <summary>动作。</summary>
    public required string Action { get; init; }
    /// <summary>协议。</summary>
    public string? Protocol { get; init; }
    /// <summary>端口。</summary>
    public int? Port { get; init; }
    /// <summary>来源地址。</summary>
    public string? Source { get; init; }
}
