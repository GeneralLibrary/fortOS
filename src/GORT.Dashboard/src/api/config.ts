import { get, put } from './client'
import type { ConfigValue, ActionSuccessResponse } from '@/types'

export function getConfig(): Promise<Record<string, string>> {
  return get<Record<string, string>>('/api/config')
}

export function updateConfig(key: string, value: string): Promise<ActionSuccessResponse> {
  return put<ActionSuccessResponse>(`/api/config/${encodeURIComponent(key)}`, {
    value,
  } satisfies ConfigValue)
}
