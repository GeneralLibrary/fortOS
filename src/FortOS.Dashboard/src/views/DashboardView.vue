<!--
  FortOS Dashboard — System Overview

  布局原则:信息分块,但块之间聚合不碎片化。
  - 顶部「系统状态」一块:CPU/内存/存储三环 + 运行时间/网络/告警/服务关键指标,
    核心信息一眼看全,不再散成独立小卡。
  - 中部两列:左「存储」(文件系统/磁盘标签切换),右「服务与告警」(标签切换)。
  - 底部次要:网络接口、Agent 容器。
  - 数据有效性:SMART/温度缺失显示 "—";无传感器时不显示温度;无冗余重复指标。
-->
<script setup lang="ts">
import { onMounted, onUnmounted, computed, h } from 'vue'
import { useDashboardStore } from '@/stores/dashboard'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import EmptyState from '@/components/common/EmptyState.vue'
import {
  formatBytes, formatUptime, formatPercent, formatBytesPerSecond,
  formatDateTime, formatTemperature, serviceStatusType, severityColor,
} from '@/utils/format'
import type { DataTableColumns } from 'naive-ui'
import type {
  DiskInfo, ServiceStatusInfo, ServiceDefinition,
  ActiveAlert, FileSystemCapacityMetrics, NetworkTrafficMetrics,
} from '@/types'

const dashboard = useDashboardStore()
const router = useRouter()
const { t } = useI18n()

onMounted(() => dashboard.startPolling())
onUnmounted(() => dashboard.stopPolling())

const metrics = computed(() => dashboard.systemMetrics)

// ---- Core metrics ----

const cpuPct = computed(() => metrics.value ? Math.round(metrics.value.cpu.usagePercent) : 0)
const cpuDetail = computed(() => {
  if (!metrics.value) return '—'
  return `${t('dashboard.loadAverage')} ${metrics.value.host.loadAverage1.toFixed(2)} · ${metrics.value.cpu.logicalProcessorCount} ${t('dashboard.logicalCores')}`
})

const memUsed = computed(() => metrics.value ? metrics.value.memory.usedBytes : 0)
const memTotal = computed(() => metrics.value ? metrics.value.memory.totalBytes : 1)
const memPct = computed(() => Math.round(memUsed.value / memTotal.value * 100))
const memDetail = computed(() => `${formatBytes(memUsed.value)} / ${formatBytes(memTotal.value)}`)

const storageTotal = computed(() => metrics.value?.fileSystems?.length
  ? metrics.value.fileSystems.reduce((s, fs) => s + (fs.totalBytes ?? 0), 0)
  : 0)
const storageUsed = computed(() => metrics.value?.fileSystems?.length
  ? metrics.value.fileSystems.reduce((s, fs) => s + (fs.usedBytes ?? 0), 0)
  : 0)
const storagePct = computed(() => storageTotal.value ? Math.round(storageUsed.value / storageTotal.value * 100) : 0)
const storageDetail = computed(() => `${formatBytes(storageUsed.value)} / ${formatBytes(storageTotal.value)}`)

const hasCpuTemp = computed(() => metrics.value?.cpu.temperatureCelsius != null)

const uptimeText = computed(() => metrics.value ? formatUptime(metrics.value.host.uptime) : '—')
const bootedAtText = computed(() =>
  metrics.value ? `${t('dashboard.bootedAt')} ${formatDateTime(metrics.value.host.bootedAt, 'short')}` : '',
)

const netRx = computed(() => metrics.value?.networks
  ?.filter(n => n.isUp)
  .reduce((s, n) => s + (n.receiveBytesPerSecond ?? 0), 0) ?? 0)
const netTx = computed(() => metrics.value?.networks
  ?.filter(n => n.isUp)
  .reduce((s, n) => s + (n.transmitBytesPerSecond ?? 0), 0) ?? 0)
const connectionsText = computed(() =>
  metrics.value ? `${metrics.value.networkStack.establishedConnections} ${t('dashboard.connections')}` : '',
)

const criticalAlerts = computed(() =>
  dashboard.activeAlerts.filter(a =>
    a.severity.toLowerCase() === 'critical' || a.severity.toLowerCase() === 'error',
  ).length,
)
const alertColor = computed(() =>
  criticalAlerts.value > 0 ? '#ef4444'
    : dashboard.activeAlerts.length > 0 ? '#f59e0b'
      : '#34c759',
)

const runningServiceCount = computed(() =>
  dashboard.services.filter(s => s.status === 'Running').length,
)

// ---- Gauge rendering ----

function gaugeColor(pct: number): string {
  if (pct >= 90) return '#ef4444'
  if (pct >= 70) return '#f59e0b'
  if (pct >= 50) return '#4a90d9'
  return '#34c759'
}

