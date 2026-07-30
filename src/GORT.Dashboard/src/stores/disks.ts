// ============================================================================
// GORT Dashboard — Disks Store
// Manages disk listing, detail, and SMART operations.
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import { listDisks, getDiskDetail, runSmartCheck } from '@/api/disks'
import type { DiskInfo, SmartData } from '@/types'

export const useDisksStore = defineStore('disks', () => {
  const disks = shallowRef<DiskInfo[]>([])
  const selectedDisk = shallowRef<DiskInfo | null>(null)
  const smartData = shallowRef<SmartData | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  /**
   * Fetch all disks.
   */
  async function fetchDisks(signal?: AbortSignal): Promise<void> {
    loading.value = true
    error.value = null
    try {
      disks.value = await listDisks(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取磁盘列表失败'
    } finally {
      loading.value = false
    }
  }

  /**
   * Fetch detail for a specific disk.
   */
  async function fetchDiskDetail(path: string): Promise<void> {
    loading.value = true
    error.value = null
    try {
      selectedDisk.value = await getDiskDetail(path)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取磁盘详情失败'
    } finally {
      loading.value = false
    }
  }

  /**
   * Run SMART check on a disk.
   */
  async function checkSmart(path: string): Promise<void> {
    loading.value = true
    error.value = null
    try {
      smartData.value = await runSmartCheck(path)
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'SMART 检测失败'
    } finally {
      loading.value = false
    }
  }

  return {
    disks,
    selectedDisk,
    smartData,
    loading,
    error,
    fetchDisks,
    fetchDiskDetail,
    checkSmart,
  }
})
