// ============================================================================
// FortOS Dashboard — TypeScript Type Definitions
// Mirrors the .NET backend models in FortOS.Core.Models.
// All property names use camelCase matching System.Text.Json camelCase policy.
// All enums are serialized as strings matching JsonStringEnumConverter.
// ============================================================================

// ---- Enums ----

export enum ServiceStatus {
  Stopped = 'Stopped',
  Starting = 'Starting',
  Running = 'Running',
  Stopping = 'Stopping',
  Failed = 'Failed',
  Unknown = 'Unknown',
}

export enum ServiceType {
  Native = 'Native',
  Container = 'Container',
  Module = 'Module',
  Systemd = 'Systemd',
}

export enum HealthCheckType {
  HttpGet = 'HttpGet',
  TcpConnect = 'TcpConnect',
  ExecCommand = 'ExecCommand',
  Grpc = 'Grpc',
}

export enum HealthStatus {
  Healthy = 'Healthy',
  Degraded = 'Degraded',
  Unhealthy = 'Unhealthy',
  Unknown = 'Unknown',
}

export enum RestartPolicy {
  Always = 'Always',
  OnFailure = 'OnFailure',
  Never = 'Never',
  ExponentialBackoff = 'ExponentialBackoff',
}

export enum ServiceStartup {
  Automatic = 'Automatic',
  Manual = 'Manual',
  Disabled = 'Disabled',
}

export enum LogCategory {
  System = 'System',
  Audit = 'Audit',
  Access = 'Access',
  Agent = 'Agent',
  Trace = 'Trace',
  Metric = 'Metric',
}

export enum TokenType {
  Access = 'Access',
  Session = 'Session',
  Agent = 'Agent',
  Service = 'Service',
}

export enum QuotaType {
  User = 'User',
  Share = 'Share',
  Group = 'Group',
}

export enum NasDataLevel {
  Public = 'Public',
  Internal = 'Internal',
  Personal = 'Personal',
  Sensitive = 'Sensitive',
  System = 'System',
}

export enum FilePermission {
  None = 'None',
  Read = 'Read',
  Write = 'Write',
  ReadWrite = 'ReadWrite',
  FullControl = 'FullControl',
}

export enum BackupTargetType {
  Local = 'Local',
  RemoteNas = 'RemoteNas',
  S3 = 'S3',
  B2 = 'B2',
  WebDAV = 'WebDAV',
  SFTP = 'SFTP',
}

export enum BackupMethod {
  Incremental = 'Incremental',
  Full = 'Full',
  Mirror = 'Mirror',
}

export enum BackupRunState {
  Queued = 'Queued',
  Running = 'Running',
  Succeeded = 'Succeeded',
  Failed = 'Failed',
  RolledBack = 'RolledBack',
}

// ---- Core Models ----

export interface DiskInfo {
  path: string
  model: string
  serial: string
  sizeBytes: number
  interfaceType: string
  isSsd: boolean
  smartStatus: string
  temperatureCelsius: number
  usedPercent: number
}

export interface HealthCheckConfig {
  type: HealthCheckType
  endpoint: string
  intervalSeconds: number
  timeoutSeconds: number
  retries: number
  startPeriodSeconds: number
}

export interface ResourceQuota {
  cpuLimit?: number
  memoryLimitBytes?: number
  ioWeight?: number
}

export interface ServiceDefinition {
  serviceId: string
  displayName: string
  type: ServiceType
  dependsOn: string[]
  requiredCapabilities: string[]
  startup: ServiceStartup
  restartPolicy: RestartPolicy
  executable?: string | null
  arguments?: string | null
  systemdUnit?: string | null
  composeFile?: string | null
  healthCheck?: HealthCheckConfig | null
  quota?: ResourceQuota | null
}

export interface ServiceStatusInfo {
  serviceId: string
  status: ServiceStatus
  type: ServiceType
  pid?: number | null
  cpuPercent: number
  memoryBytes: number
  uptime: string // TimeSpan serialized as ISO 8601 duration
  lastError?: string | null
}

export interface LogEntry {
  logId: string
  timestamp: string // DateTimeOffset as ISO 8601
  category: LogCategory
  level: string // Microsoft.Extensions.Logging.LogLevel name
  sourceComponent: string
  sourceLayer?: string | null
  hostName?: string | null
  hostArch?: string | null
  userId?: string | null
  agentId?: string | null
  serviceId?: string | null
  traceId?: string | null
  spanId?: string | null
  message: string
  template?: string | null
  properties: Record<string, unknown>
  tags: string[]
  audit?: AuditDetail | null
  metric?: MetricData | null
}