function gaugePath(cx: number, cy: number, r: number, pct: number): { d: string; color: string } {
  const color = gaugeColor(pct)
  if (pct <= 0) return { d: '', color }
  const startAngle = -Math.PI / 2
  const endAngle = startAngle + (pct / 100) * 2 * Math.PI
  const x1 = cx + r * Math.cos(startAngle)
  const y1 = cy + r * Math.sin(startAngle)
  const x2 = cx + r * Math.cos(endAngle)
  const y2 = cy + r * Math.sin(endAngle)
  return {
    d: `M ${x1} ${y1} A ${r} ${r} 0 ${pct > 50 ? 1 : 0} 1 ${x2} ${y2}`,
    color,
  }
}

function renderGauge(pct: number, label: string, detail: string) {
  const arc = gaugePath(44, 44, 34, pct)
  return h('div', { class: 'zs-gauge-item' }, [
    h('div', { class: 'zs-gauge' }, [
      h('svg', { width: 88, height: 88, viewBox: '0 0 88 88' }, [
        h('circle', { cx: 44, cy: 44, r: 34, fill: 'none', stroke: 'var(--zs-border)', 'stroke-width': 8 }),
        arc.d ? h('path', { d: arc.d, fill: 'none', stroke: arc.color, 'stroke-width': 8, 'stroke-linecap': 'round' }) : null,
      ]),
      h('div', { class: 'zs-gauge-center' }, [
        h('span', { class: 'zs-gauge-pct' }, `${pct}%`),
        h('span', { class: 'zs-gauge-label' }, label),
      ]),
    ]),
    h('div', { class: 'zs-gauge-detail' }, detail),
  ])
}

// ---- Status side item ----

function statusItem(label: string, value: string, sub?: string) {
  return h('div', { class: 'zs-status-item' }, [
    h('span', { class: 'zs-status-label' }, label),
    h('span', { class: 'zs-status-value' }, value),
    sub ? h('span', { class: 'zs-status-sub' }, sub) : null,
  ])
}

// ---- Table columns ----

const filesystemColumns: DataTableColumns<FileSystemCapacityMetrics> = [
  { title: () => t('dashboard.mountPoint'), key: 'mountPoint', ellipsis: { tooltip: true }, width: 130 },
  { title: () => t('dashboard.filesystemType'), key: 'fileSystemType', width: 80, render: (r) => r.fileSystemType ?? '—' },
  { title: () => t('dashboard.capacity'), key: 'totalBytes', width: 100, render: (r) => formatBytes(r.totalBytes) },
  { title: () => t('dashboard.used'), key: 'usedBytes', width: 100, render: (r) => formatBytes(r.usedBytes) },
  { title: () => t('dashboard.usage'), key: 'usedPercent', width: 130,
    render: (r) => h(NProgress, {
      type: 'line',
      color: r.usedPercent > 90 ? '#d03050' : r.usedPercent > 75 ? '#f0a020' : '#18a058',
      percentage: Math.round(r.usedPercent),
      showIndicator: false,
      height: 16,
      borderRadius: '4px',
    }),
  },
]

const diskColumns: DataTableColumns<DiskInfo> = [
  { title: () => t('dashboard.devicePath'), key: 'path', ellipsis: { tooltip: true }, width: 130 },
  { title: () => t('dashboard.model'), key: 'model', ellipsis: { tooltip: true } },
  { title: () => t('dashboard.capacity'), key: 'sizeBytes', width: 100, render: (r) => formatBytes(r.sizeBytes) },
  { title: () => t('dashboard.smartStatus'), key: 'smartStatus', width: 100,
    render: (r) => r.smartStatus
      ? h(NTag, {
          type: r.smartStatus.toLowerCase() === 'ok' || r.smartStatus.toLowerCase() === 'passed' ? 'success' : 'warning',
          size: 'small',
        }, { default: () => r.smartStatus })
      : '—',
  },
  { title: () => t('dashboard.temperature'), key: 'temperatureCelsius', width: 80, render: (r) => formatTemperature(r.temperatureCelsius) },
  { title: () => t('dashboard.usage'), key: 'usedPercent', width: 80, render: (r) => formatPercent(r.usedPercent) },
]

const networkColumns: DataTableColumns<NetworkTrafficMetrics> = [
  { title: () => t('dashboard.interface'), key: 'interface', width: 120 },
  { title: () => t('common.status'), key: 'isUp', width: 90,
    render: (r) => h(NTag, { type: r.isUp ? 'success' : 'default', size: 'small' }, { default: () => r.isUp ? t('common.up') : t('common.down') }),
  },
  { title: () => `${t('dashboard.rxRate')} ↓`, key: 'receiveBytesPerSecond', width: 110, render: (r) => formatBytesPerSecond(r.receiveBytesPerSecond) },
  { title: () => `${t('dashboard.txRate')} ↑`, key: 'transmitBytesPerSecond', width: 110, render: (r) => formatBytesPerSecond(r.transmitBytesPerSecond) },
]

