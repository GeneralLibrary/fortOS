import { get, post } from './client'
import type { CommandResult, SnapshotRequest, RestoreSnapshotRequest } from '@/types'

export function listSnapshots(target: string): Promise<CommandResult> {
  return get<CommandResult>('/api/snapshots', { target })
}

export function createSnapshot(request: SnapshotRequest): Promise<CommandResult> {
  return post<CommandResult>('/api/snapshots', request)
}

export function restoreSnapshot(snapshotId: string, target: string): Promise<CommandResult> {
  return post<CommandResult>(`/api/snapshots/${encodeURIComponent(snapshotId)}/restore`, {
    target,
  } satisfies RestoreSnapshotRequest)
}
