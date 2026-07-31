// ============================================================================
// FortOS Dashboard — Backup Store
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import {
  listBackupTasks, upsertBackupTask, deleteBackupTask,
  runBackupTask, getBackupRuns, restoreBackup,
} from '@/api/backup'
import type { BackupTask, BackupRunRecord } from '@/types'
import { ApiError } from '@/api/client'

export const useBackupStore = defineStore('backup', () => {
  const tasks = shallowRef<BackupTask[]>([])
  const runs = shallowRef<BackupRunRecord[]>([])
  const runsTotal = ref(0)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchTasks(signal?: AbortSignal): Promise<void> {
    loading.value = true
    error.value = null
    try {
      tasks.value = await listBackupTasks(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取备份任务列表失败'
    } finally {
      loading.value = false
    }
  }

  async function saveTask(taskId: string, task: BackupTask): Promise<BackupTask> {
    try {
      const saved = await upsertBackupTask(taskId, task)
      await fetchTasks()
      return saved
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '保存备份任务失败'
      throw e
    }
  }

  async function removeTask(taskId: string): Promise<void> {
    try {
      await deleteBackupTask(taskId)
      await fetchTasks()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '删除备份任务失败'
      throw e
    }
  }

  async function runTask(taskId: string): Promise<void> {
    try {
      await runBackupTask(taskId)
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '执行备份任务失败'
      throw e
    }
  }

  async function fetchRuns(taskId?: string, offset = 0, limit = 100): Promise<void> {
    try {
      const page = await getBackupRuns(taskId, offset, limit)
      runs.value = page.items
      runsTotal.value = page.total
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取备份历史失败'
    }
  }

  async function restore(taskId: string, sourceOverride?: string, targetOverride?: string, dryRun = false): Promise<void> {
    try {
      await restoreBackup(taskId, sourceOverride, targetOverride, dryRun)
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '恢复备份失败'
      throw e
    }
  }

  return {
    tasks, runs, runsTotal, loading, error,
    fetchTasks, saveTask, removeTask, runTask, fetchRuns, restore,
  }
})
