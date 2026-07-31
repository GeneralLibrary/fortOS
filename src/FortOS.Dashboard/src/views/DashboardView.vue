<!--
  FortOS Dashboard — 极空间-style System Overview
  Circular gauge rings for CPU/Memory/Storage, stat cards, and detail tables.
-->
<script setup lang="ts">
import { onMounted, onUnmounted, computed, h } from 'vue'
import { useDashboardStore } from '@/stores/dashboard'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import {
  ServerOutline, PulseOutline, SaveOutline,
  WifiOutline, AlertCircleOutline,
} from '@vicons/ionicons5'
import StatCard from '@/components/common/StatCard.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import {
  formatBytes, formatUptime, formatPercent, formatBytesPerSecond,
  formatDateTime, formatTemperature, serviceStatusType, severityColor,
} from '@/utils/format'
import type { DataTableColumns } from 'naive-ui'
import type {
  DiskInfo, ServiceStatusInfo, ServiceDefinition,
  ActiveAlert, FileSystemCapacityMetrics,
} from '@/types'

const dashboard = useDashboardStore()
const router = useRouter()
const { t } = useI18n()

onMounted(() => dashboard.startPolling())
onUnmounted(() => dashboard.stopPolling())

// ---- Computed metrics ----

const metrics = computed(() => dashboard.systemMetrics)

const cpuPct = computed(() => metrics.value ? Math.round(metrics.value.cpu.usagePercent) : 0)
const memUsed = computed(() => metrics.value ? metrics.value.memory.usedBytes : 0)
const memTotal = computed(() => metrics.value ? metrics.value.memory.totalBytes : 1)
const memPct = computed(() => Math.round(memUsed.value / memTotal.value * 100))

/** Aggregate storage across all filesystems. */
const storageTotal = computed(() => {
  if (!metrics.value?.fileSystems?.length) return 0
  return metrics.value.fileSystems.reduce((s, fs) => s + (fs.totalBytes ?? 0), 0)
})
const storageUsed = computed(() => {
  if (!metrics.value?.fileSystems?.length) return 0
  return metrics.value.fileSystems.reduce((s, fs) => s + (fs.usedBytes ?? 0), 0)
})
const storagePct = computed(() => storageTotal.value ? Math.round(storageUsed.value / storageTotal.value * 100) : 0)

const criticalAlerts = computed(() =>
  dashboard.activeAlerts.filter(a =>
    a.severity.toLowerCase() === 'critical' || a.severity.toLowerCase() === 'error',
  ).length,
)

const healthyDiskPercent = computed(() => {
  const disks = dashboard.disks
  if (!disks.length) return 100
  const healthy = disks.filter(d => d.smartStatus?.toLowerCase() === 'ok' || d.smartStatus?.toLowerCase() === 'passed').length
  return Math.round((healthy / disks.length) * 100)
})

const runningServiceCount = computed(() =>
  dashboard.services.filter(s => s.status === 'Running').length,
)

/** Gauge track color based on percentage. */
function gaugeColor(pct: number): string {
  if (pct >= 90) return '#ef4444'
  if (pct >= 70) return '#f59e0b'
  if (pct >= 50) return '#4a90d9'
  return '#34c759'
}

/** SVG circular gauge props. */
interface GaugeArc {
  cx: number; cy: number; r: number
  stroke: string; pct: number
}
function gaugePath({ cx, cy, r, stroke, pct }: GaugeArc): { d: string; color: string } {
  const color = gaugeColor(pct)
  if (pct <= 0) {
    return { d: '', color }
  }
  const circumference = 2 * Math.PI * r
  const length = (pct / 100) * circumference
  // Arc starts at 12 o'clock (-90deg), draws clockwise
  const startAngle = -Math.PI / 2
  const endAngle = startAngle + (pct / 100) * 2 * Math.PI
  const x1 = cx + r * Math.cos(startAngle)
  const y1 = cy + r * Math.sin(startAngle)
  const x2 = cx + r * Math.cos(endAngle)
  const y2 = cy + r * Math.sin(endAngle)
  const largeArc = pct > 50 ? 1 : 0
  return {
    d: `M ${x1} ${y1} A ${r} ${r} 0 ${largeArc} 1 ${x2} ${y2}`,
    color,
  }
}

