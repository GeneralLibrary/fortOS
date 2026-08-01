<!--
  FortOS Dashboard — Agents Management View
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
import type { ServiceDefinition, AgentTemplate, AgentConfig, AgentAccessInfo, LogEntry } from '@/types'
import type { DataTableColumns } from 'naive-ui'
import { useMessage, useDialog, NButton } from 'naive-ui'

const store = useAgentsStore()
const message = useMessage()
const dialog = useDialog()
const { t } = useI18n()

const showLogs = ref(false)
const currentLogsAgent = ref<string>('')

// ---- Catalog/deploy state ----
const showCatalog = ref(false)
const showDeploy = ref(false)
const selectedTemplate = ref<AgentTemplate | null>(null)
const deployLoading = ref(false)

// ---- Deployed agent external access panel ----
const showAccess = ref(false)
const accessInfo = ref<AgentAccessInfo | null>(null)
const accessLoading = ref(false)

// ---- Catalog search ----
const catalogSearchQuery = ref('')
const catalogSearchResults = ref<AgentTemplate[] | null>(null)
const catalogSearching = ref(false)

/** Parameters whose values are consumed by the deployment pipeline rather than the agent itself. */
const RESERVED_PARAMS = new Set(['image', 'host_port', 'container_port', 'data_dir'])

