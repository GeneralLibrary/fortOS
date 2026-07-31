import { get, post, del } from './client'
import type {
  ServiceDefinition, AgentTemplate, LogEntry, DeployAgentRequest,
  InstallAgentTemplateRequest, ActionSuccessResponse,
} from '@/types'

export function listAgents(signal?: AbortSignal): Promise<ServiceDefinition[]> {
  return get<ServiceDefinition[]>('/api/agents', {}, signal)
}

export function deployAgent(request: DeployAgentRequest): Promise<ServiceDefinition> {
  return post<ServiceDefinition>('/api/agents/deploy', request)
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
