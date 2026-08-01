import { get, post } from './client'
import type { DiskInfo, SmartData, PathRequest, RaidMetrics, RaidResult, RaidCapability, RaidLevel } from '@/types'

export function listDisks(signal?: AbortSignal): Promise<DiskInfo[]> {
  return get<DiskInfo[]>('/api/disks', {}, signal)
}

export function getDiskDetail(path: string, signal?: AbortSignal): Promise<DiskInfo> {
  return get<DiskInfo>('/api/disks/detail', { path }, signal)
}

export function runSmartCheck(path: string): Promise<SmartData> {
  return post<SmartData>('/api/disks/smart-check', { path } satisfies PathRequest)
}

export function listRaids(signal?: AbortSignal): Promise<RaidMetrics[]> {
  return get<RaidMetrics[]>('/api/disks/raids', {}, signal)
}

/** Whether the RAID tool (mdadm) is installed on this host. */
export function getRaidCapability(signal?: AbortSignal): Promise<RaidCapability> {
  return get<RaidCapability>('/api/disks/raid-capability', {}, signal)
}

export function createRaid(level: RaidLevel, diskPaths: string[], confirm: boolean): Promise<RaidResult> {
  return post<RaidResult>('/api/disks/raids', { level, diskPaths, confirm })
}
