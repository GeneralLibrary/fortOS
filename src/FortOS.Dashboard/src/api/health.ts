import { get } from './client'
import type { HealthResponse } from '@/types'

export function getHealth(): Promise<HealthResponse> {
  return get<HealthResponse>('/api/health')
}