/** Render the SVG gauge inline (so it works in scoped styles without h()). */
function renderGauge(pct: number, label: string, detail: string) {
  const arc = gaugePath({ cx: 44, cy: 44, r: 34, stroke: '', pct })
  return h('div', { class: 'zs-gauge-item', onClick: label === 'CPU' ? () => router.push({ name: 'Services' }) : label === 'RAM' ? () => router.push({ name: 'Services' }) : () => router.push({ name: 'Storage' }) }, [
    h('div', { class: 'zs-gauge' }, [
      h('svg', { width: 88, height: 88, viewBox: '0 0 88 88' }, [
        // Background track
        h('circle', { cx: 44, cy: 44, r: 34, fill: 'none', stroke: 'var(--zs-border)', 'stroke-width': 8 }),
        // Colored arc
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

// ---- Table column definitions ----

const diskColumns: DataTableColumns<DiskInfo> = [
  { title: () => t('dashboard.devicePath'), key: 'path', ellipsis: { tooltip: true }, width: 160 },
  { title: () => t('dashboard.model'), key: 'model', ellipsis: { tooltip: true } },
  { title: () => t('dashboard.capacity'), key: 'sizeBytes', render: (r) => formatBytes(r.sizeBytes) },
  { title: () => t('dashboard.smartStatus') ?? 'SMART', key: 'smartStatus', width: 90,
    render: (r) => h(NTag, {
      type: r.smartStatus?.toLowerCase() === 'ok' || r.smartStatus?.toLowerCase() === 'passed' ? 'success' : 'warning',
      size: 'small',
    }, { default: () => r.smartStatus ?? t('common.unknown') }),
  },
  { title: () => t('dashboard.temperature'), key: 'temperatureCelsius', width: 70, render: (r) => formatTemperature(r.temperatureCelsius) },
  { title: () => t('dashboard.usage'), key: 'usedPercent', width: 80, render: (r) => formatPercent(r.usedPercent) },
]

const serviceColumns: DataTableColumns<ServiceStatusInfo> = [
  { title: 'ID', key: 'serviceId', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('common.status'), key: 'status', width: 90,
    render: (r) => h(NTag, { type: serviceStatusType(r.status), size: 'small' }, { default: () => r.status }),
  },
  { title: () => t('common.type'), key: 'type', width: 90 },
  { title: () => t('services.cpu'), key: 'cpuPercent', width: 70, render: (r) => `${r.cpuPercent.toFixed(1)}%` },
  { title: () => t('services.memory'), key: 'memoryBytes', width: 90, render: (r) => formatBytes(r.memoryBytes) },
  { title: () => t('services.uptime'), key: 'uptime', width: 100, render: (r) => formatUptime(r.uptime) },
]

const agentColumns: DataTableColumns<ServiceDefinition> = [
  { title: 'ID', key: 'serviceId', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('common.name'), key: 'displayName', ellipsis: { tooltip: true } },
  { title: () => t('common.type'), key: 'type', width: 80 },
]

const filesystemColumns: DataTableColumns<FileSystemCapacityMetrics> = [
  { title: () => t('dashboard.mountPoint'), key: 'mountPoint', ellipsis: { tooltip: true } },
  { title: () => t('dashboard.device'), key: 'device', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('dashboard.filesystemType'), key: 'fileSystemType', width: 80 },
  { title: () => t('dashboard.capacity'), key: 'totalBytes', width: 100, render: (r) => formatBytes(r.totalBytes) },
  { title: () => t('dashboard.used'), key: 'usedBytes', width: 100, render: (r) => formatBytes(r.usedBytes) },
  { title: () => t('dashboard.usage'), key: 'usedPercent', width: 80,
    render: (r) => h(NProgress, { type: r.usedPercent > 90 ? 'error' : r.usedPercent > 75 ? 'warning' : 'success', percentage: Math.round(r.usedPercent), showIndicator: false, height: 18, borderRadius: '4px' }),
  },
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
    <!-- Gauges row: CPU / Memory / Storage -->
    <div class="zs-gauges-row">
      <component :is="renderGauge(cpuPct, 'CPU', metrics ? `${metrics.cpu.logicalProcessorCount} ${t('dashboard.logicalCores')} · ${cpuPct}%` : '—')" />
      <component :is="renderGauge(memPct, 'RAM', `${formatBytes(memUsed)} / ${formatBytes(memTotal)}`)" />
      <component :is="renderGauge(storagePct, t('nav.storage'), `${formatBytes(storageUsed)} / ${formatBytes(storageTotal)}`)" />
    </div>

    <!-- Stat pills row -->
    <div class="zs-stats-row">
      <StatCard
        :label="t('dashboard.hostUptime')"
        :value="metrics ? formatUptime(metrics.host.uptime) : '—'"
        :icon="ServerOutline"
        :subtitle="metrics ? `Load ${metrics.host.loadAverage1.toFixed(1)} / ${metrics.host.loadAverage5.toFixed(1)} / ${metrics.host.loadAverage15.toFixed(1)}` : undefined"
        color="#4a90d9"
      />
      <StatCard
        :label="t('dashboard.diskHealth')"
        :value="`${healthyDiskPercent}%`"
        :icon="SaveOutline"
        :color="healthyDiskPercent === 100 ? '#34c759' : healthyDiskPercent >= 80 ? '#f59e0b' : '#ef4444'"
        :subtitle="`${t('dashboard.disksCount', { count: dashboard.disks.length })} · ${t('dashboard.servicesRunning', { count: runningServiceCount })}`"
      />
      <StatCard
        :label="t('dashboard.activeAlerts')"
        :value="dashboard.activeAlerts.length"
        :icon="AlertCircleOutline"
        :color="criticalAlerts > 0 ? '#ef4444' : dashboard.activeAlerts.length > 0 ? '#f59e0b' : '#34c759'"
        :subtitle="`${criticalAlerts} ${t('dashboard.criticalAlerts')}`"
      />
      <StatCard
        :label="t('dashboard.networkTraffic')"
        :value="metrics?.networks?.length ? `${metrics.networks.filter(n => n.isUp).length}/${metrics.networks.length}` : '—'"
        :icon="WifiOutline"
        color="#0ea5e9"
        :subtitle="t('dashboard.networkInterfaces')"
      />
    </div>

    <!-- Content grid: two columns -->
    <div class="zs-dashboard-grid">
      <!-- Left column -->
      <div class="zs-dashboard-col">
        <NCard :title="t('dashboard.disks')" size="small" :bordered="true" class="zs-dashboard-card">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Storage' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <NDataTable
            v-if="dashboard.disks.length"
            :columns="diskColumns" :data="dashboard.disks"
            :bordered="false" size="small" :max-height="280" striped
          />
          <EmptyState v-else :message="t('dashboard.noDisks')" />
        </NCard>

        <NCard :title="t('dashboard.filesystems')" size="small" :bordered="true" class="zs-dashboard-card" style="margin-top: 16px">
          <NDataTable
            v-if="metrics?.fileSystems?.length"
            :columns="filesystemColumns" :data="metrics.fileSystems"
            :bordered="false" size="small" :max-height="220" striped
          />
          <EmptyState v-else :message="t('dashboard.noFilesystems')" />
        </NCard>

        <!-- Network -->
        <NCard :title="t('dashboard.networkTraffic')" size="small" :bordered="true" class="zs-dashboard-card" style="margin-top: 16px">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Network' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <div v-if="metrics?.networks?.length" class="network-list">
            <div v-for="net in metrics.networks.slice(0, 4)" :key="net.interface" class="network-row">
              <div class="network-iface">
                <NTag :type="net.isUp ? 'success' : 'default'" size="small" round>
                  {{ net.interface }}
                </NTag>
                <span class="network-speed" v-if="net.linkSpeedMbps">{{ net.linkSpeedMbps }} Mbps</span>
              </div>
              <div class="network-rates">
                <span class="rate-down">↓ {{ formatBytesPerSecond(net.receiveBytesPerSecond) }}</span>
                <span class="rate-up">↑ {{ formatBytesPerSecond(net.transmitBytesPerSecond) }}</span>
              </div>
            </div>
          </div>
          <EmptyState v-else :message="t('dashboard.noNetwork')" />
        </NCard>
      </div>

      <!-- Right column -->
      <div class="zs-dashboard-col">
        <NCard :title="t('dashboard.servicesStatus')" size="small" :bordered="true" class="zs-dashboard-card">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Services' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <NDataTable
            v-if="dashboard.services.length"
            :columns="serviceColumns" :data="dashboard.services"
            :bordered="false" size="small" :max-height="280" striped
          />
          <EmptyState v-else :message="t('dashboard.noServices')" />
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
          <EmptyState v-else :message="t('dashboard.noAgents')" />
        </NCard>

        <NCard :title="t('dashboard.activeAlerts')" size="small" :bordered="true" class="zs-dashboard-card" style="margin-top: 16px">
          <template #header-extra>
            <NButton text size="small" @click="router.push({ name: 'Alerts' })">{{ t('dashboard.viewDetails') }}</NButton>
          </template>
          <NDataTable
            v-if="dashboard.activeAlerts.length"
            :columns="alertColumns" :data="dashboard.activeAlerts"
            :bordered="false" size="small" :max-height="260" striped
          />
          <EmptyState v-else :message="t('dashboard.noAlerts')" :description="t('dashboard.allNormal')" />
        </NCard>
      </div>
    </div>
  </div>
</template>

<script lang="ts">
import { h } from 'vue'
import { NTag, NProgress } from 'naive-ui'
</script>

<style scoped>
.zs-dashboard {
  max-width: 1400px;
  margin: 0 auto;
}

/* ---- Gauge rings row ---- */
.zs-gauges-row {
  display: flex;
  justify-content: center;
  gap: 48px;
  margin-bottom: 24px;
  padding: 20px;
  background: var(--zs-bg-card);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius-xl);
}
.zs-gauge-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  transition: transform var(--zs-transition);
  min-width: 100px;
}
.zs-gauge-item:hover {
  transform: scale(1.05);
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

/* ---- Stats row ---- */
.zs-stats-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 12px;
  margin-bottom: 20px;
}

/* ---- Content grid ---- */
.zs-dashboard-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
@media (max-width: 960px) {
  .zs-dashboard-grid {
    grid-template-columns: 1fr;
  }
  .zs-gauges-row {
    gap: 24px;
    flex-wrap: wrap;
  }
}
.zs-dashboard-col {
  display: flex;
  flex-direction: column;
}

/* ---- Network list ---- */
.network-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.network-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 0;
  border-bottom: 1px solid var(--zs-border);
}
.network-row:last-child {
  border-bottom: none;
}
.network-iface {
  display: flex;
  align-items: center;
  gap: 8px;
}
.network-speed {
  font-size: 12px;
  color: var(--zs-text-tertiary);
}
.network-rates {
  display: flex;
  gap: 12px;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}
.rate-down { color: #34c759; }
.rate-up { color: #4a90d9; }

/* Make NCards match zspace */
:deep(.zs-dashboard-card) {
  border-radius: var(--zs-radius-lg) !important;
  border-color: var(--zs-border) !important;
}
</style>
