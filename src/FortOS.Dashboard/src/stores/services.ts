// ============================================================================
// FortOS Dashboard — Services Store
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import { listServices, startService, stopService } from '@/api/services'
import type { ServiceStatusInfo } from '@/types'
import { ApiError } from '@/api/client'

export const useServicesStore = defineStore('services', () => {
  const services = shallowRef<ServiceStatusInfo[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchServices(signal?: AbortSignal): Promise<void> {
    loading.value = true
    error.value = null
    try {
      services.value = await listServices(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取服务列表失败'
    } finally {
      loading.value = false
    }
  }

  async function start(id: string): Promise<void> {
    try {
      await startService(id)
      await fetchServices()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '启动服务失败'
      throw e
    }
  }

  async function stop(id: string): Promise<void> {
    try {
      await stopService(id)
      await fetchServices()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '停止服务失败'
      throw e
    }
  }

  return { services, loading, error, fetchServices, start, stop }
})
