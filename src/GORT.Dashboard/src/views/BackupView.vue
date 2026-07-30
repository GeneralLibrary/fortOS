<!--
  GORT Dashboard — Backup Management View
  Manage backup tasks (create, edit, delete, run manually),
  view run history, and restore from backup.
-->
<script setup lang="ts">
import { onMounted, ref, reactive, h, computed } from 'vue'
import { useBackupStore } from '@/stores/backup'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { formatBytes, formatDateTime, formatSchedule, backupRunStateLabel, backupRunStateType } from '@/utils/format'
import type { BackupTask, BackupRunRecord, BackupTargetType, BackupMethod } from '@/types'
import type { DataTableColumns, FormInst, FormRules } from 'naive-ui'
import { useMessage, NTag, NButton } from 'naive-ui'

const store = useBackupStore()
const message = useMessage()
const { t, locale } = useI18n()

/** Locale-aware format helpers that pass the current language. */
function fmtSchedule(cron: string) { return formatSchedule(cron, locale.value as 'zh-CN' | 'en-US') }
function fmtRunState(state: string) { return backupRunStateLabel(state, locale.value as 'zh-CN' | 'en-US') }

// ---- Create/Edit dialog state ----
const showEditor = ref(false)
const editingTask = ref<BackupTask | null>(null)
const formRef = ref<FormInst | null>(null)
const submitting = ref(false)

const formModel = reactive<BackupTask>({
  taskId: '',
  name: '',
  sourcePath: '',
  target: { type: 'Local' as BackupTargetType, connectionString: '', bucketOrPath: '' },
  cronExpression: '',
  enabled: true,
  method: 'Incremental' as BackupMethod,
  retentionDays: 30,
  retentionCount: 10,
  compression: true,
  encryption: true,
  excludePatterns: [],
  retryCount: 2,
  retryBackoffSeconds: 5,
  freshnessSlaHours: 24,
})

const rules: FormRules = {
  name: [{ required: true, message: () => t('backup.nameRequired') }],
  sourcePath: [{ required: true, message: () => t('backup.sourceRequired') }],
  'target.bucketOrPath': [{ required: true, message: () => t('backup.targetRequired') }],
  cronExpression: [{ required: true, message: () => t('backup.scheduleRequired') }],
}

const targetTypeOptions = [
  { label: t('backup.targetLocal'), value: 'Local' },
  { label: t('backup.targetRemoteNas'), value: 'RemoteNas' },
  { label: t('backup.targetS3'), value: 'S3' },
  { label: t('backup.targetB2'), value: 'B2' },
  { label: t('backup.targetWebDAV'), value: 'WebDAV' },
  { label: t('backup.targetSFTP'), value: 'SFTP' },
]

const methodOptions = [
  { label: t('backup.methodIncremental'), value: 'Incremental' },
  { label: t('backup.methodFull'), value: 'Full' },
  { label: t('backup.methodMirror'), value: 'Mirror' },
]

// ---- Run history state ----
const showRuns = ref(false)
const runsTaskId = ref<string | null>(null)

// ---- Run history pagination ----
const runsPage = ref(0)
const runsPageSize = 50

// ---- Restore state ----
const showRestore = ref(false)
const restoreTaskId = ref('')
const restoreForm = reactive({ sourceOverride: '', targetOverride: '', dryRun: false })
const restoring = ref(false)

onMounted(() => store.fetchTasks())

function openCreate() {
  editingTask.value = null
  resetForm()
  showEditor.value = true
}

function openEdit(task: BackupTask) {
  editingTask.value = task
  Object.assign(formModel, JSON.parse(JSON.stringify(task)))
  showEditor.value = true
}

function resetForm() {
  Object.assign(formModel, {
    taskId: '', name: '', sourcePath: '',
    target: { type: 'Local' as BackupTargetType, connectionString: '', bucketOrPath: '' },
    cronExpression: '', enabled: true, method: 'Incremental' as BackupMethod,
    retentionDays: 30, retentionCount: 10, compression: true, encryption: true,
    excludePatterns: [], retryCount: 2, retryBackoffSeconds: 5, freshnessSlaHours: 24,
  })
}

async function handleSave() {
  try { await formRef.value?.validate() } catch { return }
  submitting.value = true
  try {
    const taskId = editingTask.value?.taskId ?? `backup-${Date.now()}`
    await store.saveTask(taskId, { ...formModel } as BackupTask)
    showEditor.value = false
    message.success(t('backup.saveSuccess'))
  } catch {
    message.error(store.error ?? t('backup.saveFailed'))
  } finally {
    submitting.value = false
  }
}

