import { get, post } from './client'
import type { DiskInfo, SmartData, PathRequest, RaidMetrics, RaidResult, RaidCapability, RaidLevel, DeviceStatus } from '@/types'

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

/** Query filesystem / mount status of an arbitrary block device (e.g. an md array). */
export function getDeviceStatus(path: string, signal?: AbortSignal): Promise<DeviceStatus> {
  return get<DeviceStatus>('/api/disks/device-status', { path }, signal)
}

/** Format a block device. Destructive — wipes all data on it. */
export function formatDevice(device: string, fsType: string): Promise<void> {
  return post<void>('/api/disks/format', { device, fsType })
}

/** Mount a formatted device and persist the entry in /etc/fstab. */
export function mountDevice(device: string, mountPoint: string, fsType: string): Promise<void> {
  return post<void>('/api/disks/mount', { device, mountPoint, fsType })
}

/** Unmount a filesystem and remove its /etc/fstab entry. */
export function unmountDevice(mountPoint: string): Promise<void> {
  return post<void>('/api/disks/unmount', { mountPoint })
}
