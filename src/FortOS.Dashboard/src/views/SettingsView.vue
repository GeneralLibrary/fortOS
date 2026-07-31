<!--
  FortOS Dashboard — System Settings View
  Displays current configuration and allows editing non-sensitive values.
-->
<script setup lang="ts">
import { onMounted, ref, reactive, h } from 'vue'
import { useSettingsStore } from '@/stores/settings'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { useMessage, NButton } from 'naive-ui'
import type { DataTableColumns, FormInst } from 'naive-ui'

const store = useSettingsStore()
const message = useMessage()
const { t } = useI18n()

const showEdit = ref(false)
const editingKey = ref('')
const editingValue = ref('')
const formRef = ref<FormInst | null>(null)
const saving = ref(false)

onMounted(() => store.fetchConfig())

function openEdit(key: string, value: string) {
  editingKey.value = key
  editingValue.value = value
  showEdit.value = true
}

async function handleSave() {
  saving.value = true
  try {
    await store.setConfig(editingKey.value, editingValue.value)
    message.success(t('settings.updateSuccess'))
    showEdit.value = false
  } catch {
    message.error(store.error ?? t('settings.updateFailed'))
  } finally {
    saving.value = false
  }
}

interface ConfigEntry {
  key: string
  value: string
}

const configEntries = computed<ConfigEntry[]>(() =>
  Object.entries(store.config)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, value]) => ({ key, value })),
)

const columns: DataTableColumns<ConfigEntry> = [
  { title: () => t('settings.key'), key: 'key', ellipsis: { tooltip: true }, width: 300 },
  { title: () => t('settings.value'), key: 'value', ellipsis: { tooltip: true },
    render: (r) => r.value.length > 80 ? r.value.slice(0, 80) + '…' : r.value,
  },
  {
    title: () => t('common.actions'), key: 'actions', width: 80,
    render: (r) => h(NButton, { size: 'tiny', onClick: () => openEdit(r.key, r.value) }, { default: () => t('common.edit') }),
  },
]
</script>

<template>
  <div class="settings-page">
    <PageHeader :title="t('settings.title')" :subtitle="t('settings.subtitle')">
      <template #actions>
        <NButton size="small" :loading="store.loading" @click="store.fetchConfig()">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <NCard :title="t('settings.configList')" :bordered="false" size="small">
      <NDataTable
        v-if="configEntries.length"
        :columns="columns" :data="configEntries"
        :bordered="false" size="small" striped :loading="store.loading"
      />
      <EmptyState v-else :message="t('settings.noConfig')" />
    </NCard>

    <!-- Edit dialog -->
    <NModal v-model:show="showEdit" preset="card" :title="t('settings.editConfig')" style="width: 520px" :mask-closable="false">
      <NForm ref="formRef" label-placement="left" label-width="60">
        <NFormItem :label="t('settings.key')">
          <NInput :value="editingKey" disabled />
        </NFormItem>
        <NFormItem :label="t('settings.value')" required>
          <NInput v-model:value="editingValue" type="textarea" :autosize="{ minRows: 3, maxRows: 8 }" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showEdit = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="saving" @click="handleSave" style="margin-left: 12px">{{ t('common.save') }}</NButton>
      </template>
    </NModal>
  </div>
</template>

<script lang="ts">
import { computed } from 'vue'
</script>

<style scoped>
.settings-page { max-width: 1000px; margin: 0 auto; }
</style>