function confirmDelete(task: BackupTask) {
  useDialog().warning({
    title: t('common.confirm'),
    content: t('backup.deleteConfirm', { name: task.name }),
    positiveText: t('common.delete'), negativeText: t('common.cancel'),
    onPositiveClick: async () => {
      try { await store.removeTask(task.taskId); message.success(t('backup.deleteSuccess')) }
      catch { message.error(store.error ?? t('backup.deleteFailed')) }
    },
  })
}

async function handleRun(task: BackupTask) {
  try {
    await store.runTask(task.taskId)
    message.success(t('backup.runStarted', { name: task.name }))
  } catch {
    message.error(store.error ?? t('backup.runFailed'))
  }
}

async function viewRuns(taskId: string) {
  runsTaskId.value = taskId
  runsPage.value = 0
  showRuns.value = true
  await store.fetchRuns(taskId, runsPage.value * runsPageSize, runsPageSize)
}

/** Switch run history page. */
async function onRunsPageChange(page: number) {
  runsPage.value = page - 1
  if (runsTaskId.value) {
    await store.fetchRuns(runsTaskId.value, runsPage.value * runsPageSize, runsPageSize)
  }
}

/** Open the restore dialog for a given task. */
function openRestore(task: BackupTask) {
  restoreTaskId.value = task.taskId
  restoreForm.sourceOverride = ''
  restoreForm.targetOverride = ''
  restoreForm.dryRun = false
  showRestore.value = true
}

/** Execute restore. */
async function handleRestore() {
  restoring.value = true
  try {
    await store.restore(
      restoreTaskId.value,
      restoreForm.sourceOverride || undefined,
      restoreForm.targetOverride || undefined,
      restoreForm.dryRun,
    )
    if (restoreForm.dryRun) {
      message.success(t('backup.restoreDryRunSuccess'))
    } else {
      message.success(t('backup.restoreStarted'))
    }
    showRestore.value = false
  } catch {
    message.error(store.error ?? t('backup.restoreFailed'))
  } finally {
    restoring.value = false
  }
}

const taskColumns: DataTableColumns<BackupTask> = [
  { title: () => t('common.name'), key: 'name', ellipsis: { tooltip: true }, width: 130 },
  { title: () => t('backup.sourcePath'), key: 'sourcePath', ellipsis: { tooltip: true }, width: 180 },
  { title: () => t('backup.targetType'), key: 'target', width: 120, render: (r) => `${r.target.type}:${r.target.bucketOrPath}` },
  { title: () => t('backup.schedule'), key: 'cronExpression', width: 120, render: (r) => fmtSchedule(r.cronExpression) },
  { title: () => t('backup.method'), key: 'method', width: 80 },
  {
    title: () => t('common.status'), key: 'enabled', width: 70,
    render: (r) => h(NTag, { type: r.enabled ? 'success' : 'default', size: 'small' }, { default: () => r.enabled ? t('common.enabled') : t('common.disabled') }),
  },
  { title: () => t('backup.retentionDays'), key: 'retentionDays', width: 70, render: (r) => `${r.retentionDays}d` },
  {
    title: () => t('common.actions'), key: 'actions', width: 260,
    render: (r) => h('div', { style: { display: 'flex', gap: '4px', flexWrap: 'wrap' } }, [
      h(NButton, { size: 'tiny', onClick: () => handleRun(r) }, { default: () => t('backup.runNow') }),
      h(NButton, { size: 'tiny', onClick: () => viewRuns(r.taskId) }, { default: () => t('backup.history') }),
      h(NButton, { size: 'tiny', type: 'success', secondary: true, onClick: () => openRestore(r) }, { default: () => t('backup.restoreAction') }),
      h(NButton, { size: 'tiny', onClick: () => openEdit(r) }, { default: () => t('common.edit') }),
      h(NButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => confirmDelete(r) }, { default: () => t('common.delete') }),
    ]),
  },
]

const runColumns: DataTableColumns<BackupRunRecord> = [
  { title: () => t('backup.state'), key: 'state', width: 80, render: (r) => h(NTag, { type: backupRunStateType(r.state), size: 'small' }, { default: () => fmtRunState(r.state) }) },
  { title: () => t('backup.operation'), key: 'operation', width: 90 },
  { title: () => t('backup.startTime'), key: 'startedAt', width: 170, render: (r) => formatDateTime(r.startedAt) },
  { title: () => t('backup.endTime'), key: 'finishedAt', width: 170, render: (r) => r.finishedAt ? formatDateTime(r.finishedAt) : t('common.unknown') },
  { title: () => t('backup.exitCode'), key: 'exitCode', width: 70 },
  { title: () => t('backup.stderr'), key: 'stderr', ellipsis: { tooltip: true }, width: 100 },
]
</script>

