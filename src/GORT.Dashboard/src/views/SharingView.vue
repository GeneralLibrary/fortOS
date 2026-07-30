<!--
  GORT Dashboard — Sharing Management View
  Manage SMB/NFS/FTP shares: create, delete, and view.
-->
<script setup lang="ts">
import { onMounted, ref, reactive, h } from 'vue'
import { useSharesStore } from '@/stores/shares'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import type { ShareDefinition } from '@/types'
import type { DataTableColumns, FormInst, FormRules } from 'naive-ui'
import { useMessage, NTag, NButton } from 'naive-ui'

const store = useSharesStore()
const message = useMessage()
const { t } = useI18n()

const showCreate = ref(false)
const formRef = ref<FormInst | null>(null)
const submitting = ref(false)

/** Protocols available for selection. */
const protocolOptions = [
  { label: 'SMB', value: 'smb' },
  { label: 'NFS', value: 'nfs' },
  { label: 'FTP', value: 'ftp' },
]

/** Create/Edit form model. */
const formModel = reactive<ShareDefinition>({
  shareId: '',
  name: '',
  path: '',
  protocols: [],
  readOnly: false,
  description: '',
})

const rules: FormRules = {
  name: [{ required: true, message: () => t('sharing.nameRequired') }],
  path: [{ required: true, message: () => t('sharing.pathRequired') }],
  protocols: [{ required: true, type: 'array', min: 1, message: () => t('sharing.protocolRequired') }],
}

function openCreate() {
  formModel.shareId = ''
  formModel.name = ''
  formModel.path = ''
  formModel.protocols = []
  formModel.readOnly = false
  formModel.description = ''
  showCreate.value = true
}

async function handleCreate() {
  try { await formRef.value?.validate() } catch { return }
  submitting.value = true
  try {
    formModel.shareId = `share-${Date.now()}`
    await store.addShare({ ...formModel })
    showCreate.value = false
    message.success(t('sharing.createSuccess'))
  } catch {
    message.error(store.error ?? t('sharing.createFailed'))
  } finally {
    submitting.value = false
  }
}

function confirmDelete(share: ShareDefinition) {
  const d = useDialog()
  d.warning({
    title: t('common.confirm'),
    content: t('sharing.deleteConfirm', { name: share.name }),
    positiveText: t('common.delete'),
    negativeText: t('common.cancel'),
    onPositiveClick: async () => {
      try {
        await store.removeShare(share.shareId)
        message.success(t('sharing.deleteSuccess'))
      } catch {
        message.error(store.error ?? t('sharing.deleteFailed'))
      }
    },
  })
}

onMounted(() => store.fetchShares())

const columns: DataTableColumns<ShareDefinition> = [
  { title: () => t('common.name'), key: 'name', ellipsis: { tooltip: true }, width: 140 },
  { title: () => t('sharing.sharePath'), key: 'path', ellipsis: { tooltip: true } },
  {
    title: () => t('sharing.protocols'), key: 'protocols', width: 160,
    render: (r) => r.protocols.map(p =>
      h(NTag, { size: 'small', style: { marginRight: '4px' } }, { default: () => p.toUpperCase() }),
    ),
  },
  {
    title: () => t('sharing.readOnly'), key: 'readOnly', width: 70,
    render: (r) => h(NTag, { type: r.readOnly ? 'warning' : 'success', size: 'small' }, { default: () => r.readOnly ? t('common.yes') : t('common.no') }),
  },
  { title: () => t('sharing.description'), key: 'description', ellipsis: { tooltip: true } },
  {
    title: () => t('common.actions'), key: 'actions', width: 80,
    render: (r) => h(NButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => confirmDelete(r) }, { default: () => t('common.delete') }),
  },
]
</script>

<template>
  <div class="sharing-page">
    <PageHeader :title="t('sharing.title')" :subtitle="t('sharing.subtitle')">
      <template #actions>
        <NButton type="primary" size="small" @click="openCreate">{{ t('sharing.createShare') }}</NButton>
        <NButton size="small" :loading="store.loading" @click="store.fetchShares()">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <NCard :title="t('sharing.shareList')" :bordered="false" size="small">
      <NDataTable
        v-if="store.shares.length"
        :columns="columns"
        :data="store.shares"
        :bordered="false"
        size="small"
        striped
        :loading="store.loading"
      />
      <EmptyState v-else :message="t('sharing.noShares')" :description="t('sharing.noSharesHint')" />
    </NCard>

    <!-- Create dialog -->
    <NModal v-model:show="showCreate" preset="card" :title="t('sharing.createShare')" style="width: 560px" :mask-closable="false">
      <NForm ref="formRef" :model="formModel" :rules="rules" label-placement="left" label-width="80">
        <NFormItem path="name" :label="t('sharing.shareName')">
          <NInput v-model:value="formModel.name" :placeholder="t('sharing.namePlaceholder')" />
        </NFormItem>
        <NFormItem path="path" :label="t('sharing.sharePath')">
          <NInput v-model:value="formModel.path" :placeholder="t('sharing.pathPlaceholder')" />
        </NFormItem>
        <NFormItem path="protocols" :label="t('sharing.protocols')">
          <NSelect v-model:value="formModel.protocols" :options="protocolOptions" multiple :placeholder="t('sharing.selectProtocols')" />
        </NFormItem>
        <NFormItem path="readOnly" :label="t('sharing.readOnly')">
          <NSwitch v-model:value="formModel.readOnly" />
        </NFormItem>
        <NFormItem path="description" :label="t('sharing.description')">
          <NInput v-model:value="formModel.description" :placeholder="t('sharing.descPlaceholder')" type="textarea" :autosize="{ minRows: 2 }" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showCreate = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="submitting" @click="handleCreate" style="margin-left: 12px">{{ t('common.create') }}</NButton>
      </template>
    </NModal>
  </div>
</template>

<script lang="ts">
import { useDialog } from 'naive-ui'
</script>

<style scoped>
.sharing-page {
  max-width: 1200px;
  margin: 0 auto;
}
</style>
