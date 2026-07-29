<!--
  GNAS Dashboard — Agents Management View
  Manage Docker container agents: deploy from catalog,
  start/stop/remove, view logs, and browse the template catalog.
-->
<script setup lang="ts">
import { onMounted, ref, h, computed } from 'vue'
import { useAgentsStore } from '@/stores/agents'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { formatBytes, serviceStatusType, formatDateTime } from '@/utils/format'
import type { ServiceDefinition, AgentTemplate, LogEntry } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { useMessage, NButton } from 'naive-ui'

const store = useAgentsStore()
const message = useMessage()
const { t } = useI18n()

const showLogs = ref(false)
const currentLogsAgent = ref<string>('')

// ---- Catalog/deploy state ----
const showCatalog = ref(false)
const showDeploy = ref(false)
const selectedTemplate = ref<AgentTemplate | null>(null)
const deployLoading = ref(false)

// ---- Catalog search ----
const catalogSearchQuery = ref('')
const catalogSearchResults = ref<AgentTemplate[] | null>(null)
const catalogSearching = ref(false)

async function handleCatalogSearch() {
  if (!catalogSearchQuery.value.trim()) {
    catalogSearchResults.value = null
    return
  }
  catalogSearching.value = true
  try {
    catalogSearchResults.value = await store.searchCatalog(catalogSearchQuery.value.trim())
  } catch {
    catalogSearchResults.value = []
  } finally {
    catalogSearching.value = false
  }
}

/** Currently displayed catalog entries — full catalog or search results. */
const displayedCatalog = computed(() => catalogSearchResults.value ?? store.catalog)

const deployForm = ref({
  agentId: '',
  displayName: '',
  imageName: '',
})

onMounted(() => {
  store.fetchAgents()
  store.fetchCatalog()
})

async function handleStart(agentId: string) {
  try {
    await store.start(agentId)
    message.success(t('agents.startSuccess'))
  } catch { message.error(store.error ?? t('agents.startFailed')) }
}

async function handleStop(agentId: string) {
  try {
    await store.stop(agentId)
    message.success(t('agents.stopSuccess'))
  } catch { message.error(store.error ?? t('agents.stopFailed')) }
}

function confirmRemove(agent: ServiceDefinition) {
  useDialog().warning({
    title: t('common.confirm'),
    content: t('agents.deleteConfirm', { name: agent.displayName }),
    positiveText: t('common.delete'), negativeText: t('common.cancel'),
    onPositiveClick: async () => {
      try { await store.remove(agent.serviceId); message.success(t('agents.deleteSuccess')) }
      catch { message.error(store.error ?? t('agents.deleteFailed')) }
    },
  })
}

async function viewLogs(agentId: string) {
  currentLogsAgent.value = agentId
  showLogs.value = true
  await store.fetchLogs(agentId, 200)
}

function openDeploy(template: AgentTemplate) {
  selectedTemplate.value = template
  deployForm.value = {
    agentId: `${template.id}-${Date.now().toString(36)}`,
    displayName: template.name,
    imageName: '',
  }
  showDeploy.value = true
}

async function handleDeploy() {
  deployLoading.value = true
  try {
    await store.deploy(deployForm.value.agentId, {
      agentId: deployForm.value.agentId,
      displayName: deployForm.value.displayName,
      imageName: deployForm.value.imageName,
      capabilities: selectedTemplate.value?.capabilitiesRequired ?? [],
      volumeMapping: [],
      portMapping: [],
    })
    message.success(t('agents.deploySuccess'))
    showDeploy.value = false
    showCatalog.value = false
  } catch {
    message.error(store.error ?? t('agents.deployFailed'))
  } finally {
    deployLoading.value = false
  }
}

const agentColumns: DataTableColumns<ServiceDefinition> = [
  { title: 'ID', key: 'serviceId', ellipsis: { tooltip: true }, width: 150 },
  { title: () => t('common.name'), key: 'displayName', ellipsis: { tooltip: true } },
  { title: () => t('common.type'), key: 'type', width: 80 },
  { title: () => t('agents.startupPolicy'), key: 'startup', width: 90 },
  {
    title: () => t('common.actions'), key: 'actions', width: 240,
    render: (r) => h('div', { style: { display: 'flex', gap: '4px' } }, [
      h(NButton, { size: 'tiny', type: 'success', secondary: true, onClick: () => handleStart(r.serviceId) }, { default: () => t('agents.start') }),
      h(NButton, { size: 'tiny', type: 'warning', secondary: true, onClick: () => handleStop(r.serviceId) }, { default: () => t('agents.stop') }),
      h(NButton, { size: 'tiny', onClick: () => viewLogs(r.serviceId) }, { default: () => t('agents.logs') }),
      h(NButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => confirmRemove(r) }, { default: () => t('common.delete') }),
    ]),
  },
]

