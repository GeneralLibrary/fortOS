<!--
  FortOS Dashboard — Disk & Storage Settings Panel
  Rendered inside the Settings page for the "storage" category:
  disk inventory + RAID pool creation (modes mirror the NAS convention:
  RAID0/1/5/6/10 — levels the backend actually supports).
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMessage } from 'naive-ui'
import { listDisks, listRaids, createRaid, getRaidCapability, getDeviceStatus, formatDevice, mountDevice, unmountDevice } from '@/api/disks'
import { ApiError } from '@/api/client'
import { formatBytes, formatTemperature } from '@/utils/format'
import EmptyState from '@/components/common/EmptyState.vue'
import type { DiskInfo, RaidMetrics, RaidCapability, DeviceStatus } from '@/types'
import { RaidLevel } from '@/types'

const { t } = useI18n()
const message = useMessage()

const disks = ref<DiskInfo[]>([])
const raids = ref<RaidMetrics[]>([])
const loading = ref(false)
const creating = ref(false)
/** null = capability not yet loaded; available=false = mdadm missing on host. */
const capability = ref<RaidCapability | null>(null)

/** Per-array block-device status (filesystem / mount point), keyed by array name. */
const statuses = ref<Record<string, DeviceStatus>>({})
/** Array currently being initialized (format + mount), or null. */
const initPool = ref<string | null>(null)
const initFsType = ref('ext4')
const initMountPoint = ref('')
const initializing = ref(false)

/** File systems the backend can create (mirrors LinuxFileSystem.AllowedFileSystems). */
const FS_TYPES = ['ext4', 'xfs', 'btrfs'] as const

const selectedLevel = ref<RaidLevel | null>(null)
const selectedDisks = ref<string[]>([])

/** RAID modes the backend supports, with their minimum disk counts. */
const RAID_MODES: { level: RaidLevel; minDisks: number }[] = [
  { level: RaidLevel.Raid0, minDisks: 2 },
  { level: RaidLevel.Raid1, minDisks: 2 },
  { level: RaidLevel.Raid5, minDisks: 3 },
  { level: RaidLevel.Raid6, minDisks: 4 },
  { level: RaidLevel.Raid10, minDisks: 4 },
]

async function load(): Promise<void> {
  loading.value = true
  try {
    const [diskList, raidList, cap] = await Promise.all([listDisks(), listRaids(), getRaidCapability()])
    disks.value = diskList
    raids.value = raidList
    capability.value = cap
    await loadRaidStatuses()
  } catch {
    message.error(t('settings.storage.loadFailed'))
  } finally {
    loading.value = false
  }
}

/** Query filesystem / mount status of each active md array (best-effort). */
async function loadRaidStatuses(): Promise<void> {
  const entries = await Promise.all(
    raids.value.map(async raid => {
      const status = await getDeviceStatus(`/dev/${raid.name}`).catch(() => null)
      return [raid.name, status] as const
    }),
  )
  const result: Record<string, DeviceStatus> = {}
  for (const [name, status] of entries) {
    if (status) result[name] = status
  }
  statuses.value = result
}

onMounted(load)

const selectedMode = computed(() => RAID_MODES.find(m => m.level === selectedLevel.value))

const needMore = computed(() => {
  const mode = selectedMode.value
  if (!mode) return 0
  return Math.max(0, mode.minDisks - selectedDisks.value.length)
})

const canCreate = computed(() => selectedLevel.value !== null && needMore.value === 0)

function toggleDisk(path: string): void {
  const disk = disks.value.find(d => d.path === path)
  // 挂载中的磁盘(如系统盘)禁止选为 RAID 成员:后端同样会拒绝。
  if (disk?.mountPoint != null) return
  if (selectedDisks.value.includes(path)) {
    selectedDisks.value = selectedDisks.value.filter(p => p !== path)
  } else {
    selectedDisks.value = [...selectedDisks.value, path]
  }
}

