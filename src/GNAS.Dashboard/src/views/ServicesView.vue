<!--
  GNAS Dashboard — Services Management View
  View and control all system services (native, systemd, module, container).
-->
<script setup lang="ts">
import { onMounted, h } from 'vue'
import { useServicesStore } from '@/stores/services'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { formatBytes, formatUptime, serviceStatusType } from '@/utils/format'
import type { ServiceStatusInfo } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { useMessage, NTag, NButton } from 'naive-ui'

const store = useServicesStore()
const message = useMessage()
const { t } = useI18n()

onMounted(() => store.fetchServices())

async function handleStart(id: string) {
  try { await store.start(id); message.success(t('services.startSuccess')) }
  catch { message.error(store.error ?? t('services.startFailed')) }
}

async function handleStop(id: string) {
  try { await store.stop(id); message.success(t('services.stopSuccess')) }
  catch { message.error(store.error ?? t('services.stopFailed')) }
}

const columns: DataTableColumns<ServiceStatusInfo> = [
  { title: () => t('services.serviceId'), key: 'serviceId', ellipsis: { tooltip: true }, width: 160 },
  {
    title: () => t('common.status'), key: 'status', width: 90,
    render: (r) => h(NTag, { type: serviceStatusType(r.status), size: 'small' }, { default: () => r.status }),
  },
  { title: () => t('common.type'), key: 'type', width: 90 },
  { title: () => t('services.pid'), key: 'pid', width: 70, render: (r) => r.pid?.toString() ?? t('common.unknown') },
  { title: () => t('services.cpu'), key: 'cpuPercent', width: 80, render: (r) => `${r.cpuPercent.toFixed(1)}%` },
  { title: () => t('services.memory'), key: 'memoryBytes', width: 100, render: (r) => formatBytes(r.memoryBytes) },
  { title: () => t('services.uptime'), key: 'uptime', width: 110, render: (r) => formatUptime(r.uptime) },
  { title: () => t('services.lastError'), key: 'lastError', width: 100, ellipsis: { tooltip: true }, render: (r) => r.lastError ?? t('common.unknown') },
  {
    title: () => t('common.actions'), key: 'actions', width: 120,
    render: (r) => h('div', { style: { display: 'flex', gap: '4px' } }, [
      h(NButton, { size: 'tiny', type: 'success', secondary: true, onClick: () => handleStart(r.serviceId) }, { default: () => t('agents.start') }),
      h(NButton, { size: 'tiny', type: 'warning', secondary: true, onClick: () => handleStop(r.serviceId) }, { default: () => t('agents.stop') }),
    ]),
  },
]
</script>

<template>
  <div class="services-page">
    <PageHeader :title="t('services.title')" :subtitle="t('services.subtitle')">
      <template #actions>
        <NButton size="small" :loading="store.loading" @click="store.fetchServices()">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <NCard :title="t('services.serviceList')" :bordered="false" size="small">
      <NDataTable
        v-if="store.services.length"
        :columns="columns" :data="store.services"
        :bordered="false" size="small" striped :loading="store.loading"
      />
      <EmptyState v-else :message="t('services.noServices')" />
    </NCard>
  </div>
</template>

<style scoped>
.services-page { max-width: 1200px; margin: 0 auto; }
</style>
