import { get } from './client'
import type { LogEntry, LogCategory } from '@/types'

export interface LogQueryParams {
  category?: LogCategory
  minLevel?: string
  from?: string
  to?: string
  searchText?: string
  tags?: string
  serviceId?: string
  agentId?: string
  traceId?: string
  limit?: number
  offset?: number
}

export function queryLogs(params: LogQueryParams, signal?: AbortSignal): Promise<LogEntry[]> {
  return get<LogEntry[]>('/api/logs', params as Record<string, string | number | boolean | undefined>, signal)
}
