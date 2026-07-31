<!--
  FortOS Dashboard — Storage Management View
  Displays all physical disks with SMART health, temperature,
  and usage statistics. Supports SMART check execution.
-->
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useDisksStore } from '@/stores/disks'
import { runSmartCheck as apiRunSmartCheck } from '@/api/disks'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { formatBytes, formatTemperature, formatPercent } from '@/utils/format'
import type { DiskInfo, SmartData } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { h } from 'vue'
import { NButton } from 'naive-ui'

const store = useDisksStore()
const { t } = useI18n()

/** Selected disk for detail drawer. */
const selectedDisk = ref<DiskInfo | null>(null)
const smartResult = ref<SmartData | null>(null)
const smartLoading = ref(false)
const showDetail = ref(false)

onMounted(() => store.fetchDisks())

/** Open disk detail and run SMART check. */
async function viewDetail(disk: DiskInfo) {
  selectedDisk.value = disk
  showDetail.value = true
  smartLoading.value = true
  try {
    smartResult.value = await apiRunSmartCheck(disk.path)
  } catch {
    smartResult.value = null
  } finally {
    smartLoading.value = false
  }
}

const columns: DataTableColumns<DiskInfo> = [
  { title: () => t('storage.devicePath'), key: 'path', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('storage.model'), key: 'model', ellipsis: { tooltip: true } },
  { title: () => t('storage.serial'), key: 'serial', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('storage.capacity'), key: 'sizeBytes', width: 100, render: (r) => formatBytes(r.sizeBytes) },
  { title: () => t('storage.interface'), key: 'interfaceType', width: 70 },
  { title: () => t('storage.diskType'), key: 'isSsd', width: 60, render: (r) => r.isSsd ? 'SSD' : 'HDD' },
  { title: () => t('storage.smartStatus'), key: 'smartStatus', width: 90,
    render: (r) => h('span', {
      style: { color: r.smartStatus?.toLowerCase().includes('ok') || r.smartStatus?.toLowerCase().includes('pass') ? '#4ade80' : '#f87171' }
    }, r.smartStatus ?? t('common.unknown')),
  },
  { title: () => t('storage.temperature'), key: 'temperatureCelsius', width: 70, render: (r) => formatTemperature(r.temperatureCelsius) },
  { title: () => t('storage.usage'), key: 'usedPercent', width: 80, render: (r) => formatPercent(r.usedPercent) },
  {
    title: () => t('common.actions'), key: 'actions', width: 100,
    render: (r) => h(NButton, { size: 'tiny', onClick: () => viewDetail(r) }, { default: () => t('common.detail') }),
  },
]
</script>

<template>
  <div class="storage-page">
    <PageHeader
      :title="t('storage.title')"
      :subtitle="t('storage.subtitle')"
    >
      <template #actions>
        <NButton size="small" :loading="store.loading" @click="store.fetchDisks()">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <!-- Disk table -->
    <NCard :title="t('storage.diskList')" :bordered="false" size="small">
      <NDataTable
        v-if="store.disks.length"
        :columns="columns"
        :data="store.disks"
        :bordered="false"
        size="small"
        striped
        :loading="store.loading"
      />
      <EmptyState v-else :message="t('storage.noDisks')" />
    </NCard>

    <!-- Disk detail drawer -->
    <NDrawer v-model:show="showDetail" :width="500" placement="right">
      <NDrawerContent v-if="selectedDisk" :title="t('storage.diskDetail')" closable>
        <template #header>
          {{ selectedDisk.model }}
        </template>

        <NDescriptions label-placement="left" :column="1" bordered size="small">
          <NDescriptionsItem :label="t('storage.devicePath')">{{ selectedDisk.path }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.model')">{{ selectedDisk.model }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.serial')">{{ selectedDisk.serial }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.capacity')">{{ formatBytes(selectedDisk.sizeBytes) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.interface')">{{ selectedDisk.interfaceType }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.diskType')">
            <NTag :type="selectedDisk.isSsd ? 'info' : 'default'" size="small">
              {{ selectedDisk.isSsd ? 'SSD' : 'HDD' }}
            </NTag>
          </NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.smartStatus')">
            <NTag
              :type="selectedDisk.smartStatus?.toLowerCase().includes('ok') || selectedDisk.smartStatus?.toLowerCase().includes('pass') ? 'success' : 'error'"
              size="small"
            >
              {{ selectedDisk.smartStatus }}
            </NTag>
          </NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.temperature')">{{ formatTemperature(selectedDisk.temperatureCelsius) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('storage.usage')">{{ formatPercent(selectedDisk.usedPercent) }}</NDescriptionsItem>
        </NDescriptions>

        <!-- SMART detail -->
        <NDivider />
        <h4 style="margin: 0 0 12px">{{ t('storage.smartDetail') }}</h4>
        <NSpin v-if="smartLoading" />
        <div v-else-if="smartResult">
          <NDescriptions label-placement="left" :column="1" bordered size="small">
            <NDescriptionsItem :label="t('storage.healthStatus')">{{ smartResult.health }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('storage.temperature')">
              {{ formatTemperature(smartResult.temperatureCelsius) }}
            </NDescriptionsItem>
          </NDescriptions>
          <NCode
            v-if="smartResult.rawJson"
            :code="smartResult.rawJson"
            language="json"
            style="margin-top: 12px; max-height: 400px"
          />
        </div>
        <NAlert v-else type="warning" style="margin-top: 8px">
          {{ t('storage.smartFailed') }}
        </NAlert>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.storage-page {
  max-width: 1200px;
  margin: 0 auto;
}
</style>
