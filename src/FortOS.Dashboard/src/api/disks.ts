import { get, post } from './client'
import type { DiskInfo, SmartData, PathRequest } from '@/types'

export function listDisks(signal?: AbortSignal): Promise<DiskInfo[]> {
  return get<DiskInfo[]>('/api/disks', {}, signal)
}

export function getDiskDetail(path: string, signal?: AbortSignal): Promise<DiskInfo> {
  return get<DiskInfo>('/api/disks/detail', { path }, signal)
}

export function runSmartCheck(path: string): Promise<SmartData> {
  return post<SmartData>('/api/disks/smart-check', { path } satisfies PathRequest)
}