const catalogColumns: DataTableColumns<AgentTemplate> = [
  { title: () => t('common.name'), key: 'name', ellipsis: { tooltip: true }, width: 130 },
  { title: () => t('agents.version'), key: 'version', width: 80 },
  { title: () => t('alerts.description'), key: 'description', ellipsis: { tooltip: true } },
  { title: () => t('agents.capabilities'), key: 'capabilitiesRequired', width: 120, render: (r) => r.capabilitiesRequired.join(', ') || t('common.unknown') },
  {
    title: () => t('common.actions'), key: 'actions', width: 80,
    render: (r) => h(NButton, { size: 'tiny', type: 'primary', onClick: () => openDeploy(r) }, { default: () => t('agents.deploy') }),
  },
]
</script>

<template>
  <div class="agents-page">
    <PageHeader :title="t('agents.title')" :subtitle="t('agents.subtitle')">
      <template #actions>
        <NButton type="primary" size="small" @click="showCatalog = true">{{ t('agents.templateCatalog') }}</NButton>
        <NButton size="small" :loading="store.loading" @click="store.fetchAgents()">{{ t('common.refresh') }}</NButton>
      </template>
    </PageHeader>

    <NCard :title="t('agents.deployedAgents')" :bordered="false" size="small">
      <NDataTable
        v-if="store.agents.length"
        :columns="agentColumns" :data="store.agents"
        :bordered="false" size="small" striped :loading="store.loading"
      />
      <EmptyState v-else :message="t('agents.noAgents')" :description="t('agents.noAgentsHint')" />
    </NCard>

    <!-- Logs drawer -->
    <NDrawer v-model:show="showLogs" :width="700" placement="right">
      <NDrawerContent :title="t('agents.agentLogs')" closable>
        <div v-if="store.selectedAgentLogs.length" class="logs-container">
          <div v-for="entry in store.selectedAgentLogs" :key="entry.logId" class="log-line">
            <span class="log-time">{{ formatDateTime(entry.timestamp).slice(-8) }}</span>
            <NTag :type="entry.level === 'Error' || entry.level === 'Critical' ? 'error' : entry.level === 'Warning' ? 'warning' : 'default'" size="tiny" style="margin: 0 6px">
              {{ entry.level }}
            </NTag>
            <span class="log-msg">{{ entry.message }}</span>
          </div>
        </div>
        <EmptyState v-else :message="t('agents.noLogs')" />
      </NDrawerContent>
    </NDrawer>

    <!-- Catalog modal -->
    <NModal v-model:show="showCatalog" preset="card" :title="t('agents.templateCatalog')" style="width: 800px">
      <NSpace style="margin-bottom: 12px" align="center">
        <NInput
          v-model:value="catalogSearchQuery"
          :placeholder="t('agents.searchCatalogPlaceholder')"
          style="width: 300px"
          clearable
          @keyup.enter="handleCatalogSearch"
          @clear="catalogSearchResults = null"
        />
        <NButton size="small" :loading="catalogSearching" @click="handleCatalogSearch">{{ t('common.search') }}</NButton>
      </NSpace>
      <NDataTable
        v-if="displayedCatalog.length"
        :columns="catalogColumns" :data="displayedCatalog"
        :bordered="false" size="small" striped
      />
      <EmptyState v-else :message="t('agents.noTemplates')" />
    </NModal>

    <!-- Deploy modal -->
    <NModal v-model:show="showDeploy" preset="card" :title="t('agents.deployAgent')" style="width: 520px" :mask-closable="false">
      <NForm label-placement="left" label-width="100">
        <NFormItem :label="t('agents.template')">
          <NInput :value="selectedTemplate?.name ?? ''" disabled />
        </NFormItem>
        <NFormItem :label="t('agents.agentId')" required>
          <NInput v-model:value="deployForm.agentId" />
        </NFormItem>
        <NFormItem :label="t('agents.displayName')" required>
          <NInput v-model:value="deployForm.displayName" />
        </NFormItem>
        <NFormItem :label="t('agents.imageName')" required>
          <NInput v-model:value="deployForm.imageName" :placeholder="t('agents.imagePlaceholder')" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showDeploy = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="deployLoading" @click="handleDeploy" style="margin-left: 12px">{{ t('agents.deploy') }}</NButton>
      </template>
    </NModal>
  </div>
</template>

<script lang="ts">
import { useDialog } from 'naive-ui'
</script>

<style scoped>
.agents-page { max-width: 1200px; margin: 0 auto; }
.logs-container {
  max-height: 70vh;
  overflow-y: auto;
  font-family: 'Cascadia Code', 'Fira Code', monospace;
  font-size: 12px;
  line-height: 1.6;
}
.log-line {
  display: flex;
  align-items: baseline;
  padding: 2px 0;
  border-bottom: 1px solid var(--zs-border);
}
.log-time { color: var(--zs-text-tertiary); flex-shrink: 0; font-size: 11px; min-width: 70px; }
.log-msg { color: var(--zs-text-primary); word-break: break-all; }
</style>
