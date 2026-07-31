// ============================================================================
// FortOS Dashboard — Alerts Store
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import { listAlerts, listAlertRules, addAlertRule } from '@/api/alerts'
import type { ActiveAlert, AlertRule } from '@/types'
import { ApiError } from '@/api/client'

export const useAlertsStore = defineStore('alerts', () => {
  const alerts = shallowRef<ActiveAlert[]>([])
  const rules = shallowRef<AlertRule[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchAlerts(signal?: AbortSignal): Promise<void> {
    loading.value = true
    error.value = null
    try {
      alerts.value = await listAlerts(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取告警列表失败'
    } finally {
      loading.value = false
    }
  }

  async function fetchRules(signal?: AbortSignal): Promise<void> {
    try {
      rules.value = await listAlertRules(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取告警规则失败'
    }
  }

  async function addRule(rule: AlertRule): Promise<void> {
    try {
      await addAlertRule(rule)
      await fetchRules()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '添加告警规则失败'
      throw e
    }
  }

  return { alerts, rules, loading, error, fetchAlerts, fetchRules, addRule }
})
