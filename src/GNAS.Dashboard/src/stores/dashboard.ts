// ============================================================================
// GNAS Dashboard — Dashboard Store
// Manages the main overview page data: host metrics, alerts,
// recent logs, and service summary. Polls the backend periodically.
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import { getSystemMetrics } from '@/api/metrics'
import { listAlerts } from '@/api/alerts'
import { listServices } from '@/api/services'
import { listAgents } from '@/api/agents'
import { listDisks } from '@/api/disks'
import type { SystemMetricsSnapshot, ActiveAlert, ServiceStatusInfo, ServiceDefinition, DiskInfo } from '@/types'

/** Polling interval in milliseconds for the dashboard overview. */
const POLL_INTERVAL_MS = 5_000

export const useDashboardStore = defineStore('dashboard', () => {
  // ---- State ----

  const systemMetrics = shallowRef<SystemMetricsSnapshot | null>(null)
  const activeAlerts = shallowRef<ActiveAlert[]>([])
  const services = shallowRef<ServiceStatusInfo[]>([])
  const agents = shallowRef<ServiceDefinition[]>([])
  const disks = shallowRef<DiskInfo[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const lastUpdated = ref<Date | null>(null)

  let pollTimer: ReturnType<typeof setInterval> | null = null

  // ---- Actions ----

  /**
   * Fetch all dashboard data in parallel.
   */
  async function fetchAll(signal?: AbortSignal): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const results = await Promise.allSettled([
        getSystemMetrics(signal),
        listAlerts(signal),
        listServices(signal),
        listAgents(signal),
        listDisks(signal),
      ])

      // Use allSettled so one failing endpoint doesn't crash the whole page.
      const [metricsResult, alertsResult, servicesResult, agentsResult, disksResult] = results

      if (metricsResult.status === 'fulfilled') systemMetrics.value = metricsResult.value
      if (alertsResult.status === 'fulfilled') activeAlerts.value = alertsResult.value
      if (servicesResult.status === 'fulfilled') services.value = servicesResult.value
      if (agentsResult.status === 'fulfilled') agents.value = agentsResult.value
      if (disksResult.status === 'fulfilled') disks.value = disksResult.value

      lastUpdated.value = new Date()
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取仪表盘数据失败'
    } finally {
      loading.value = false
    }
  }

  /**
   * Start periodic polling.
   */
  function startPolling(): void {
    stopPolling()
    fetchAll()
    pollTimer = setInterval(() => fetchAll(), POLL_INTERVAL_MS)
  }

  /**
   * Stop periodic polling.
   */
  function stopPolling(): void {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  return {
    systemMetrics,
    activeAlerts,
    services,
    agents,
    disks,
    loading,
    error,
    lastUpdated,
    fetchAll,
    startPolling,
    stopPolling,
  }
})