async function handleCreate(): Promise<void> {
  if (!selectedLevel.value) return
  creating.value = true
  try {
    const result = await createRaid(selectedLevel.value, selectedDisks.value, true)
    if (result.success) {
      message.success(t('settings.storage.raidCreated', { pool: result.poolId ?? '' }))
      selectedLevel.value = null
      selectedDisks.value = []
      await load()
      // 引导闭环:创建成功后直接打开「格式化并挂载」面板。
      const poolName = result.poolId?.replace(/^\/dev\//, '') ?? ''
      if (poolName) openInit(poolName)
    } else {
      message.error(result.message ?? t('settings.storage.raidFailed'))
    }
  } catch {
    message.error(t('settings.storage.raidFailed'))
  } finally {
    creating.value = false
  }
}

function openInit(poolName: string): void {
  initPool.value = poolName
  initFsType.value = 'ext4'
  initMountPoint.value = `/srv/nas/raid-${poolName}`
}

const canInitialize = computed(() => {
  if (!initPool.value || !initMountPoint.value) return false
  return initMountPoint.value.startsWith('/srv/nas/')
})

/** Format (if needed) then mount the array; persists to fstab. */
async function handleInitialize(): Promise<void> {
  const pool = initPool.value
  if (!pool || !canInitialize.value) return
  initializing.value = true
  try {
    const device = `/dev/${pool}`
    const status = statuses.value[pool]
    // 仅在明确检测到未格式化时执行破坏性的格式化;状态未知/设备不可见时直接尝试挂载,
    // 避免在无法确认设备内容的情况下清盘。
    if (status && !status.fileSystem) {
      await formatDevice(device, initFsType.value)
    }
    await mountDevice(device, initMountPoint.value, initFsType.value)
    message.success(t('settings.storage.initialized', { pool, mount: initMountPoint.value }))
    initPool.value = null
    initMountPoint.value = ''
    await load()
  } catch (error) {
    message.error(error instanceof ApiError ? error.message : t('settings.storage.initFailed'))
  } finally {
    initializing.value = false
  }
}

/** Unmount an array (removes its fstab entry too). */
async function handleUnmount(poolName: string): Promise<void> {
  const status = statuses.value[poolName]
  if (!status?.mountPoint) return
  try {
    await unmountDevice(status.mountPoint)
    message.success(t('settings.storage.unmounted'))
    await load()
  } catch (error) {
    message.error(error instanceof ApiError ? error.message : t('settings.storage.unmountFailed'))
  }
}
</script>

<template>
  <div class="storage-panel">
    <!-- Existing RAID arrays -->
    <section class="storage-block">
      <h4 class="storage-block-title">{{ t('settings.storage.existingRaids') }}</h4>
      <div v-if="raids.length" class="raid-list">
        <div v-for="raid in raids" :key="raid.name" class="raid-item">
          <div class="raid-item-info">
            <div class="raid-item-head">
              <code class="raid-name">{{ raid.name }}</code>
              <NTag size="small" :type="raid.healthy ? 'success' : 'warning'" round>
                {{ raid.healthy ? t('settings.storage.healthy') : t('settings.storage.degraded') }}
              </NTag>
            </div>
            <div class="raid-item-meta">
              <span>{{ t('settings.storage.raidLevel') }}: {{ raid.level }}</span>
              <span>{{ t('settings.storage.members') }}: {{ raid.activeDevices }}/{{ raid.totalDevices }}</span>
            </div>
            <NProgress
              v-if="raid.operation && raid.progressPercent != null"
              type="line" :percentage="Math.round(raid.progressPercent)"
              indicator-placement="inside" :height="6"
            >
              {{ raid.operation }} {{ Math.round(raid.progressPercent) }}%
            </NProgress>
            <!-- Format / mount state -->
            <div class="raid-item-status">
              <template v-if="statuses[raid.name]">
                <template v-if="statuses[raid.name].mountPoint">
                  <NTag size="tiny" type="success" :bordered="false">
                    {{ t('settings.storage.mountedAt', { mount: statuses[raid.name].mountPoint }) }}
                  </NTag>
                  <NButton size="tiny" quaternary type="error" @click="handleUnmount(raid.name)">
                    {{ t('settings.storage.unmount') }}
                  </NButton>
                </template>
                <template v-else-if="statuses[raid.name].fileSystem">
                  <span class="raid-item-state">{{ t('settings.storage.formattedNotMounted', { fs: statuses[raid.name].fileSystem }) }}</span>
                  <NButton size="tiny" secondary @click="openInit(raid.name)">{{ t('settings.storage.mount') }}</NButton>
                </template>
                <template v-else>
                  <NTag size="tiny" type="warning" :bordered="false">{{ t('settings.storage.noRaidInit') }}</NTag>
                  <NButton size="tiny" secondary @click="openInit(raid.name)">{{ t('settings.storage.initialize') }}</NButton>
                </template>
              </template>
              <span v-else class="raid-item-state">{{ t('settings.storage.noRaidStatus') }}</span>
            </div>
            <!-- Initialize (format + mount) form -->
            <div v-if="initPool === raid.name" class="raid-init-form">
              <NSelect
                v-model:value="initFsType"
                size="small"
                class="raid-init-fs"
                :options="FS_TYPES.map(fs => ({ label: fs.toUpperCase(), value: fs }))"
              />
              <NInput v-model:value="initMountPoint" size="small" class="raid-init-mount" placeholder="/srv/nas/raid-md0" />
              <NPopconfirm
                :positive-text="t('settings.storage.confirmInitOk')"
                :negative-text="t('common.cancel')"
                @positive-click="handleInitialize"
              >
                <template #trigger>
                  <NButton size="small" type="primary" :disabled="!canInitialize" :loading="initializing">
                    {{ t('settings.storage.initialize') }}
                  </NButton>
                </template>
                {{ t('settings.storage.confirmInitDesc', { pool: raid.name }) }}
              </NPopconfirm>
              <span v-if="!canInitialize" class="raid-init-hint">{{ t('settings.storage.mountPointHint') }}</span>
            </div>
          </div>
        </div>
      </div>
      <EmptyState v-else :message="t('settings.storage.noRaids')" />
    </section>

    <!-- Create RAID (only when mdadm is available on the host) -->
    <section v-if="capability?.available" class="storage-block">
      <h4 class="storage-block-title">{{ t('settings.storage.createRaid') }}</h4>

      <!-- Mode picker -->
      <div class="raid-modes">
        <button
          v-for="mode in RAID_MODES"
          :key="mode.level"
          type="button"
          class="raid-mode"
          :class="{ active: selectedLevel === mode.level }"
          @click="selectedLevel = mode.level"
        >
          <span class="raid-mode-name">{{ t(`settings.storage.modes.${mode.level}.name`) }}</span>
          <span class="raid-mode-tags">
            <NTag size="tiny" :bordered="false">{{ t('settings.storage.minDisks', { count: mode.minDisks }) }}</NTag>
            <NTag size="tiny" :bordered="false">{{ t(`settings.storage.modes.${mode.level}.capacity`) }}</NTag>
          </span>
          <span class="raid-mode-desc">{{ t(`settings.storage.modes.${mode.level}.desc`) }}</span>
        </button>
      </div>

      <!-- Disk picker -->
      <div class="disk-picker-head">
        <span>{{ t('settings.storage.selectDisks') }}</span>
        <span v-if="selectedLevel" class="disk-picker-count">
          {{ selectedDisks.length }}/{{ selectedMode?.minDisks }}
        </span>
      </div>
      <div class="disk-grid">
        <button
          v-for="disk in disks"
          :key="disk.path"
          type="button"
          class="disk-card"
          :class="{ selected: selectedDisks.includes(disk.path), disabled: disk.mountPoint != null }"
          :disabled="disk.mountPoint != null"
          :title="disk.mountPoint ? t('settings.storage.diskInUse', { mount: disk.mountPoint }) : undefined"
          @click="toggleDisk(disk.path)"
        >
          <div class="disk-card-head">
            <code>{{ disk.path }}</code>
            <span class="disk-card-tags">
              <NTag v-if="disk.mountPoint" size="tiny" :bordered="false" type="warning">
                {{ t('settings.storage.inUse') }}
              </NTag>
              <NTag size="tiny" :bordered="false" :type="disk.isSsd ? 'info' : 'default'">
                {{ disk.isSsd ? t('settings.storage.ssd') : t('settings.storage.hdd') }}
              </NTag>
            </span>
          </div>
          <div class="disk-card-model">{{ disk.model || t('common.unknown') }}</div>
          <div class="disk-card-meta">
            <span>{{ formatBytes(disk.sizeBytes) }}</span>
            <span v-if="disk.temperatureCelsius > 0">{{ formatTemperature(disk.temperatureCelsius) }}</span>
          </div>
        </button>
      </div>
      <EmptyState v-if="!disks.length && !loading" :message="t('settings.storage.noDisks')" />

      <!-- Create action -->
      <div class="raid-create-bar">
        <span class="raid-create-hint">
          <template v-if="selectedLevel && needMore > 0">{{ t('settings.storage.needMore', { count: needMore }) }}</template>
          <template v-else-if="!selectedLevel">{{ t('settings.storage.chooseModeHint') }}</template>
          <template v-else>{{ t('settings.storage.readyHint') }}</template>
        </span>
        <NPopconfirm
          :positive-text="t('settings.storage.confirmOk')"
          :negative-text="t('common.cancel')"
          @positive-click="handleCreate"
        >
          <template #trigger>
            <NButton type="error" size="small" :disabled="!canCreate" :loading="creating">
              {{ t('settings.storage.createRaid') }}
            </NButton>
          </template>
          {{ t('settings.storage.confirmDesc') }}
        </NPopconfirm>
      </div>
    </section>

    <!-- mdadm missing: guide the user through installation -->
    <section v-else-if="capability" class="storage-block tool-missing">
      <h4 class="storage-block-title">{{ t('settings.storage.toolMissing') }}</h4>
      <p class="tool-missing-desc">
        {{ t('settings.storage.toolMissingDesc', { tool: capability.tool }) }}
      </p>
      <div class="tool-cmd">
        <code>sudo apt-get install -y {{ capability.tool }}</code>
      </div>
      <p class="tool-missing-hint">{{ t('settings.storage.toolMissingHint') }}</p>
    </section>
  </div>
</template>

<style scoped>
.storage-panel {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.storage-block {
  background: var(--zs-bg-card);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius-lg);
  padding: 18px;
}

.storage-block-title {
  margin: 0 0 14px;
  font-size: 14px;
  font-weight: 700;
  color: var(--zs-text-primary);
}

/* ---- Existing RAID arrays ---- */
.raid-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.raid-item {
  padding: 12px 14px;
  background: var(--zs-bg-input);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius);
}