const serviceColumns: DataTableColumns<ServiceStatusInfo> = [
  { title: 'ID', key: 'serviceId', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('common.status'), key: 'status', width: 90,
    render: (r) => h(NTag, { type: serviceStatusType(r.status), size: 'small' }, { default: () => r.status }),
  },
  { title: () => t('services.cpu'), key: 'cpuPercent', width: 70, render: (r) => `${r.cpuPercent.toFixed(1)}%` },
  { title: () => t('services.memory'), key: 'memoryBytes', width: 90, render: (r) => formatBytes(r.memoryBytes) },
  { title: () => t('services.uptime'), key: 'uptime', width: 100, render: (r) => formatUptime(r.uptime) },
]

const agentColumns: DataTableColumns<ServiceDefinition> = [
  { title: 'ID', key: 'serviceId', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('common.name'), key: 'displayName', ellipsis: { tooltip: true } },
  { title: () => t('common.type'), key: 'type', width: 80 },
]

const alertColumns: DataTableColumns<ActiveAlert> = [
  { title: () => t('alerts.severity'), key: 'severity', width: 80,
    render: (r) => h(NTag, { type: severityColor(r.severity), size: 'small' }, { default: () => r.severity }),
  },
  { title: () => t('alerts.message'), key: 'message', ellipsis: { tooltip: true } },
  { title: () => t('alerts.triggeredAt'), key: 'triggeredAt', width: 170, render: (r) => formatDateTime(r.triggeredAt) },
]
</script>

<template>
  <div class="zs-dashboard">
    <!-- ===== 系统状态:核心指标聚合成一块 ===== -->
    <NCard class="zs-status-card" :bordered="true" size="small">
      <div class="zs-status-body">
        <!-- 左:三环 -->
        <div class="zs-gauges">
          <component :is="renderGauge(cpuPct, 'CPU', cpuDetail)" />
          <component :is="renderGauge(memPct, 'RAM', memDetail)" />
          <component :is="renderGauge(storagePct, t('nav.storage'), storageDetail)" />
        </div>
        <!-- 右:关键指标,紧凑排列 -->
        <div class="zs-status-side">
          <component :is="statusItem(t('dashboard.hostUptime'), uptimeText, bootedAtText || undefined)" />
          <component :is="statusItem(
            t('dashboard.networkTraffic'),
            metrics?.networks?.length ? `↓ ${formatBytesPerSecond(netRx)}  ↑ ${formatBytesPerSecond(netTx)}` : '—',
            connectionsText || undefined,
          )" />
          <component :is="statusItem(
            t('dashboard.activeAlerts'),
            dashboard.activeAlerts.length ? String(dashboard.activeAlerts.length) : t('dashboard.normal'),
            dashboard.activeAlerts.length ? `${criticalAlerts} ${t('dashboard.criticalAlerts')}` : undefined,
          )" />
          <component :is="statusItem(
            t('dashboard.servicesStatus'),
            dashboard.services.length ? `${runningServiceCount}/${dashboard.services.length}` : '—',
            dashboard.services.length ? t('dashboard.servicesRunningOf') : undefined,
          )" />
          <component
            v-if="hasCpuTemp"
            :is="statusItem(t('dashboard.cpuTemperature'), formatTemperature(metrics!.cpu.temperatureCelsius))"
          />
        </div>
      </div>
    </NCard>

    <!-- ===== 详情区:两列,相关块合并 ===== -->
    <div class="zs-dashboard-grid">
      <!-- 左列:存储 + 网络 -->
      <div class="zs-dashboard-col">
        <NCard :title="t('nav.storage')" size="small" :bordered="true" class="zs-dashboard-card">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Storage' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <NTabs type="segment" animated>
            <NTabPane name="filesystems" :tab="t('dashboard.filesystems')">
              <NDataTable
                v-if="metrics?.fileSystems?.length"
                :columns="filesystemColumns" :data="metrics.fileSystems"
                :bordered="false" size="small" :max-height="260" striped
              />
              <EmptyState v-else :message="t('dashboard.noFilesystems')" />
            </NTabPane>
            <NTabPane name="disks" :tab="t('dashboard.disks')">
              <NDataTable
                v-if="dashboard.disks.length"
                :columns="diskColumns" :data="dashboard.disks"
                :bordered="false" size="small" :max-height="260" striped
              />
              <EmptyState v-else :message="t('dashboard.noDisks')" />
            </NTabPane>
          </NTabs>
        </NCard>

        <NCard :title="t('dashboard.networkInterfaces')" size="small" :bordered="true" class="zs-dashboard-card" style="margin-top: 16px">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Network' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <NDataTable
            v-if="metrics?.networks?.length"
            :columns="networkColumns" :data="metrics.networks"
            :bordered="false" size="small" :max-height="200" striped
          />
          <EmptyState v-else :message="t('dashboard.noNetwork')" />
        </NCard>
      </div>

      <!-- 右列:服务与告警 + Agent -->
      <div class="zs-dashboard-col">
        <NCard :title="t('dashboard.servicesStatus')" size="small" :bordered="true" class="zs-dashboard-card">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Services' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <NTabs type="segment" animated>
            <NTabPane name="services" :tab="t('dashboard.servicesStatus')">
              <NDataTable
                v-if="dashboard.services.length"
                :columns="serviceColumns" :data="dashboard.services"
                :bordered="false" size="small" :max-height="260" striped
              />
              <EmptyState v-else-if="dashboard.failedEndpoints.has('services')" :message="t('dashboard.loadFailed')" :description="t('dashboard.loadFailedHint')" />
              <EmptyState v-else :message="t('dashboard.noServices')" />
            </NTabPane>
            <NTabPane name="alerts" :tab="t('dashboard.activeAlerts')">
              <NDataTable
                v-if="dashboard.activeAlerts.length"
                :columns="alertColumns" :data="dashboard.activeAlerts"
                :bordered="false" size="small" :max-height="260" striped
              />
              <EmptyState v-else :message="t('dashboard.noAlerts')" :description="t('dashboard.allNormal')" />
            </NTabPane>
          </NTabs>
        </NCard>

        <NCard :title="t('dashboard.agentContainers')" size="small" :bordered="true" class="zs-dashboard-card" style="margin-top: 16px">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Agents' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <NDataTable
            v-if="dashboard.agents.length"
            :columns="agentColumns" :data="dashboard.agents"
            :bordered="false" size="small" :max-height="180" striped
          />
          <EmptyState v-else-if="dashboard.failedEndpoints.has('agents')" :message="t('dashboard.loadFailed')" :description="t('dashboard.loadFailedHint')" />
          <EmptyState v-else :message="t('dashboard.noAgents')" />
        </NCard>
      </div>
    </div>
  </div>
