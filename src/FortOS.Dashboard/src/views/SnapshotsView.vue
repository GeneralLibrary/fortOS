<!--
  FortOS Dashboard — Snapshots Management View
  Create and restore btrfs/LVM snapshots for data protection.
-->
<script setup lang="ts">
import { ref, reactive, h, computed } from 'vue'
import { createSnapshot, listSnapshots, restoreSnapshot } from '@/api/snapshots'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import type { CommandResult } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { useMessage, useDialog, NButton } from 'naive-ui'

const message = useMessage()
const dialog = useDialog()
const { t } = useI18n()

const target = ref('')
const loading = ref(false)
const snapshotList = ref<string>('')
const creating = ref(false)
const showCreate = ref(false)

const createForm = reactive({
  target: '',
  name: '',
})

async function fetchSnapshots() {
  if (!target.value.trim()) return
  loading.value = true
  try {
    const result = await listSnapshots(target.value.trim())
    snapshotList.value = result.stdout || result.stderr || JSON.stringify(result)
  } catch {
    message.error(t('snapshots.fetchFailed'))
  } finally {
    loading.value = false
  }
}

async function handleCreate() {
  if (!createForm.target.trim()) return
  creating.value = true
  try {
    await createSnapshot({ target: createForm.target, name: createForm.name || undefined })
    message.success(t('snapshots.createSuccess'))
    showCreate.value = false
    target.value = createForm.target
    await fetchSnapshots()
  } catch {
    message.error(t('snapshots.createFailed'))
  } finally {
    creating.value = false
  }
}

function confirmRestore(snapshotId: string) {
  dialog.warning({
    title: t('common.confirm'),
    content: t('snapshots.restoreConfirm', { id: snapshotId }),
    positiveText: t('snapshots.restore'), negativeText: t('common.cancel'),
    onPositiveClick: async () => {
      try {
        await restoreSnapshot(snapshotId, target.value)
        message.success(t('snapshots.restoreSuccess'))
      } catch {
        message.error(t('snapshots.restoreFailed'))
      }
    },
  })
}

interface SnapshotRow {
  id: string
  target: string
  details: string
}

/** Derived snapshot rows from raw CLI output. Computed to avoid re-parsing on every render. */
const snapshotRows = computed<SnapshotRow[]>(() => {
  if (!snapshotList.value) return []
  const lines = snapshotList.value.split('\n').filter(l => l.trim())
  return lines.map((line, i) => ({
    id: `snap-${i}`,
    target: target.value,
    details: line,
  }))
})

const columns: DataTableColumns<SnapshotRow> = [
  { title: () => t('snapshots.snapshotInfo'), key: 'details', ellipsis: { tooltip: true } },
  {
    title: () => t('common.actions'), key: 'actions', width: 80,
    render: (r) => h(NButton, { size: 'tiny', type: 'warning', secondary: true, onClick: () => confirmRestore(r.id) }, { default: () => t('snapshots.restore') }),
  },
]
</script>

<template>
  <div class="snapshots-page">
    <PageHeader :title="t('snapshots.title')" :subtitle="t('snapshots.subtitle')" />

    <!-- Query section -->
    <NCard :title="t('snapshots.viewSnapshots')" :bordered="false" size="small" style="margin-bottom: 16px">
      <NSpace>
        <NInput v-model:value="target" :placeholder="t('snapshots.targetPlaceholder')" style="width: 400px" @keyup.enter="fetchSnapshots" />
        <NButton type="primary" :loading="loading" @click="fetchSnapshots">{{ t('snapshots.query') }}</NButton>
        <NButton @click="showCreate = true">{{ t('snapshots.createSnapshot') }}</NButton>
      </NSpace>
    </NCard>

    <!-- Snapshot results -->
    <NCard :title="t('snapshots.snapshotList')" :bordered="false" size="small">
      <NDataTable
        v-if="snapshotList"
        :columns="columns"
        :data="snapshotRows"
        :bordered="false"
        size="small"
        striped
        :loading="loading"
      />
      <EmptyState v-else :message="t('snapshots.noSnapshots')" :description="t('snapshots.noSnapshotsHint')" />
    </NCard>

    <!-- Create modal -->
    <NModal v-model:show="showCreate" preset="card" :title="t('snapshots.createSnapshotTitle')" style="width: 480px">
      <NForm label-placement="left" label-width="80">
        <NFormItem :label="t('snapshots.targetPath')" required>
          <NInput v-model:value="createForm.target" :placeholder="t('snapshots.targetRequired')" />
        </NFormItem>
        <NFormItem :label="t('snapshots.snapshotName')">
          <NInput v-model:value="createForm.name" :placeholder="t('snapshots.snapshotNameHint')" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showCreate = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="creating" @click="handleCreate" style="margin-left: 12px">{{ t('common.create') }}</NButton>
      </template>
    </NModal>
  </div>
</template>

<style scoped>
.snapshots-page { max-width: 1000px; margin: 0 auto; }
</style>