.raid-item-head {
  display: flex;
  align-items: center;
  gap: 10px;
}

.raid-name {
  font-size: 13px;
  font-weight: 600;
  color: var(--zs-text-primary);
}

.raid-item-meta {
  display: flex;
  gap: 16px;
  margin-top: 6px;
  font-size: 12px;
  color: var(--zs-text-tertiary);
}

.raid-item-status {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
  flex-wrap: wrap;
}

.raid-item-state {
  font-size: 12px;
  color: var(--zs-text-tertiary);
}

.raid-init-form {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 10px;
  padding: 10px;
  background: var(--zs-bg-card);
  border: 1px dashed var(--zs-border);
  border-radius: var(--zs-radius);
  flex-wrap: wrap;
}

.raid-init-fs {
  width: 110px;
}

.raid-init-mount {
  flex: 1;
  min-width: 180px;
}

.raid-init-hint {
  font-size: 11px;
  color: var(--zs-text-tertiary);
  width: 100%;
}

.raid-item .n-progress {
  margin-top: 8px;
}

/* ---- RAID mode picker ---- */
.raid-modes {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 10px;
  margin-bottom: 16px;
}

.raid-mode {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 12px 14px;
  text-align: left;
  background: var(--zs-bg-input);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius);
  cursor: pointer;
  transition: all var(--zs-transition);
}

