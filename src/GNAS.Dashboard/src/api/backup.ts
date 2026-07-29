import { get, post, put, del } from './client'
import type { BackupTask, BackupRunRecord, Page, ActionSuccessResponse } from '@/types'

export function listBackupTasks(signal?: AbortSignal): Promise<BackupTask[]> {
  return get<BackupTask[]>('/api/backup/tasks', {}, signal)
}

export function upsertBackupTask(taskId: string, task: BackupTask): Promise<BackupTask> {
  return put<BackupTask>(`/api/backup/tasks/${encodeURIComponent(taskId)}`, task)
}

export function deleteBackupTask(taskId: string): Promise<ActionSuccessResponse> {
  return del<ActionSuccessResponse>(`/api/backup/tasks/${encodeURIComponent(taskId)}`)
}

export function runBackupTask(taskId: string): Promise<ActionSuccessResponse & { runId: string; state: string }> {
  return post(`/api/backup/tasks/${encodeURIComponent(taskId)}/run`)
}

export function getBackupRuns(taskId?: string, offset = 0, limit = 100): Promise<Page<BackupRunRecord>> {
  return get<Page<BackupRunRecord>>('/api/backup/runs', { taskId, offset, limit })
}

export function restoreBackup(
  taskId: string,
  sourceOverride?: string,
  targetOverride?: string,
  dryRun = false,
): Promise<ActionSuccessResponse & { runId: string; state: string }> {
  return post(`/api/backup/tasks/${encodeURIComponent(taskId)}/restore`, {
    sourceOverride: sourceOverride ?? null,
    targetOverride: targetOverride ?? null,
    dryRun,
  })
}
