// ============================================================================
// FortOS Dashboard — Settings Store
// Manages system configuration read/write.
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import { getConfig, updateConfig } from '@/api/config'
import { ApiError } from '@/api/client'

export const useSettingsStore = defineStore('settings', () => {
  const config = shallowRef<Record<string, string>>({})
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchConfig(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      config.value = await getConfig()
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取配置失败'
    } finally {
      loading.value = false
    }
  }

  async function setConfig(key: string, value: string): Promise<void> {
    try {
      await updateConfig(key, value)
      await fetchConfig()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '更新配置失败'
      throw e
    }
  }

  return { config, loading, error, fetchConfig, setConfig }
})
