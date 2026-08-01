<!--
  FortOS Dashboard — Logs & Audit View
  Query system logs with filters by category, level, time range,
  and service/agent. View audit chain verification status.
-->
<script setup lang="ts">
import { ref, reactive, onMounted, h } from 'vue'
import { NTag } from 'naive-ui'
import { queryLogs } from '@/api/logs'
import { useI18n } from 'vue-i18n'
import type { LogEntry, LogCategory } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { formatDateTime } from '@/utils/format'

const { t } = useI18n()

const loading = ref(false)
const logs = ref<LogEntry[]>([])
const auditValid = ref<boolean | null>(null)

/** Query filter form. */
const filter = reactive({
  category: undefined as LogCategory | undefined,
  minLevel: undefined as string | undefined,
  searchText: '',
  limit: 200,
  offset: 0,
})

// ---- Pagination ----
const page = ref(1)
const pageSize = 50
const hasMore = ref(true) // true while results fill the page (API doesn't return total)

async function fetchLogs() {
  loading.value = true
  try {
    const result = await queryLogs({
      category: filter.category,
      minLevel: filter.minLevel,
      searchText: filter.searchText || undefined,
      limit: filter.limit,
      offset: filter.offset,
    })
    logs.value = result
    hasMore.value = result.length >= pageSize
  } catch {
    logs.value = []
    hasMore.value = false
  } finally {
    loading.value = false
  }
}

/** Navigate to a new page. */
function onPageChange(p: number) {
  page.value = p
  filter.offset = (p - 1) * pageSize
  fetchLogs()
}

async function checkAudit() {
  try {
    const resp = await fetch('/api/audit/verify')
    if (resp.ok) {
      const data = await resp.json()
      auditValid.value = data.isValid
    } else {
      // Non-200 response (4xx, 5xx) — treat as verification failure.
      auditValid.value = false
    }
  } catch {
    // Network error — also treat as verification failure.
    auditValid.value = false
  }
}

onMounted(() => {
  fetchLogs()
  checkAudit()
})

const levelOptions = [
  { label: t('common.all'), value: undefined },
  { label: 'Trace', value: 'Trace' },
  { label: 'Debug', value: 'Debug' },
  { label: 'Information', value: 'Information' },
  { label: 'Warning', value: 'Warning' },
  { label: 'Error', value: 'Error' },
  { label: 'Critical', value: 'Critical' },
]

/** Compact abbreviations for log levels so the column never overflows. */
const LEVEL_ABBR: Record<string, string> = {
  Trace: 'TRC',
  Verbose: 'VRB',
  Debug: 'DBG',
  Information: 'INFO',
  Warning: 'WARN',
  Error: 'ERR',
  Critical: 'CRIT',
  Fatal: 'FATL',
  None: '—',
}

function levelAbbr(level: string): string {
  return LEVEL_ABBR[level] ?? level.slice(0, 4).toUpperCase()
}

const categoryOptions = [
  { label: t('common.all'), value: undefined },
  { label: t('logs.categorySystem'), value: 'System' },
  { label: t('logs.categoryAudit'), value: 'Audit' },
  { label: t('logs.categoryAccess'), value: 'Access' },
  { label: t('logs.categoryAgent'), value: 'Agent' },
  { label: t('logs.categoryTrace'), value: 'Trace' },
  { label: t('logs.categoryMetric'), value: 'Metric' },
]

const logColumns: DataTableColumns<LogEntry> = [
  { title: () => t('logs.time'), key: 'timestamp', width: 170, render: (r) => formatDateTime(r.timestamp) },
  {
    title: () => t('logs.level'), key: 'level', width: 70,
    render: (r) => h(NTag, {
      type: r.level === 'Error' || r.level === 'Critical' || r.level === 'Fatal' ? 'error' : r.level === 'Warning' ? 'warning' : r.level === 'Information' ? 'info' : 'default',
      size: 'tiny',
      title: r.level,
    }, { default: () => levelAbbr(r.level) }),
  },
  { title: () => t('logs.category'), key: 'category', width: 70 },
  { title: () => t('logs.source'), key: 'sourceComponent', width: 140, ellipsis: { tooltip: true } },
  { title: () => t('logs.message'), key: 'message', ellipsis: { tooltip: true } },
  { title: () => t('logs.user'), key: 'userId', width: 80, render: (r) => r.userId ?? t('common.unknown') },
  { title: () => t('logs.agent'), key: 'agentId', width: 100, render: (r) => r.agentId ?? t('common.unknown') },
  { title: () => t('logs.traceId'), key: 'traceId', width: 100, ellipsis: { tooltip: true }, render: (r) => r.traceId?.slice(0, 12) ?? t('common.unknown') },
]
</script>

<template>
  <div class="logs-page">
    <PageHeader :title="t('logs.title')" :subtitle="t('logs.subtitle')">
      <template #actions>
        <NButton size="small" :loading="loading" @click="fetchLogs">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <!-- Audit status -->
    <NCard size="small" :bordered="false" style="margin-bottom: 16px">
      <NSpace align="center">
        <span style="color: var(--zs-text-secondary); font-size: 13px">{{ t('logs.auditStatus') }}</span>
        <NTag v-if="auditValid === true" type="success">{{ t('logs.auditValid') }}</NTag>
        <NTag v-else-if="auditValid === false" type="error">{{ t('logs.auditInvalid') }}</NTag>
        <NSpin v-else size="small" />
        <NButton size="tiny" @click="checkAudit">{{ t('logs.auditVerify') }}</NButton>
      </NSpace>
    </NCard>

    <!-- Filters -->
    <NCard size="small" :bordered="false" style="margin-bottom: 16px">
      <NSpace align="end">
        <NFormItem :label="t('logs.category')" label-placement="left" size="small">
          <NSelect v-model:value="filter.category" :options="categoryOptions" style="width: 120px" clearable />
        </NFormItem>
        <NFormItem :label="t('logs.level')" label-placement="left" size="small">
          <NSelect v-model:value="filter.minLevel" :options="levelOptions" style="width: 130px" clearable />
        </NFormItem>
        <NFormItem :label="t('logs.search')" label-placement="left" size="small">
          <NInput v-model:value="filter.searchText" :placeholder="t('logs.searchPlaceholder')" style="width: 200px" clearable @keyup.enter="fetchLogs" />
        </NFormItem>
        <NButton type="primary" size="small" @click="() => { page = 1; filter.offset = 0; fetchLogs(); }">{{ t('common.search') }}</NButton>
      </NSpace>
    </NCard>

    <!-- Log table -->
    <NCard :title="t('logs.logList')" :bordered="false" size="small">
      <NDataTable
        v-if="logs.length"
        :columns="logColumns" :data="logs"
        :bordered="false" size="small" striped
        :loading="loading" :max-height="600"
        :row-class-name="(row: LogEntry) => row.level === 'Error' || row.level === 'Critical' ? 'log-row-error' : ''"
      />
      <div v-if="logs.length" style="display: flex; justify-content: center; margin-top: 12px">
        <NPagination
          :page="page"
          :page-size="pageSize"
          :item-count="hasMore ? undefined : (page - 1) * pageSize + logs.length"
          :page-slot="7"
          :simple="hasMore"
          @update:page="onPageChange"
        />
      </div>
      <EmptyState v-else :message="t('logs.noLogs')" :description="t('logs.noLogsHint')" />
    </NCard>
  </div>
</template>

<style scoped>
.logs-page { max-width: 1400px; margin: 0 auto; }
:deep(.log-row-error) { background: rgba(248, 113, 113, 0.06) !important; }
</style>