</template>

<script lang="ts">
import { NTag, NProgress } from 'naive-ui'
</script>

<style scoped>
.zs-dashboard {
  max-width: 1400px;
  margin: 0 auto;
}

/* ---- 系统状态块 ---- */
.zs-status-card {
  border-radius: var(--zs-radius-xl) !important;
  border-color: var(--zs-border) !important;
  margin-bottom: 16px;
}
.zs-status-body {
  display: flex;
  align-items: center;
  gap: 40px;
  padding: 8px 8px;
  flex-wrap: wrap;
}
.zs-gauges {
  display: flex;
  gap: 48px;
  flex: 0 0 auto;
}
.zs-gauge-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  min-width: 100px;
}
.zs-gauge {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.zs-gauge-center {
  position: absolute;
  display: flex;
  flex-direction: column;
  align-items: center;
  line-height: 1.2;
}
.zs-gauge-pct {
  font-size: 18px;
  font-weight: 800;
  color: var(--zs-text-primary);
  font-variant-numeric: tabular-nums;
}
.zs-gauge-label {
  font-size: 10px;
  color: var(--zs-text-tertiary);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}
.zs-gauge-detail {
  font-size: 11px;
  color: var(--zs-text-secondary);
  white-space: nowrap;
  text-align: center;
}

/* 右侧关键指标:网格排列,紧凑不散落 */
.zs-status-side {
  flex: 1 1 320px;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 8px 24px;
  min-width: 0;
}
.zs-status-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 6px 0;
  min-width: 0;
}
.zs-status-label {
  font-size: 12px;
  color: var(--zs-text-tertiary);
  font-weight: 500;
}
.zs-status-value {
  font-size: 15px;
  font-weight: 700;
  color: var(--zs-text-primary);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.zs-status-sub {
  font-size: 11px;
  color: var(--zs-text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* ---- 详情区 ---- */
.zs-dashboard-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
@media (max-width: 960px) {
  .zs-dashboard-grid {
    grid-template-columns: 1fr;
  }
  .zs-gauges {
    gap: 24px;
    flex-wrap: wrap;
    justify-content: center;
  }
  .zs-status-body {
    justify-content: center;
  }
}
.zs-dashboard-col {
  display: flex;
  flex-direction: column;
}

/* Make NCards match zspace */
:deep(.zs-dashboard-card) {
  border-radius: var(--zs-radius-lg) !important;
  border-color: var(--zs-border) !important;
}
</style>
