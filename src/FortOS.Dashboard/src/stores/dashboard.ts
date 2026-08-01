// ============================================================================
// FortOS Dashboard — Dashboard Store
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
  /** Endpoints that failed during the last poll, so the UI can show errors instead of silent empty states. */
  const failedEndpoints = shallowRef<Set<string>>(new Set())
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
      const failed = new Set<string>()
      if (metricsResult.status === 'fulfilled') systemMetrics.value = metricsResult.value
      else failed.add('metrics')
      if (alertsResult.status === 'fulfilled') activeAlerts.value = alertsResult.value
      else failed.add('alerts')
      if (servicesResult.status === 'fulfilled') services.value = servicesResult.value
      else failed.add('services')
      if (agentsResult.status === 'fulfilled') agents.value = agentsResult.value
      else failed.add('agents')
      if (disksResult.status === 'fulfilled') disks.value = disksResult.value
      else failed.add('disks')
      failedEndpoints.value = failed

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
    failedEndpoints,
    loading,
    error,
    lastUpdated,
    fetchAll,
    startPolling,
    stopPolling,
  }
})
