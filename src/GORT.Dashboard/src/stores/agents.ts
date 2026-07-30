// ============================================================================
// GORT Dashboard — Agents Store
// Manages Docker container agents: listing, deploy, start/stop, logs.
// ============================================================================

import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'
import {
  listAgents, deployAgent, startAgent, stopAgent, deleteAgent,
  getAgentLogs, listAgentCatalog, searchAgentCatalog,
} from '@/api/agents'
import type { ServiceDefinition, AgentTemplate, LogEntry, AgentConfig, DeployAgentRequest } from '@/types'
import { ApiError } from '@/api/client'

export const useAgentsStore = defineStore('agents', () => {
  const agents = shallowRef<ServiceDefinition[]>([])
  const catalog = shallowRef<AgentTemplate[]>([])
  const selectedAgentLogs = shallowRef<LogEntry[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchAgents(signal?: AbortSignal): Promise<void> {
    loading.value = true
    error.value = null
    try {
      agents.value = await listAgents(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取 Agent 列表失败'
    } finally {
      loading.value = false
    }
  }

  async function deploy(templateId: string, config: AgentConfig): Promise<ServiceDefinition> {
    loading.value = true
    error.value = null
    try {
      const deployed = await deployAgent({ templateId, config } satisfies DeployAgentRequest)
      await fetchAgents()
      return deployed
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : '部署 Agent 失败'
      error.value = msg
      throw e
    } finally {
      loading.value = false
    }
  }

  async function start(agentId: string): Promise<void> {
    try {
      await startAgent(agentId)
      await fetchAgents()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '启动 Agent 失败'
      throw e
    }
  }

  async function stop(agentId: string): Promise<void> {
    try {
      await stopAgent(agentId)
      await fetchAgents()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '停止 Agent 失败'
      throw e
    }
  }

  async function remove(agentId: string): Promise<void> {
    try {
      await deleteAgent(agentId)
      await fetchAgents()
    } catch (e) {
      error.value = e instanceof ApiError ? e.message : '删除 Agent 失败'
      throw e
    }
  }

  async function fetchLogs(agentId: string, tail = 100): Promise<void> {
    try {
      selectedAgentLogs.value = await getAgentLogs(agentId, tail)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取 Agent 日志失败'
    }
  }

  async function fetchCatalog(signal?: AbortSignal): Promise<void> {
    try {
      catalog.value = await listAgentCatalog(signal)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '获取模板目录失败'
    }
  }

  async function searchCatalog(query: string): Promise<AgentTemplate[]> {
    return searchAgentCatalog(query)
  }

  return {
    agents, catalog, selectedAgentLogs, loading, error,
    fetchAgents, deploy, start, stop, remove, fetchLogs, fetchCatalog, searchCatalog,
  }
})
