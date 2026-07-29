<!--
  GNAS Dashboard — Alerts Center View
  Displays active alerts and alert rules configuration.
-->
<script setup lang="ts">
import { onMounted, h } from 'vue'
import { useAlertsStore } from '@/stores/alerts'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { formatDateTime, severityColor } from '@/utils/format'
import type { ActiveAlert, AlertRule } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { NTag } from 'naive-ui'

const store = useAlertsStore()
const { t } = useI18n()

onMounted(() => {
  store.fetchAlerts()
  store.fetchRules()
})

const alertColumns: DataTableColumns<ActiveAlert> = [
  {
    title: () => t('alerts.severity'), key: 'severity', width: 80,
    render: (r) => h(NTag, { type: severityColor(r.severity), size: 'small' }, { default: () => r.severity }),
  },
  { title: () => t('alerts.message'), key: 'message', ellipsis: { tooltip: true } },
  { title: () => t('alerts.triggeredAt'), key: 'triggeredAt', width: 170, render: (r) => formatDateTime(r.triggeredAt) },
  { title: () => t('alerts.dimensions'), key: 'dimensions', width: 180, ellipsis: { tooltip: true },
    render: (r) => Object.entries(r.dimensions).map(([k, v]) => `${k}=${v}`).join(', ') || t('common.unknown'),
  },
]

const ruleColumns: DataTableColumns<AlertRule> = [
  {
    title: () => t('alerts.severity'), key: 'severity', width: 80,
    render: (r) => h(NTag, { type: severityColor(r.severity), size: 'small' }, { default: () => r.severity }),
  },
  { title: () => t('alerts.ruleName'), key: 'name', ellipsis: { tooltip: true }, width: 160 },
  { title: () => t('alerts.description'), key: 'description', ellipsis: { tooltip: true } },
  { title: () => t('alerts.conditionType'), key: 'condition', width: 100, render: (r) => r.condition.type },
  { title: () => t('common.actions'), key: 'actions', width: 120, render: (r) => r.actions.join(', ') || t('common.unknown') },
  { title: () => t('alerts.coolDown'), key: 'cooldownSeconds', width: 80 },
]
</script>

<template>
  <div class="alerts-page">
    <PageHeader :title="t('alerts.title')" :subtitle="t('alerts.subtitle')">
      <template #actions>
        <NButton size="small" :loading="store.loading" @click="store.fetchAlerts(); store.fetchRules()">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <!-- Active alerts -->
    <NCard :title="t('alerts.activeAlerts')" :bordered="false" size="small" style="margin-bottom: 16px">
      <template #header-extra>
        <NTag :type="store.alerts.length > 0 ? 'error' : 'success'" size="small">
          {{ store.alerts.length }}{{ t('alerts.countSuffix') }}
        </NTag>
      </template>
      <NDataTable
        v-if="store.alerts.length"
        :columns="alertColumns" :data="store.alerts"
        :bordered="false" size="small" striped
      />
      <EmptyState v-else :message="t('alerts.noAlerts')" :description="t('alerts.noAlertsHint')" />
    </NCard>

    <!-- Alert rules -->
    <NCard :title="t('alerts.alertRules')" :bordered="false" size="small">
      <NDataTable
        v-if="store.rules.length"
        :columns="ruleColumns" :data="store.rules"
        :bordered="false" size="small" striped
      />
      <EmptyState v-else :message="t('alerts.noRules')" />
    </NCard>
  </div>
</template>

<style scoped>
.alerts-page { max-width: 1200px; margin: 0 auto; }
</style>
