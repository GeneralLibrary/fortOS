import { get, post, del } from './client'
import type { ActionSuccessResponse } from '@/types'

export interface RecycleItem {
  id: string
  path: string
  size: number
}

export function listRecycle(share: string): Promise<RecycleItem[]> {
  return get<RecycleItem[]>(`/api/recycle/${encodeURIComponent(share)}`)
}

export function restoreRecycle(id: string, targetPath?: string): Promise<ActionSuccessResponse> {
  return post<ActionSuccessResponse>(`/api/recycle/restore/${encodeURIComponent(id)}`, {
    targetPath: targetPath ?? null,
  })
}

export function emptyRecycle(share: string, retentionDays = 0): Promise<ActionSuccessResponse> {
  return del<ActionSuccessResponse>(`/api/recycle/${encodeURIComponent(share)}/empty`, undefined)
}
