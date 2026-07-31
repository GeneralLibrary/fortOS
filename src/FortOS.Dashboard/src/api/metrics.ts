import { get } from './client'
import type { SystemMetricsSnapshot, MetricData, LegacyMetricsResponse } from '@/types'

export function getLegacyMetrics(signal?: AbortSignal): Promise<LegacyMetricsResponse> {
  return get<LegacyMetricsResponse>('/api/metrics/current', {}, signal)
}

export function getSystemMetrics(signal?: AbortSignal): Promise<SystemMetricsSnapshot> {
  return get<SystemMetricsSnapshot>('/api/metrics/system', {}, signal)
}

export function getMetricHistory(metric?: string, from?: string, limit = 500, signal?: AbortSignal): Promise<MetricData[]> {
  return get<MetricData[]>('/api/metrics/history', { metric, from, limit }, signal)
}