export interface AuditDetail {
  action: string
  resource: string
  resourceType: string
  permissionRequired?: string | null
  granted: boolean
  clientIp?: string | null
  userAgent?: string | null
  sessionId?: string | null
  beforeState?: string | null
  afterState?: string | null
  previousHash?: string | null
  currentHash: string
  chainSignature: string
}

export interface MetricData {
  metricName: string
  value: number
  unit: string
  dimensions: Record<string, string>
  timestamp: string
}

export interface AgentTemplate {
  id: string
  name: string
  version: string
  description?: string | null
  /** Logo: emoji or image URL (http/https). */
  logo?: string | null
  capabilitiesRequired: string[]
  parameters: AgentTemplateParameter[]
  accessNotes: string[]
  composeTemplate: string
}

export interface AgentTemplateParameter {
  name: string
  type: string
  required: boolean
  default?: string | null
}

export interface AgentConfig {
  agentId: string
  displayName: string
  imageName: string
  capabilities: string[]
  volumeMapping: VolumeMapping[]
  portMapping: PortMapping[]
  resourceQuota?: ResourceQuota | null
  /** Extra environment variables merged over template defaults. */
  environment?: Record<string, string>
}

/** External access info for a deployed agent (from GET /api/agents/{id}/access). */
export interface AgentAccessInfo {
  agentId: string
  templateId: string
  displayName: string
  imageName: string
  ports: AgentPortInfo[]
  env: AgentEnvInfo[]
  accessNotes: string[]
  urls: { name: string; url: string | null }[]
}

/** Asynchronous deployment acceptance/status. */
export interface AgentDeploymentStatus {
  status: 'deploying' | 'success' | 'failed' | 'unknown'
  error?: string | null
  startedAt?: string | null
  serviceId?: string | null
  finishedAt?: string | null
  /** Fine-grained stage: queued → pulling → deploying → success/failed. */
  stage?: string
  message?: string | null
}

export interface AgentPortInfo {
  hostPort: number
  containerPort: number
  protocol: string
}

export interface AgentEnvInfo {
  name: string
  set: boolean
}

export interface VolumeMapping {
  hostPath: string
  containerPath: string
  readOnly: boolean
}

export interface PortMapping {
  hostPort: number
  containerPort: number
  protocol: string
}

export interface ShareDefinition {
  shareId: string
  name: string
  path: string
  protocols: string[]
  readOnly: boolean
  description?: string | null
}

export interface SmartData {
  diskPath: string
  health: string
  temperatureCelsius?: number | null
  rawJson?: string | null
}

export interface CommandResult {
  exitCode: number
  stdout: string
  stderr: string
}

export interface AlertRule {
  ruleId: string
  name: string
  description: string
  severity: string
  condition: AlertCondition
  actions: string[]
  cooldownSeconds: number
  suppress?: AlertSuppress | null
}

export interface AlertCondition {
  type: string
  topic?: string | null
  metric?: string | null
  operator?: string | null
  value?: number | null
  durationSeconds?: number | null
  count?: number | null
  withinSeconds?: number | null
}

export interface AlertSuppress {
  window?: string | null
}

export interface ActiveAlert {
  alertId: string
  ruleId: string
  severity: string
  message: string
  triggeredAt: string
  dimensions: Record<string, string>
}

export interface BackupTask {
  taskId: string
  name: string
  sourcePath: string
  target: BackupTarget
  cronExpression: string
  enabled: boolean
  method: BackupMethod
  retentionDays: number
  retentionCount: number
  compression: boolean
  encryption: boolean
  excludePatterns: string[]
  retryCount: number
  retryBackoffSeconds: number
  freshnessSlaHours: number
}

export interface BackupTarget {
  type: BackupTargetType
  connectionString: string
  bucketOrPath: string
  accessKey?: string | null
  secretKey?: string | null
}

export interface BackupRunRecord {
  runId: string
  taskId: string
  operation: string
  state: BackupRunState
  startedAt: string
  finishedAt?: string | null
  success: boolean
  exitCode: number
  stdout: string
  stderr: string
  leaseToken?: number | null
  report: BackupRunReport
}

