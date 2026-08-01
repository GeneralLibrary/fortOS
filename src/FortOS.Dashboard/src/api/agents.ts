import { get, post, del } from './client'
import type {
  ServiceDefinition, AgentTemplate, LogEntry, DeployAgentRequest, AgentAccessInfo,
  AgentDeploymentStatus, InstallAgentTemplateRequest, ActionSuccessResponse,
} from '@/types'

export function listAgents(signal?: AbortSignal): Promise<ServiceDefinition[]> {
  return get<ServiceDefinition[]>('/api/agents', {}, signal)
}

/** Starts an asynchronous deployment; poll getDeployStatus until it finishes. */
export function deployAgent(request: DeployAgentRequest): Promise<{ agentId: string; status: string }> {
  return post<{ agentId: string; status: string }>('/api/agents/deploy', request)
}

export function getDeployStatus(agentId: string, signal?: AbortSignal): Promise<AgentDeploymentStatus> {
  return get<AgentDeploymentStatus>(`/api/agents/${encodeURIComponent(agentId)}/deploy-status`, {}, signal)
}

export function startAgent(agentId: string): Promise<ActionSuccessResponse> {
  return post<ActionSuccessResponse>(`/api/agents/${encodeURIComponent(agentId)}/start`)
}

export function stopAgent(agentId: string): Promise<ActionSuccessResponse> {
  return post<ActionSuccessResponse>(`/api/agents/${encodeURIComponent(agentId)}/stop`)
}

export function deleteAgent(agentId: string): Promise<ActionSuccessResponse> {
  return del<ActionSuccessResponse>(`/api/agents/${encodeURIComponent(agentId)}`)
}

export function getAgentLogs(agentId: string, tail = 100): Promise<LogEntry[]> {
  return get<LogEntry[]>(`/api/agents/${encodeURIComponent(agentId)}/logs`, { tail })
}

/** External access info: ports, environment variable names, integration notes. */
export function getAgentAccess(agentId: string, signal?: AbortSignal): Promise<AgentAccessInfo> {
  return get<AgentAccessInfo>(`/api/agents/${encodeURIComponent(agentId)}/access`, {}, signal)
}

export function listAgentCatalog(signal?: AbortSignal): Promise<AgentTemplate[]> {
  return get<AgentTemplate[]>('/api/agents/catalog', {}, signal)
}

export function searchAgentCatalog(query: string): Promise<AgentTemplate[]> {
  return get<AgentTemplate[]>('/api/agents/catalog/search', { query })
}

export function installAgentTemplate(source: string): Promise<AgentTemplate> {
  return post<AgentTemplate>('/api/agents/catalog/install', {
    source,
  } satisfies InstallAgentTemplateRequest)
}

export function updateAgentTemplate(templateId: string): Promise<AgentTemplate> {
  return post<AgentTemplate>(`/api/agents/catalog/${encodeURIComponent(templateId)}/update`)
}