<template>
  <div class="backup-page">
    <PageHeader :title="t('backup.title')" :subtitle="t('backup.subtitle')">
      <template #actions>
        <NButton type="primary" size="small" @click="openCreate">{{ t('backup.createTask') }}</NButton>
        <NButton size="small" :loading="store.loading" @click="store.fetchTasks()">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <NCard :title="t('backup.taskList')" :bordered="false" size="small">
      <NDataTable
        v-if="store.tasks.length"
        :columns="taskColumns" :data="store.tasks"
        :bordered="false" size="small" striped :loading="store.loading"
      />
      <EmptyState v-else :message="t('backup.noTasks')" :description="t('backup.noTasksHint')" />
    </NCard>

    <!-- Create/Edit modal -->
    <NModal v-model:show="showEditor" preset="card" :title="editingTask ? t('backup.editTask') : t('backup.createTaskTitle')" style="width: 640px" :mask-closable="false">
      <NForm ref="formRef" :model="formModel" :rules="rules" label-placement="left" label-width="100">
        <NFormItem path="name" :label="t('backup.taskName')">
          <NInput v-model:value="formModel.name" :placeholder="t('backup.namePlaceholder')" />
        </NFormItem>
        <NFormItem path="sourcePath" :label="t('backup.sourcePath')">
          <NInput v-model:value="formModel.sourcePath" :placeholder="t('backup.sourcePlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('backup.targetType')">
          <NSelect v-model:value="formModel.target.type" :options="targetTypeOptions" />
        </NFormItem>
        <NFormItem path="target.bucketOrPath" :label="t('backup.targetPath')">
          <NInput v-model:value="formModel.target.bucketOrPath" :placeholder="t('backup.targetPlaceholder')" />
        </NFormItem>
        <NFormItem path="cronExpression" :label="t('backup.schedule')">
          <NInput v-model:value="formModel.cronExpression" :placeholder="t('backup.schedulePlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('backup.method')">
          <NSelect v-model:value="formModel.method" :options="methodOptions" />
        </NFormItem>
        <NGrid :cols="2" :x-gap="12">
          <NFormItem :label="t('backup.retentionDays')">
            <NInputNumber v-model:value="formModel.retentionDays" :min="1" />
          </NFormItem>
          <NFormItem :label="t('backup.retentionCount')">
            <NInputNumber v-model:value="formModel.retentionCount" :min="1" />
          </NFormItem>
        </NGrid>
        <NFormItem :label="t('backup.taskEnabled')">
          <NSwitch v-model:value="formModel.enabled" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showEditor = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="submitting" @click="handleSave" style="margin-left: 12px">{{ t('common.save') }}</NButton>
      </template>
    </NModal>

    <!-- Restore modal -->
    <NModal v-model:show="showRestore" preset="card" :title="t('backup.restoreTitle')" style="width: 520px" :mask-closable="false">
      <NForm label-placement="left" label-width="140">
        <NFormItem :label="t('backup.restoreSourceOverride')">
          <NInput v-model:value="restoreForm.sourceOverride" :placeholder="t('backup.sourcePlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('backup.restoreTargetOverride')">
          <NInput v-model:value="restoreForm.targetOverride" :placeholder="t('backup.targetPlaceholder')" />
        </NFormItem>
        <NFormItem :label="t('backup.restoreDryRun')">
          <NSwitch v-model:value="restoreForm.dryRun" />
          <span style="margin-left: 8px; font-size: 12px; color: var(--zs-text-tertiary)">{{ t('backup.restoreDryRunHint') }}</span>
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showRestore = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="restoring" @click="handleRestore" style="margin-left: 12px">{{ t('backup.restoreAction') }}</NButton>
      </template>
    </NModal>

    <!-- Run history drawer -->
    <NDrawer v-model:show="showRuns" :width="700" placement="right">
      <NDrawerContent :title="t('backup.runHistory')" closable>
        <NDataTable
          v-if="store.runs.length"
          :columns="runColumns" :data="store.runs"
          :bordered="false" size="small" striped
          :pagination="false"
        />
        <NPagination
          v-if="store.runsTotal > runsPageSize"
          :page="runsPage + 1" :page-size="runsPageSize"
          :item-count="store.runsTotal"
          :page-slot="5"
          style="margin-top: 12px; justify-content: center"
          @update:page="onRunsPageChange"
        />
        <EmptyState v-else-if="!store.runs.length" :message="t('backup.noRuns')" />
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<script lang="ts">
import { useDialog } from 'naive-ui'
</script>

<style scoped>
.backup-page { max-width: 1200px; margin: 0 auto; }
</style>
