import { get, put } from './client'
import type { ConfigValue, ActionSuccessResponse, ConfigMeta } from '@/types'

export function getConfig(): Promise<Record<string, string>> {
  return get<Record<string, string>>('/api/config')
}

/** Fetch metadata describing whitelisted, user-editable configuration. */
export function getConfigMeta(): Promise<ConfigMeta> {
  return get<ConfigMeta>('/api/config/meta')
}

export function updateConfig(key: string, value: string): Promise<ActionSuccessResponse> {
  return put<ActionSuccessResponse>(`/api/config/${encodeURIComponent(key)}`, {
    value,
  } satisfies ConfigValue)
}
