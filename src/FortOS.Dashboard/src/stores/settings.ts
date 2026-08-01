// ============================================================================
// FortOS Dashboard — Settings Store
// Manages system configuration read/write plus the metadata that drives the
// categorized, typed settings UI.
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import { getConfig, getConfigMeta, updateConfig } from '@/api/config'
import { ApiError } from '@/api/client'
import type { ConfigCategoryMeta, ConfigEntryMeta } from '@/types'

export const useSettingsStore = defineStore('settings', () => {
  const config = shallowRef<Record<string, string>>({})
  /** Semantic categories served by GET /api/config/meta. */
  const categories = shallowRef<ConfigCategoryMeta[]>([])
  /** Whitelisted, user-editable entries served by GET /api/config/meta. */
  const entries = shallowRef<ConfigEntryMeta[]>([])
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

  /** Load config metadata (best-effort — the UI degrades to a raw list without it). */
  async function fetchMeta(): Promise<void> {
    try {
      const meta = await getConfigMeta()
      categories.value = meta.categories
      entries.value = meta.entries
    } catch {
      categories.value = []
      entries.value = []
    }
  }

  /** Load config values and metadata together. */
  async function load(): Promise<void> {
    await Promise.all([fetchConfig(), fetchMeta()])
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

  /** Persist several keys (serial PUTs, then one refresh) to avoid hammering GET. */
  async function setConfigs(updates: { key: string; value: string }[]): Promise<void> {
    if (!updates.length) return
    try {
      for (const u of updates) await updateConfig(u.key, u.value)
      await fetchConfig()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '更新配置失败'
      throw e
    }
  }

  return { config, categories, entries, loading, error, fetchConfig, fetchMeta, load, setConfig, setConfigs }
})
