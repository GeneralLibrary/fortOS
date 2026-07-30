<!--
  GORT Dashboard — Network Management View
  Displays network interfaces, traffic rates, and basic configuration.
-->
<script setup lang="ts">
import { ref, onMounted, h, computed } from 'vue'
import { getSystemMetrics } from '@/api/metrics'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { formatBytesPerSecond } from '@/utils/format'
import type { NetworkTrafficMetrics, ProtocolSessionMetrics } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { NTag } from 'naive-ui'

const loading = ref(false)
const interfaces = ref<NetworkTrafficMetrics[]>([])
const protocolSessions = ref<ProtocolSessionMetrics[]>([])
const lastUpdated = ref<Date | null>(null)

const { t } = useI18n()

async function fetchNetworkData() {
  loading.value = true
  try {
    const metrics = await getSystemMetrics()
    interfaces.value = [...metrics.networks]
    protocolSessions.value = [...metrics.protocolSessions]
    lastUpdated.value = new Date()
  } catch {
    // silently fail
  } finally {
    loading.value = false
  }
}

onMounted(fetchNetworkData)

const totalRx = computed(() => interfaces.value.reduce((s, n) => s + n.receiveBytesPerSecond, 0))
const totalTx = computed(() => interfaces.value.reduce((s, n) => s + n.transmitBytesPerSecond, 0))

const ifaceColumns: DataTableColumns<NetworkTrafficMetrics> = [
  { title: () => t('network.interface'), key: 'interface', width: 120 },
  {
    title: () => t('common.status'), key: 'isUp', width: 70,
    render: (r) => h(NTag, { type: r.isUp ? 'success' : 'default', size: 'small' }, { default: () => r.isUp ? t('network.up') : t('network.down') }),
  },
  { title: () => t('network.linkSpeed'), key: 'linkSpeedMbps', width: 80, render: (r) => r.linkSpeedMbps ? `${r.linkSpeedMbps} Mbps` : t('common.unknown') },
  { title: () => t('network.receive'), key: 'receiveBytesPerSecond', width: 110, render: (r) => formatBytesPerSecond(r.receiveBytesPerSecond) },
  { title: () => t('network.transmit'), key: 'transmitBytesPerSecond', width: 110, render: (r) => formatBytesPerSecond(r.transmitBytesPerSecond) },
  { title: () => t('network.rxErrors'), key: 'receiveErrors', width: 80 },
  { title: () => t('network.txErrors'), key: 'transmitErrors', width: 80 },
  { title: () => t('network.rxDropped'), key: 'receiveDropped', width: 80 },
  { title: () => t('network.txDropped'), key: 'transmitDropped', width: 80 },
]

const sessionColumns: DataTableColumns<ProtocolSessionMetrics> = [
  { title: () => t('network.protocol'), key: 'protocol', width: 100 },
  { title: () => t('network.activeSessions'), key: 'activeSessions', width: 100 },
]
</script>

<template>
  <div class="network-page">
    <PageHeader
      :title="t('network.title')"
      :subtitle="lastUpdated ? `${t('common.lastUpdated')}: ${lastUpdated.toLocaleTimeString()}` : undefined"
    >
      <template #actions>
        <NButton size="small" :loading="loading" @click="fetchNetworkData">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <!-- Summary -->
    <NGrid :cols="3" :x-gap="12" style="margin-bottom: 16px">
      <NGi>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('network.totalRx')" :value="formatBytesPerSecond(totalRx)" />
        </NCard>
      </NGi>
      <NGi>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('network.totalTx')" :value="formatBytesPerSecond(totalTx)" />
        </NCard>
      </NGi>
      <NGi>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('network.interfaceCount')" :value="`${interfaces.length}`" />
        </NCard>
      </NGi>
    </NGrid>

    <!-- Interfaces -->
    <NCard :title="t('network.interfaces')" :bordered="false" size="small" style="margin-bottom: 16px">
      <NDataTable
        v-if="interfaces.length"
        :columns="ifaceColumns" :data="interfaces"
        :bordered="false" size="small" striped :loading="loading"
      />
      <EmptyState v-else :message="t('network.noInterfaces')" />
    </NCard>

    <!-- Protocol sessions -->
    <NCard :title="t('network.protocolSessions')" :bordered="false" size="small">
      <NDataTable
        v-if="protocolSessions.length"
        :columns="sessionColumns" :data="protocolSessions"
        :bordered="false" size="small" striped
      />
      <EmptyState v-else :message="t('network.noSessions')" />
    </NCard>
  </div>
</template>

<style scoped>
.network-page { max-width: 1200px; margin: 0 auto; }
</style>
