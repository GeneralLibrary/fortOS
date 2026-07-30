// ============================================================================
// GORT Dashboard — Formatting Utilities
// Provides consistent display formatting for byte sizes, durations, dates,
// and other values throughout the dashboard.
// ============================================================================

import { ServiceStatus } from '@/types'

const BYTE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB'] as const
const BIT_UNITS = ['bps', 'Kbps', 'Mbps', 'Gbps'] as const

/**
 * Formats a byte count into a human-readable string with IEC binary prefixes.
 * @param bytes — raw byte count
 * @param decimals — fractional digits to display (default 1)
 * @returns formatted string like "1.5 GiB"
 */
export function formatBytes(bytes: number, decimals = 1): string {
  if (bytes === 0) return '0 B'
  if (bytes < 0) return `-${formatBytes(-bytes, decimals)}`
  const k = 1024
  const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), BYTE_UNITS.length - 1)
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(decimals))} ${BYTE_UNITS[i]}`
}

/**
 * Formats a bit-rate value into a human-readable string.
 * @param bitsPerSecond — raw bits per second
 * @param decimals — fractional digits (default 1)
 */
export function formatBitRate(bitsPerSecond: number, decimals = 1): string {
  if (bitsPerSecond === 0) return '0 bps'
  const k = 1000
  const i = Math.min(Math.floor(Math.log(bitsPerSecond) / Math.log(k)), BIT_UNITS.length - 1)
  return `${parseFloat((bitsPerSecond / Math.pow(k, i)).toFixed(decimals))} ${BIT_UNITS[i]}`
}

/**
 * Formats a byte-per-second rate for display (network/storage I/O).
 */
export function formatBytesPerSecond(bytesPerSecond: number, decimals = 1): string {
  return `${formatBytes(bytesPerSecond, decimals)}/s`
}

/**
 * Formats a percentage value with consistent precision.
 * @param value — raw ratio or percentage (e.g. 0.75 or 75)
 * @param isRatio — true if value is 0–1 ratio, false if already 0–100
 */
export function formatPercent(value: number, isRatio = false): string {
  const pct = isRatio ? value * 100 : value
  return `${pct.toFixed(1)}%`
}

/**
 * Formats a Unix timestamp or ISO 8601 string as a locale date string.
 */
export function formatDateTime(value: string | number, format: 'short' | 'long' | 'relative' = 'short'): string {
  const date = typeof value === 'string' ? new Date(value) : new Date(value * 1000)
  if (isNaN(date.getTime())) return '—'
  switch (format) {
    case 'short':
      return date.toLocaleString()
    case 'long':
      return date.toLocaleString(undefined, {
        year: 'numeric', month: 'long', day: 'numeric',
        hour: '2-digit', minute: '2-digit', second: '2-digit',
      })
    case 'relative':
      return formatRelativeTime(date)
  }
}

/**
 * Formats a date relative to now (e.g. "2 minutes ago").
 */
export function formatRelativeTime(date: Date): string {
  const now = Date.now()
  const diffMs = now - date.getTime()
  const diffSeconds = Math.floor(diffMs / 1000)

  if (diffSeconds < 0) return 'just now'
  if (diffSeconds < 60) return `${diffSeconds}s ago`
  if (diffSeconds < 3600) return `${Math.floor(diffSeconds / 60)}m ago`
  if (diffSeconds < 86400) return `${Math.floor(diffSeconds / 3600)}h ago`
  if (diffSeconds < 604800) return `${Math.floor(diffSeconds / 86400)}d ago`
  return date.toLocaleDateString()
}

/**
 * Formats a TimeSpan string (ISO 8601 duration like "1.05:30:00" or "00:15:00")
 * into a human-friendly representation.
 */
export function formatUptime(duration: string | undefined | null): string {
  if (!duration) return '—'
  // .NET TimeSpan serialized as "d.hh:mm:ss.fffffff"
  const match = duration.match(/^(\d+\.)?(\d{2}):(\d{2}):(\d{2})/)
  if (!match) return duration
  const days = match[1] ? parseInt(match[1], 10) : 0
  const hours = parseInt(match[2], 10)
  const minutes = parseInt(match[3], 10)
  const totalHours = days * 24 + hours

  const parts: string[] = []
  if (totalHours > 0) parts.push(`${totalHours}h`)
  if (minutes > 0 || totalHours === 0) parts.push(`${minutes}m`)
  return parts.join(' ') || '< 1m'
}

/**
 * Formats a temperature in Celsius with unit.
 */
export function formatTemperature(celsius: number | undefined | null): string {
  if (celsius == null) return '—'
  return `${celsius}°C`
}

/**
 * Calculates and formats a percentage from two absolute values.
 */
export function calcPercent(used: number, total: number): string {
  if (total <= 0) return '0%'
  return formatPercent((used / total) * 100)
}

/**
 * Returns severity color mapping for Naive UI tag/status types.
 */
export function severityColor(severity: string): 'error' | 'warning' | 'info' | 'success' | 'default' {
  switch (severity.toLowerCase()) {
    case 'critical':
    case 'error':
      return 'error'
    case 'warning':
      return 'warning'
    case 'info':
      return 'info'
    case 'ok':
    case 'success':
      return 'success'
    default:
      return 'default'
  }
}

/**
 * Returns a Naive UI tag type based on ServiceStatus.
 */
export function serviceStatusType(status: ServiceStatus): 'success' | 'warning' | 'error' | 'info' | 'default' {
  switch (status) {
    case 'Running' as ServiceStatus: return 'success'
    case 'Starting' as ServiceStatus:
    case 'Stopping' as ServiceStatus: return 'warning'
    case 'Failed' as ServiceStatus: return 'error'
    case 'Stopped' as ServiceStatus: return 'default'
    default: return 'info'
  }
}

/**
 * Returns a human-readable label for BackupRunState.
 * @param locale — language code for localized output (default zh-CN)
 */
export function backupRunStateLabel(state: string, locale: 'zh-CN' | 'en-US' = 'zh-CN'): string {
  const map: Record<string, Record<string, string>> = {
    'zh-CN': { Queued: '排队中', Running: '运行中', Succeeded: '成功', Failed: '失败', RolledBack: '已回滚' },
    'en-US': { Queued: 'Queued', Running: 'Running', Succeeded: 'Succeeded', Failed: 'Failed', RolledBack: 'Rolled back' },
  }
  return (map[locale] ?? map['zh-CN'])[state] ?? state
}

/**
 * Returns a Naive UI tag type for BackupRunState.
 */
export function backupRunStateType(state: string): 'success' | 'warning' | 'error' | 'info' | 'default' {
  switch (state) {
    case 'Succeeded': return 'success'
    case 'Running': return 'info'
    case 'Queued': return 'warning'
    case 'Failed': return 'error'
    case 'RolledBack': return 'warning'
    default: return 'default'
  }
}

/**
 * Truncates a string with ellipsis.
 */
export function truncate(value: string, maxLength: number): string {
  if (value.length <= maxLength) return value
  return value.slice(0, maxLength - 1) + '…'
}

/**
 * Parses a cron expression or "interval:N" pattern into human-readable text.
 * @param cronExpression — the raw schedule string
 * @param locale — language code for localized output (default zh-CN)
 */
export function formatSchedule(cronExpression: string, locale: 'zh-CN' | 'en-US' = 'zh-CN'): string {
  if (!cronExpression) return '—'
  if (cronExpression.startsWith('interval:')) {
    const minutes = parseInt(cronExpression.split(':')[1], 10)
    return locale === 'zh-CN'
      ? `每 ${minutes} 分钟`
      : `Every ${minutes} min`
  }
  // Simple time-of-day: "HH:mm"
  const timeMatch = cronExpression.match(/^(\d{1,2}):(\d{2})$/)
  if (timeMatch) {
    return locale === 'zh-CN'
      ? `每天 ${timeMatch[0]}`
      : `Daily at ${timeMatch[0]}`
  }
  return cronExpression
}
