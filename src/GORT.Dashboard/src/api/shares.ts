import { get, post, del } from './client'
import type { ShareDefinition, ActionSuccessResponse } from '@/types'

export function listShares(signal?: AbortSignal): Promise<ShareDefinition[]> {
  return get<ShareDefinition[]>('/api/shares', {}, signal)
}

export function createShare(share: ShareDefinition): Promise<ShareDefinition> {
  return post<ShareDefinition>('/api/shares', share)
}

export function deleteShare(shareId: string): Promise<ActionSuccessResponse> {
  return del<ActionSuccessResponse>(`/api/shares/${encodeURIComponent(shareId)}`)
}