/** Extract deploy-time defaults from a template: image, ports, and editable env vars. */
function templateDefaults(template: AgentTemplate) {
  const params = new Map(template.parameters.map(p => [p.name, p.default ?? '']))
  const env: Record<string, string> = {}
  for (const p of template.parameters) {
    if (!RESERVED_PARAMS.has(p.name)) env[p.name] = p.default ?? ''
  }
  const parsePort = (name: string) => {
    const v = parseInt(params.get(name) ?? '', 10)
    return Number.isFinite(v) && v > 0 ? v : 0
  }
  const hostPort = parsePort('host_port')
  const containerPort = parsePort('container_port')
  return {
    image: params.get('image') ?? '',
    ports: hostPort > 0 && containerPort > 0
      ? [{ hostPort, containerPort }]
      : [] as { hostPort: number; containerPort: number }[],
    env,
  }
}

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
  ports: [] as { hostPort: number; containerPort: number }[],
  env: {} as Record<string, string>,
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
  dialog.warning({
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

/** Open the external access panel for a deployed agent. */
async function viewAccess(agentId: string) {
  accessLoading.value = true
  accessInfo.value = null
  showAccess.value = true
  try {
    accessInfo.value = await store.fetchAccess(agentId)
  } finally {
    accessLoading.value = false
  }
}

/** Fallback display host for access URLs when the backend has no agent:public_host configured. */
const accessHost = computed(() => window.location.hostname)

function accessUrl(port: number): string {
  return `http://${accessHost.value}:${port}`
}

function openDeploy(template: AgentTemplate) {
  selectedTemplate.value = template
  const defaults = templateDefaults(template)
  deployForm.value = {
    agentId: `${template.id}-${Date.now().toString(36)}`,
    displayName: template.name,
    imageName: defaults.image,
    ports: defaults.ports,
    env: { ...defaults.env },
  }
  showDeploy.value = true
}

function addPortRow() {
  deployForm.value.ports.push({ hostPort: 0, containerPort: 0 })
}

function removePortRow(index: number) {
  deployForm.value.ports.splice(index, 1)
}

async function handleDeploy() {
  deployLoading.value = true
  try {
    const config = {
      agentId: deployForm.value.agentId,
      displayName: deployForm.value.displayName,
      imageName: deployForm.value.imageName,
      capabilities: selectedTemplate.value?.capabilitiesRequired ?? [],
      volumeMapping: [],
      portMapping: deployForm.value.ports
        .filter(p => p.hostPort > 0 && p.containerPort > 0)
        .map(p => ({ hostPort: p.hostPort, containerPort: p.containerPort, protocol: 'tcp' })),
      environment: deployForm.value.env,
    } satisfies AgentConfig
    if (!selectedTemplate.value) {
      message.error(t('agents.deployFailed'))
      return
    }
    const deployed = await store.deploy(selectedTemplate.value.id, config)
    message.success(t('agents.deploySuccess'))
    showDeploy.value = false
    showCatalog.value = false
    if (deployed) viewAccess(deployed.serviceId)
  } catch {
    message.error(store.error ?? t('agents.deployFailed'))
  } finally {
    deployLoading.value = false
  }
}

/** Template parameter row model for the deploy form. */
const deployEnvRows = computed(() => Object.entries(deployForm.value.env))

/** Overall deployment progress (0-100) derived from the backend stage. */
const deployPercent = computed(() => {
  const stage = store.deployStatus?.stage
  switch (stage) {
    case 'queued': return 5
    case 'pulling': return 35
    case 'deploying': return 70
    case 'success': return 100
    case 'failed': return 100
    default: return 5
  }
})

/** Human-readable progress message: prefer backend message, fall back to stage text. */
const deployMessage = computed(() => {
  if (store.deployStatus?.message) return store.deployStatus.message
  switch (store.deployStatus?.stage) {
    case 'queued': return t('agents.deployStageQueued')
    case 'pulling': return t('agents.deployStagePulling')
    case 'deploying': return t('agents.deployStageDeploying')
    case 'success': return t('agents.deployStageSuccess')
    case 'failed': return store.deployStatus.error ?? t('agents.deployFailed')
    default: return ''
  }
})

const agentColumns: DataTableColumns<ServiceDefinition> = [
  { title: 'ID', key: 'serviceId', ellipsis: { tooltip: true }, width: 150 },
  { title: () => t('common.name'), key: 'displayName', ellipsis: { tooltip: true } },
  { title: () => t('common.type'), key: 'type', width: 80 },
  { title: () => t('agents.startupPolicy'), key: 'startup', width: 90 },
  {
    title: () => t('common.actions'), key: 'actions', width: 290,
    render: (r) => h('div', { style: { display: 'flex', gap: '4px' } }, [
      h(NButton, { size: 'tiny', type: 'primary', secondary: true, onClick: () => viewAccess(r.serviceId) }, { default: () => t('agents.access') }),
      h(NButton, { size: 'tiny', type: 'success', secondary: true, onClick: () => handleStart(r.serviceId) }, { default: () => t('agents.start') }),
      h(NButton, { size: 'tiny', type: 'warning', secondary: true, onClick: () => handleStop(r.serviceId) }, { default: () => t('agents.stop') }),
      h(NButton, { size: 'tiny', onClick: () => viewLogs(r.serviceId) }, { default: () => t('agents.logs') }),
      h(NButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => confirmRemove(r) }, { default: () => t('common.delete') }),
    ]),
  },
]

/** First published port of a template (from its host_port parameter), used in catalog cards. */
function templatePort(template: AgentTemplate): string {
  const port = template.parameters.find(p => p.name === 'host_port')
  return port?.default ? `:${port.default}` : ''
}

/** Whether the template logo is an image (site-relative path or remote URL), otherwise it is an emoji. */
function isImageLogo(logo?: string | null): boolean {
  return !!logo && (logo.startsWith('/') || /^https?:\/\//i.test(logo))
}

const catalogColumns: DataTableColumns<AgentTemplate> = [
  { title: () => t('common.name'), key: 'name', ellipsis: { tooltip: true }, width: 130 },
  { title: () => t('agents.version'), key: 'version', width: 80 },
  { title: () => t('alerts.description'), key: 'description', ellipsis: { tooltip: true } },
  { title: '端口', key: 'port', width: 70, render: (r) => templatePort(r) },
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
    <NModal v-model:show="showCatalog" preset="card" :title="t('agents.templateCatalog')" style="width: 900px">
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
      <div v-if="displayedCatalog.length" class="catalog-grid">
        <div v-for="tpl in displayedCatalog" :key="tpl.id" class="catalog-card">
          <div class="catalog-card-header">
            <span class="catalog-card-logo">
              <img v-if="isImageLogo(tpl.logo)" :src="tpl.logo ?? undefined" alt="" />
              <span v-else>{{ tpl.logo ?? '📦' }}</span>
            </span>
            <span class="catalog-card-name">{{ tpl.name }}</span>
            <NTag v-if="templatePort(tpl)" size="small" type="info">{{ templatePort(tpl) }}</NTag>
          </div>
          <div class="catalog-card-desc">{{ tpl.description }}</div>
          <div class="catalog-card-footer">
            <NButton size="small" type="primary" @click="openDeploy(tpl)">{{ t('agents.deploy') }}</NButton>
          </div>
        </div>
      </div>
      <EmptyState v-else :message="t('agents.noTemplates')" />
    </NModal>

    <!-- Deploy modal -->
    <NModal v-model:show="showDeploy" preset="card" :title="t('agents.deployAgent')" style="width: 620px" :mask-closable="false">
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
        <NFormItem :label="t('agents.ports')">
          <div style="width: 100%">
            <div v-for="(p, i) in deployForm.ports" :key="i" class="port-row">
              <NInputNumber v-model:value="p.hostPort" :placeholder="t('agents.hostPort')" style="width: 120px" :min="0" :max="65535" />
              <span class="port-arrow">→</span>
              <NInputNumber v-model:value="p.containerPort" :placeholder="t('agents.containerPort')" style="width: 120px" :min="0" :max="65535" />
              <NButton size="small" quaternary type="error" @click="removePortRow(i)">✕</NButton>
            </div>
            <NButton size="tiny" @click="addPortRow">+ {{ t('agents.addPort') }}</NButton>
          </div>
        </NFormItem>
        <NFormItem v-if="deployEnvRows.length" :label="t('agents.environment')">
          <div style="width: 100%">
            <div v-for="[key, value] in deployEnvRows" :key="key" class="env-row">
              <span class="env-key">{{ key }}</span>
              <NInput v-model:value="deployForm.env[key]" :placeholder="t('agents.envValuePlaceholder')" />
            </div>
            <NText depth="3" style="font-size: 12px">{{ t('agents.envHint') }}</NText>
          </div>
        </NFormItem>
        <NFormItem v-if="deployLoading" :label="t('agents.deployProgress')">
          <div style="width: 100%">
            <NProgress
              type="line"
              :percentage="deployPercent"
              :status="store.deployStatus?.status === 'failed' ? 'error' : 'info'"
              indicator-placement="inside"
              processing
            />
            <NText depth="3" style="font-size: 12px; margin-top: 6px; display: block">{{ deployMessage }}</NText>
          </div>
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showDeploy = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="deployLoading" @click="handleDeploy" style="margin-left: 12px">{{ t('agents.deploy') }}</NButton>
      </template>
    </NModal>

    <!-- External access modal -->
    <NModal v-model:show="showAccess" preset="card" :title="t('agents.accessTitle')" style="width: 640px">
      <NSpin :show="accessLoading">
        <template v-if="accessInfo">
          <div v-if="accessInfo.ports.length" class="access-section">
            <div class="access-label">{{ t('agents.accessUrls') }}</div>
            <div v-for="p in accessInfo.ports" :key="p.hostPort" class="access-url-row">
              <a :href="accessUrl(p.hostPort)" target="_blank" rel="noopener">{{ accessUrl(p.hostPort) }}</a>
              <NTag size="tiny" type="info">{{ p.containerPort }}/{{ p.protocol }}</NTag>
            </div>
          </div>
          <div v-if="accessInfo.env.length" class="access-section">
            <div class="access-label">{{ t('agents.accessEnv') }}</div>
            <div v-for="e in accessInfo.env" :key="e.name" class="access-env-row">
              <code>{{ e.name }}</code>
              <NTag size="tiny" :type="e.set ? 'success' : 'warning'">{{ e.set ? t('agents.envSet') : t('agents.envUnset') }}</NTag>
            </div>
            <NText depth="3" style="font-size: 12px">{{ t('agents.envEditHint') }}</NText>
          </div>
          <div v-if="accessInfo.accessNotes.length" class="access-section">
            <div class="access-label">{{ t('agents.accessNotes') }}</div>
            <ul class="access-notes">
              <li v-for="(note, i) in accessInfo.accessNotes" :key="i">{{ note }}</li>
            </ul>
          </div>
        </template>
        <EmptyState v-else-if="!accessLoading" :message="t('agents.noAccessInfo')" />
      </NSpin>
      <template #footer>
        <NButton @click="showAccess = false">{{ t('common.close') }}</NButton>
      </template>
    </NModal>
  </div>
</template>

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

/* Market catalog cards */
.catalog-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 12px;
  max-height: 60vh;
  overflow-y: auto;
  padding: 2px;
}
.catalog-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 14px;
  border-radius: var(--zs-radius);
  border: 1px solid var(--zs-border);
  background: var(--zs-bg-card);
  transition: all var(--zs-transition);
}
.catalog-card:hover {
  border-color: var(--zs-border-light);
  background: var(--zs-bg-card-hover);
}
.catalog-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.catalog-card-logo {
  width: 34px;
  height: 34px;
  border-radius: 10px;
  background: var(--zs-bg-input);
  border: 1px solid var(--zs-border);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 20px;
  flex-shrink: 0;
  overflow: hidden;
}
.catalog-card-logo img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.catalog-card-name {
  flex: 1;
  font-size: 14px;
  font-weight: 600;
  color: var(--zs-text-primary);
  word-break: break-all;
}
.catalog-card-desc {
  flex: 1;
  font-size: 12px;
  color: var(--zs-text-secondary);
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.catalog-card-footer {
  display: flex;
  justify-content: flex-end;
}

/* Deploy form port / env rows */
.port-row, .env-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}
.port-arrow { color: var(--zs-text-tertiary); }
.env-key {
  width: 200px;
  flex-shrink: 0;
  font-size: 12px;
  font-weight: 500;
  color: var(--zs-text-secondary);
  word-break: break-all;
  font-family: 'Cascadia Code', 'Fira Code', monospace;
}

/* Access panel */
.access-section { margin-bottom: 16px; }
.access-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--zs-text-primary);
  margin-bottom: 8px;
}
.access-url-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  border-radius: var(--zs-radius-sm);
  background: var(--zs-bg-input);
  border: 1px solid var(--zs-border);
  margin-bottom: 6px;
  font-family: 'Cascadia Code', 'Fira Code', monospace;
  font-size: 13px;
}
.access-url-row a { color: var(--zs-primary); text-decoration: none; }
.access-url-row a:hover { text-decoration: underline; }
.access-env-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
}
.access-env-row code {
  font-size: 12px;
  font-family: 'Cascadia Code', 'Fira Code', monospace;
  color: var(--zs-text-secondary);
}
.access-notes {
  margin: 0;
  padding-left: 18px;
  font-size: 12px;
  color: var(--zs-text-secondary);
  line-height: 1.7;
}
</style>