.raid-mode:hover {
  border-color: var(--zs-border-light);
}

.raid-mode.active {
  border-color: var(--zs-primary);
  background: var(--zs-primary-bg);
}

.raid-mode-name {
  font-size: 13px;
  font-weight: 700;
  color: var(--zs-text-primary);
}

.raid-mode-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 6px;
}

.raid-mode-tags :deep(.n-tag) {
  max-width: 100%;
}

.raid-mode-tags :deep(.n-tag__content) {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.raid-mode-desc {
  font-size: 11px;
  color: var(--zs-text-tertiary);
  line-height: 1.5;
}

/* ---- Disk picker ---- */
.disk-picker-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
  font-size: 12px;
  color: var(--zs-text-secondary);
}

.disk-picker-count {
  color: var(--zs-primary);
  font-weight: 600;
}

.disk-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 10px;
}

.disk-card {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 14px;
  text-align: left;
  background: var(--zs-bg-input);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius);
  cursor: pointer;
  transition: all var(--zs-transition);
}

.disk-card:hover {
  border-color: var(--zs-border-light);
}

.disk-card.selected {
  border-color: var(--zs-primary);
  background: var(--zs-primary-bg);
  box-shadow: var(--zs-shadow-sm);
}

.disk-card:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.disk-card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.disk-card-tags {
  display: inline-flex;
  gap: 4px;
  min-width: 0;
}

.disk-card-head code {
  font-size: 12px;
  font-weight: 600;
  color: var(--zs-text-primary);
}

.disk-card-model {
  font-size: 11px;
  color: var(--zs-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.disk-card-meta {
  display: flex;
  gap: 10px;
  font-size: 11px;
  color: var(--zs-text-tertiary);
}

/* ---- Create bar ---- */
.raid-create-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid var(--zs-border);
}

.raid-create-hint {
  font-size: 12px;
  color: var(--zs-text-tertiary);
}

/* ---- mdadm missing banner ---- */
.tool-missing-desc {
  margin: 0 0 12px;
  font-size: 12px;
  color: var(--zs-text-secondary);
  line-height: 1.6;
}

.tool-cmd {
  display: inline-block;
  padding: 10px 14px;
  background: var(--zs-bg-input);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius);
  margin-bottom: 10px;
}

.tool-cmd code {
  font-size: 13px;
  color: var(--zs-primary);
  user-select: all;
}

.tool-missing-hint {
  margin: 0;
  font-size: 12px;
  color: var(--zs-text-tertiary);
}
</style>