export interface BackupRunReport {
  attemptCount: number
  errorCode?: string | null
  checksumManifestPath?: string | null
  checksumVerified: boolean
  checkpointPath?: string | null
  bytesProcessed?: number | null
}

export interface FsInfo {
  mountPoint: string
  fileSystemType: string
  totalBytes: number
  availableBytes: number
  usedBytes: number
}

export interface NetworkInterfaceInfo {
  name: string
  macAddress?: string | null
  addresses: string[]
  isUp: boolean
  speedMbps?: number | null
}

export interface NetConfig {
  dhcp: boolean
  address?: string | null
  gateway?: string | null
  dnsServers: string[]
}

export interface FirewallRule {
  ruleId: string
  action: string
  protocol?: string | null
  port?: number | null
  source?: string | null
}

export interface ChainVerificationResult {
  isValid: boolean
  totalEntries: number
  brokenAtSequence?: number | null
  message?: string | null
}

export interface SnapshotInfo {
  snapshotId: string
  target: string
  createdAt: string
  sizeBytes?: number | null
  description?: string | null
}

// ---- Paging ----

export interface PageRequest {
  offset: number
  limit: number
}

export interface Page<T> {
  items: T[]
  offset: number
  limit: number
  total: number
  hasMore: boolean
}

// ---- File Management Models ----

export interface ManagedFileEntry {
  path: string
  name: string
  isDirectory: boolean
  sizeBytes?: number | null
  modifiedAt?: string | null
}

export interface ManagedFileContent {
  path: string
  encoding: string
  content: string
  sizeBytes: number
}

export interface ManagedFileStat {
  path: string
  exists: boolean
  isDirectory: boolean
  sizeBytes?: number | null
  modifiedAt?: string | null
}

export interface ManagedDeleteResult {
  deletedPath: string
  hardDeleted: boolean
  recyclePath?: string | null
}

export interface UploadSession {
  sessionId: string
  targetPath: string
  receivedBytes: number
  expectedSize?: number | null
  expectedSha256?: string | null
  state: string
  expiresAt: string
  etag?: string | null
  temporaryPath?: string | null
}

// ---- System Metrics Models ----

export interface SystemMetricsSnapshot {
  collectedAt: string
  host: HostRuntimeMetrics
  cpu: CpuMetrics
  memory: MemoryMetrics
  disks: DiskIoMetrics[]
  networks: NetworkTrafficMetrics[]
  networkStack: NetworkStackMetrics
  protocolSessions: ProtocolSessionMetrics[]
  fileSystems: FileSystemCapacityMetrics[]
  raidArrays: RaidMetrics[]
  services: ServiceRuntimeMetrics[]
  containers: ContainerRuntimeMetrics[]
  diagnostics: string[]
}

export interface HostRuntimeMetrics {
  uptime: string
  bootedAt: string
  loadAverage1: number
  loadAverage5: number
  loadAverage15: number
}

export interface CpuMetrics {
  logicalProcessorCount: number
  usagePercent: number
  userPercent: number
  systemPercent: number
  ioWaitPercent: number
  temperatureCelsius?: number | null
}

export interface MemoryMetrics {
  totalBytes: number
  availableBytes: number
  usedBytes: number
  usedPercent: number
  swapTotalBytes: number
  swapUsedBytes: number
  swapUsedPercent: number
  oomKillsSinceLastCollection: number
}

export interface DiskIoMetrics {
  device: string
  readBytesPerSecond: number
  writeBytesPerSecond: number
  readOperationsPerSecond: number
  writeOperationsPerSecond: number
  averageLatencyMilliseconds: number
  utilizationPercent: number
  temperatureCelsius?: number | null
  smartHealth?: string | null
}

export interface NetworkTrafficMetrics {
  interface: string
  isUp: boolean
  linkSpeedMbps?: number | null
  receiveBytesPerSecond: number
  transmitBytesPerSecond: number
  receiveErrors: number
  transmitErrors: number
  receiveDropped: number
  transmitDropped: number
}

export interface NetworkStackMetrics {
  establishedConnections: number
  retransmittedSegmentsPerSecond: number
}

export interface ProtocolSessionMetrics {
  protocol: string
  activeSessions: number
}

export interface FileSystemCapacityMetrics {
  device: string
  mountPoint: string
  fileSystemType?: string | null
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usedPercent: number
  growthBytesPerSecond: number
  estimatedFullAt?: string | null
}

