import { get, post } from './client'
import type { ServiceStatusInfo, ActionSuccessResponse } from '@/types'

export function listServices(signal?: AbortSignal): Promise<ServiceStatusInfo[]> {
  return get<ServiceStatusInfo[]>('/api/services', {}, signal)
}

export function startService(serviceId: string): Promise<ActionSuccessResponse> {
  return post<ActionSuccessResponse>(`/api/services/${encodeURIComponent(serviceId)}/start`)
}

export function stopService(serviceId: string): Promise<ActionSuccessResponse> {
  return post<ActionSuccessResponse>(`/api/services/${encodeURIComponent(serviceId)}/stop`)
}
