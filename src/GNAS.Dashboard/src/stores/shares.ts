// ============================================================================
// GNAS Dashboard — Shares Store
// Manages share definitions and operations.
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import { listShares, createShare, deleteShare } from '@/api/shares'
import type { ShareDefinition } from '@/types'
import { ApiError } from '@/api/client'

export const useSharesStore = defineStore('shares', () => {
  const shares = shallowRef<ShareDefinition[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchShares(signal?: AbortSignal): Promise<void> {
    loading.value = true
    error.value = null
    try {
      shares.value = await listShares(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取共享列表失败'
    } finally {
      loading.value = false
    }
  }

  async function addShare(share: ShareDefinition): Promise<ShareDefinition> {
    loading.value = true
    error.value = null
    try {
      const created = await createShare(share)
      await fetchShares()
      return created
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : '创建共享失败'
      error.value = msg
      throw e
    } finally {
      loading.value = false
    }
  }

  async function removeShare(shareId: string): Promise<void> {
    loading.value = true
    error.value = null
    try {
      await deleteShare(shareId)
      await fetchShares()
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : '删除共享失败'
      error.value = msg
      throw e
    } finally {
      loading.value = false
    }
  }

  return { shares, loading, error, fetchShares, addShare, removeShare }
})