export interface RaidMetrics {
  name: string
  level: string
  healthy: boolean
  activeDevices: number
  totalDevices: number
  operation?: string | null
  progressPercent?: number | null
}

/** RAID levels supported by the storage backend (mirrors FortOS.Core.RaidLevel). */
export enum RaidLevel {
  Unknown = 'Unknown',
  Raid0 = 'Raid0',
  Raid1 = 'Raid1',
  Raid5 = 'Raid5',
  Raid6 = 'Raid6',
  Raid10 = 'Raid10',
}

/** RAID creation response (mirrors FortOS.Core.RaidResult). */
export interface RaidResult {
  success: boolean
  poolId?: string | null
  message?: string | null
  errorCode?: string | null
}

/** Whether RAID tooling is available on the host (mirrors GET /api/disks/raid-capability). */
export interface RaidCapability {
  available: boolean
  tool: string
}

export interface ServiceRuntimeMetrics {
  serviceId: string
  state: string
  uptime: string
  restartCount: number
}

export interface ContainerRuntimeMetrics {
  containerId: string
  name: string
  cpuPercent: number
  memoryUsedBytes: number
  memoryLimitBytes: number
  memoryPercent: number
  networkReceiveBytes: number
  networkTransmitBytes: number
  blockReadBytes: number
  blockWriteBytes: number
}

// ---- Auth Models ----

export interface LoginRequest {
  username: string
  password: string
  totp?: string | null
}

export interface LoginResponse {
  token: string
  payload: NasTokenPayload
}

export interface RegisterRequest {
  username: string
  password: string
  displayName?: string | null
  email?: string | null
}

export interface NasTokenPayload {
  iss: string
  sub: string
  iat: string
  exp: string
  tokenType: TokenType
  trustLevel: number
  capabilities: NAbilitySet
  delegationChain: string[]
  deviceBinding?: string | null
  jti: string
}

export interface NAbilitySet {
  abilities: string[]
}

// ---- Request / Response DTOs ----

export interface PathRequest {
  path: string
}

export interface SnapshotRequest {
  target: string
  name?: string | null
}

export interface RestoreSnapshotRequest {
  target: string
}

export interface DeployAgentRequest {
  templateId: string
  config: AgentConfig
}

export interface InstallAgentTemplateRequest {
  source: string
}

export interface RecoveryRequest {
  target: string
  mode?: string | null
  source?: string | null
  snapshotId?: string | null
  dryRun: boolean
}

export interface ConfigValue {
  value?: string | null
}

// ---- Config metadata (GET /api/config/meta) ----

/** Control type the dashboard renders for a config entry. */
export type ConfigEntryType = 'boolean' | 'number' | 'select' | 'string' | 'text'

/** Semantic category grouping config entries in the settings page. */
export interface ConfigCategoryMeta {
  id: string
  name: string
  icon: string
  description?: string | null
  order: number
}

/** Metadata for one whitelisted, user-editable configuration entry. */
export interface ConfigEntryMeta {
  key: string
  category: string
  type: ConfigEntryType
  label?: string | null
  description?: string | null
  options?: string[] | null
  min?: number | null
  max?: number | null
  step?: number | null
  defaultValue?: string | null
  order: number
}

/** Full metadata payload served by GET /api/config/meta. */
export interface ConfigMeta {
  categories: ConfigCategoryMeta[]
  entries: ConfigEntryMeta[]
}

export interface RestoreBackupRequest {
  sourceOverride?: string | null
  targetOverride?: string | null
  dryRun: boolean
}

// ---- API wrapper types for standard responses ----

/** Standard success response shape used by start/stop/delete endpoints. */
export interface ActionSuccessResponse {
  success: boolean
  serviceId?: string
  agentId?: string
  shareId?: string
  taskId?: string
  ruleId?: string
  key?: string
  deleted?: number
  [key: string]: unknown
}

/** Health check response. */
export interface HealthResponse {
  status: string
  version?: string
  uptime: string
  traceId?: string | null
}

/** Legacy metrics response from /api/metrics/current. */
export interface LegacyMetricsResponse {
  gc: {
    totalMemory: number
    gen0: number
    gen1: number
    gen2: number
  }
  disks: DiskInfo[]
}

/** UPS status response. */
export interface UpsStatusResponse {
  configured: boolean
  raw?: string
  message?: string
  error?: string
}
