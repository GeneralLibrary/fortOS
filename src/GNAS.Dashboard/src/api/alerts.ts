import { get, post } from './client'
import type { ActiveAlert, AlertRule, ActionSuccessResponse } from '@/types'

export function listAlerts(signal?: AbortSignal): Promise<ActiveAlert[]> {
  return get<ActiveAlert[]>('/api/alerts', {}, signal)
}

export function listAlertRules(signal?: AbortSignal): Promise<AlertRule[]> {
  return get<AlertRule[]>('/api/alerts/rules', {}, signal)
}

export function addAlertRule(rule: AlertRule): Promise<ActionSuccessResponse> {
  return post<ActionSuccessResponse>('/api/alerts/rules', rule)
}
