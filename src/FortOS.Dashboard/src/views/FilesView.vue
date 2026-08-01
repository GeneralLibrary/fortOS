<!--
  FortOS Dashboard — 极空间-style File Manager
  Directory navigation with grid/thumbnail and list view modes,
  image/video preview, text editing, upload, and CRUD operations.
-->
<script setup lang="ts">
import { ref, computed, onMounted, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMessage, useDialog, NTag, NButton, NIcon as NIconComp, NSpin } from 'naive-ui'
import {
  listFiles, readFileContent, createDirectory,
  writeFile, updateFile, moveFile, deleteFile,
  createUpload, appendUpload, finalizeUpload, abortUpload,
  downloadBlob,
} from '@/api/files'
import EmptyState from '@/components/common/EmptyState.vue'
import { formatBytes, formatDateTime } from '@/utils/format'
import type { ManagedFileEntry } from '@/types'
import type { DataTableColumns } from 'naive-ui'

const { t } = useI18n()
const message = useMessage()
// useDialog/useMessage must be resolved inside setup; calling useDialog() inside
// an event handler returns undefined (no inject context) and crashes on .warning.
const dialog = useDialog()

// ---- View mode ----
type ViewMode = 'list' | 'grid'
const viewMode = ref<ViewMode>('list')

// ---- Directory navigation state ----
// Data root must match the backend FileManagerService default (FortOS_DATA_ROOT or /srv/nas).
// The backend permission engine only allows operations under this root, so the UI
// must treat it as the top-level browseable path instead of the filesystem root `/`.
const DATA_ROOT = '/srv/nas'
const currentPath = ref(DATA_ROOT)
const entries = ref<ManagedFileEntry[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const downloading = ref(false)

// ---- Breadcrumbs ----
const breadcrumbs = computed(() => {
  if (currentPath.value === DATA_ROOT) return [{ label: t('files.rootPath'), path: DATA_ROOT }]
  const segments = currentPath.value.slice(DATA_ROOT.length).split('/').filter(Boolean)
  const crumbs = [{ label: t('files.rootPath'), path: DATA_ROOT }]
  let acc = DATA_ROOT
  for (const seg of segments) {
    acc += '/' + seg
    crumbs.push({ label: seg, path: acc })
  }
  return crumbs
})

const parentPath = computed(() => {
  if (currentPath.value === DATA_ROOT) return null
  const idx = currentPath.value.lastIndexOf('/')
  // Never navigate above the data root.
  return idx <= DATA_ROOT.length - 1 ? DATA_ROOT : currentPath.value.slice(0, idx)
})

// ---- File type helpers ----
const IMG_EXTS = new Set(['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg'])
const VID_EXTS = new Set(['.mp4', '.webm', '.mkv', '.mov', '.avi', '.m4v'])
const TXT_EXTS = new Set(['.txt', '.md', '.yml', '.yaml', '.json', '.xml', '.html', '.css', '.js', '.ts', '.py', '.sh', '.cfg', '.ini', '.conf', '.env', '.log', '.csv', '.sql', '.cs', '.java', '.go', '.rs', '.toml'])

function ext(name: string): string {
  const dot = name.lastIndexOf('.')
  return dot === -1 ? '' : name.slice(dot).toLowerCase()
}
function isImage(name: string) { return IMG_EXTS.has(ext(name)) }
function isVideo(name: string) { return VID_EXTS.has(ext(name)) }
function isText(name: string) { return TXT_EXTS.has(ext(name)) }

function fileTypeLabel(name: string, isDir: boolean): string {
  if (isDir) return t('files.folder')
  if (isImage(name)) return t('files.image')
  if (isVideo(name)) return t('files.video')
  if (isText(name)) return t('files.text')
  return t('files.other')
}

/** Icon for grid view thumbnails. */
function fileTypeIcon(name: string, isDir: boolean): string {
  if (isDir) return '📁'
  if (isImage(name)) return '🖼️'
  if (isVideo(name)) return '🎬'
  if (isText(name)) return '📄'
  return '📦'
}

/** Color class for grid card icon background. */
function fileIconBg(name: string, isDir: boolean): string {
  if (isDir) return 'bg-blue'
  if (isImage(name)) return 'bg-pink'
  if (isVideo(name)) return 'bg-purple'
  if (isText(name)) return 'bg-teal'
  return 'bg-gray'
}

async function openExternalDownload(entry: ManagedFileEntry | null) {
  if (!entry) return
  downloading.value = true
  try {
    // Download through the authenticated fetch layer: a plain window.open cannot
    // attach the Bearer token, so downloads would 401 under require_auth.
    const blob = await downloadBlob(entry.path)
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = entry.name
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
  } catch (e) {
    message.error((e as Error).message || t('files.downloadFailed'))
  } finally {
    downloading.value = false
  }
}

// ---- Fetch directory ----
async function fetchDir() {
  loading.value = true
  error.value = null
  try {
    entries.value = await listFiles(currentPath.value)
  } catch (e) {
    error.value = (e as Error).message
    entries.value = []
  } finally {
    loading.value = false
  }
}

onMounted(fetchDir)

function navigateTo(path: string) {
  currentPath.value = path
  fetchDir()
}

function goUp() {
  if (parentPath.value !== null) {
    navigateTo(parentPath.value)
  }
}

function handleRowClick(row: ManagedFileEntry) {
  if (row.isDirectory) {
    navigateTo(row.path)
  } else {
    openPreview(row)
  }
}

// ---- Folder create ----
const showCreateFolder = ref(false)
const newFolderName = ref('')
const creatingFolder = ref(false)

async function handleCreateFolder() {
  if (!newFolderName.value.trim()) return
  creatingFolder.value = true
  const fullPath = `${currentPath.value}/${newFolderName.value.trim()}`
  try {
    await createDirectory(fullPath)
    message.success(t('files.createFolderSuccess'))
    showCreateFolder.value = false
    newFolderName.value = ''
    fetchDir()
  } catch {
    message.error(t('files.createFolderFailed'))
  } finally {
    creatingFolder.value = false
  }
}

// ---- File create ----
const showCreateFile = ref(false)
const newFileName = ref('')
const creatingFile = ref(false)

async function handleCreateFile() {
  if (!newFileName.value.trim()) return
  creatingFile.value = true
  const fullPath = `${currentPath.value}/${newFileName.value.trim()}`
  try {
    await writeFile(fullPath, '', false)
    message.success(t('files.createFileSuccess'))
    showCreateFile.value = false
    newFileName.value = ''
    fetchDir()
  } catch {
    message.error(t('files.saveFailed'))
  } finally {
    creatingFile.value = false
  }
}

// ---- Delete ----
function confirmDelete(entry: ManagedFileEntry) {
  dialog.warning({
    title: t('common.confirm'),
    content: t('files.deleteConfirm', { name: entry.name }),
    positiveText: t('common.delete'),
    negativeText: t('common.cancel'),
    onPositiveClick: async () => {
      try {
        await deleteFile(entry.path)
        message.success(t('files.deleteSuccess'))
        fetchDir()
      } catch { message.error(t('files.deleteFailed')) }
    },
  })
}

// ---- Rename ----
const showRename = ref(false)
const renameTarget = ref<ManagedFileEntry | null>(null)
const renameNewName = ref('')
const renaming = ref(false)

function openRename(entry: ManagedFileEntry) {
  renameTarget.value = entry
  renameNewName.value = entry.name
  showRename.value = true
}

async function handleRename() {
  if (!renameTarget.value || !renameNewName.value.trim()) return
  renaming.value = true
  const oldPath = renameTarget.value.path
  const parent = oldPath.includes('/') ? oldPath.slice(0, oldPath.lastIndexOf('/')) || '/' : '/'
  const newPath = parent === '/' ? `/${renameNewName.value.trim()}` : `${parent}/${renameNewName.value.trim()}`
  try {
    await moveFile(oldPath, newPath, false)
    message.success(t('files.renameSuccess'))
    showRename.value = false
    fetchDir()
  } catch {
    message.error(t('files.renameFailed'))
  } finally {
    renaming.value = false
  }
}

// ---- Move ----
const showMove = ref(false)
const moveTarget = ref<ManagedFileEntry | null>(null)
const moveDestPath = ref('')
const moving = ref(false)

function openMove(entry: ManagedFileEntry) {
  moveTarget.value = entry
  moveDestPath.value = '/'
  showMove.value = true
}

async function handleMove() {
  if (!moveTarget.value || !moveDestPath.value.trim()) return
  moving.value = true
  try {
    await moveFile(moveTarget.value.path, moveDestPath.value.trim(), false)
    message.success(t('files.moveSuccess'))
    showMove.value = false
    fetchDir()
  } catch {
    message.error(t('files.moveFailed'))
  } finally {
    moving.value = false
  }
}

// ---- Upload ----
const uploadTarget = ref<HTMLInputElement | null>(null)
const uploading = ref(false)
const uploadFileName = ref('')

function triggerUpload() {
  uploadTarget.value?.click()
}

async function handleFileSelected(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  uploadFileName.value = file.name
  uploading.value = true

  const fullPath = `${currentPath.value}/${file.name}`

  // Use the resumable upload protocol for all files.
  // The legacy writeFile (JSON/base64) interface is capped at ~1MB server-side,
  // so even moderate files fail with 500. Chunked streaming handles any size.
  const CHUNK_SIZE = 4 * 1024 * 1024 // 4 MiB per chunk
  let sessionId: string | null = null

  try {
    // 1. Create an upload session; server allocates a temp file.
    const session = await createUpload(fullPath, file.size)
    sessionId = session.sessionId

    // 2. Stream the file in chunks via PUT with Content-Range.
    let offset = session.receivedBytes || 0
    while (offset < file.size) {
      const end = Math.min(offset + CHUNK_SIZE, file.size)
      const chunk = file.slice(offset, end)
      const updated = await appendUpload(sessionId, chunk, offset, file.size)
      offset = updated.receivedBytes
    }

    // 3. Finalize — atomically moves the temp file to the target path.
    await finalizeUpload(sessionId)
    sessionId = null // session consumed; no abort needed

    message.success(t('files.uploadSuccess'))
    fetchDir()
  } catch (err) {
    // Abort the session on failure so the server reclaims the temp file.
    if (sessionId) {
      try { await abortUpload(sessionId) } catch { /* ignore */ }
    }
    message.error(t('files.uploadFailed'))
    // eslint-disable-next-line no-console
    console.error('Upload failed:', err)
  } finally {
    uploading.value = false
    uploadFileName.value = ''
    input.value = ''
  }
}

// ---- Preview ----
const showPreview = ref(false)
const previewEntry = ref<ManagedFileEntry | null>(null)
const previewContent = ref('')
const previewDataUrl = ref('')
const previewLoading = ref(false)

async function openPreview(entry: ManagedFileEntry) {
  previewEntry.value = entry
  previewContent.value = ''
  previewDataUrl.value = ''
  showPreview.value = true

  if (isImage(entry.name)) {
    previewLoading.value = true
    try {
      const result = await readFileContent(entry.path, 'base64')
      previewDataUrl.value = `data:${mimeType(entry.name)};base64,${result.content}`
    } catch { previewDataUrl.value = '' }
    finally { previewLoading.value = false }
  } else if (isText(entry.name)) {
    previewLoading.value = true
    try {
      const result = await readFileContent(entry.path, 'text')
      previewContent.value = result.content
    } catch { previewContent.value = '' }
    finally { previewLoading.value = false }
  } else if (isVideo(entry.name)) {
    // Browsers cannot attach a JWT to a raw <video src>. Fetch the blob with
    // an Authorization header and create an object URL so <video> can play it.
    // The server returns Content-Type: application/octet-stream for downloads,
    // so we must re-wrap the blob with the correct video MIME type, otherwise
    // <video> refuses to play an octet-stream blob.
    previewLoading.value = true
    try {
      const raw = await downloadBlob(entry.path)
      const blob = new Blob([raw], { type: videoMimeType(entry.name) })
      previewDataUrl.value = URL.createObjectURL(blob)
    } catch { previewDataUrl.value = '' }
    finally { previewLoading.value = false }
  }
}

function mimeType(name: string): string {
  const e = ext(name)
  const map: Record<string, string> = {
    '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.png': 'image/png',
    '.gif': 'image/gif', '.webp': 'image/webp', '.bmp': 'image/bmp',
    '.svg': 'image/svg+xml',
  }
  return map[e] ?? 'application/octet-stream'
}

function videoMimeType(name: string): string {
  const e = ext(name)
  const map: Record<string, string> = {
    '.mp4': 'video/mp4',
    '.webm': 'video/webm',
    '.ogv': 'video/ogg',
    '.mov': 'video/quicktime',
    '.mkv': 'video/x-matroska',
    '.avi': 'video/x-msvideo',
    '.m4v': 'video/mp4',
    '.ts': 'video/mp2t',
  }
  return map[e] ?? 'video/mp4'
}

function closePreview() {
  showPreview.value = false
  previewEntry.value = null
  previewContent.value = ''
  // Revoke object URLs created for video preview to free memory.
  if (previewDataUrl.value && previewDataUrl.value.startsWith('blob:')) {
    URL.revokeObjectURL(previewDataUrl.value)
  }
  previewDataUrl.value = ''
}

// ---- Text save ----
const savingText = ref(false)
async function handleSaveText() {
  if (!previewEntry.value) return
  savingText.value = true
  try {
    await updateFile(previewEntry.value.path, previewContent.value)
    message.success(t('files.saveSuccess'))
    fetchDir()
  } catch {
    message.error(t('files.saveFailed'))
  } finally {
    savingText.value = false
  }
}

// ---- Table columns (list view) ----
const columns: DataTableColumns<ManagedFileEntry> = [
  {
    title: () => t('files.fileName'), key: 'name', ellipsis: { tooltip: true },
    render: (r) => h('span', {
      style: { cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px' },
      onClick: () => handleRowClick(r),
      ondblclick: () => r.isDirectory ? navigateTo(r.path) : openPreview(r),
    }, [
      h(NTag, {
        type: r.isDirectory ? 'info' : 'default',
        size: 'tiny',
        round: true,
      }, { default: () => fileTypeLabel(r.name, r.isDirectory) }),
      r.name,
    ]),
  },
  {
    title: () => t('files.fileSize'), key: 'sizeBytes', width: 90,
    render: (r) => r.isDirectory ? '—' : formatBytes(r.sizeBytes ?? 0),
  },
  {
    title: () => t('files.fileType'), key: 'isDirectory', width: 80,
    render: (r) => r.isDirectory ? t('files.folder') : ext(r.name).slice(1).toUpperCase() || '—',
  },
  {
    title: () => t('files.modifiedAt'), key: 'modifiedAt', width: 170,
    render: (r) => r.modifiedAt ? formatDateTime(r.modifiedAt) : t('common.unknown'),
  },
  {
    title: () => t('common.actions'), key: 'actions', width: 200,
    render: (r) => h('div', { style: { display: 'flex', gap: '4px', flexWrap: 'wrap' } }, [
      h(NButton, {
        size: 'tiny', type: 'primary', secondary: true,
        onClick: () => r.isDirectory ? navigateTo(r.path) : openPreview(r),
      }, { default: () => r.isDirectory ? t('files.folder') : t('files.preview') }),
      h(NButton, { size: 'tiny', onClick: () => openRename(r) }, { default: () => t('files.renameFile') }),
      h(NButton, { size: 'tiny', onClick: () => openMove(r) }, { default: () => t('files.moveFile') }),
      h(NButton, {
        size: 'tiny', disabled: r.isDirectory,
        onClick: () => openExternalDownload(r),
      }, { default: () => t('files.download') }),
      h(NButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => confirmDelete(r) }, { default: () => t('common.delete') }),
    ]),
  },
]
</script>

<template>
  <div class="zs-files-page">
    <!-- Breadcrumb + actions toolbar -->
    <div class="zs-files-toolbar">
      <div class="zs-files-breadcrumb">
        <button class="zs-icon-btn" :disabled="parentPath === null" @click="goUp" :title="t('files.goBack')">
          <NIconComp size="18"><ArrowUpOutline /></NIconComp>
        </button>
        <div class="zs-breadcrumb-segments">
          <template v-for="(crumb, i) in breadcrumbs" :key="crumb.path">
            <span v-if="i > 0" class="zs-breadcrumb-sep">/</span>
            <button
              class="zs-breadcrumb-btn"
              :class="{ 'zs-breadcrumb-btn--active': crumb.path === currentPath }"
              @click="navigateTo(crumb.path)"
            >{{ crumb.label }}</button>
          </template>
        </div>
      </div>

      <div class="zs-files-actions">
        <!-- View mode toggle -->
        <div class="zs-view-toggle">
          <button class="zs-icon-btn" :class="{ 'zs-icon-btn--active': viewMode === 'list' }" @click="viewMode = 'list'" title="List">
            <NIconComp size="16"><ListOutline /></NIconComp>
          </button>
          <button class="zs-icon-btn" :class="{ 'zs-icon-btn--active': viewMode === 'grid' }" @click="viewMode = 'grid'" title="Grid">
            <NIconComp size="16"><GridOutline /></NIconComp>
          </button>
        </div>

        <NButton size="small" @click="showCreateFolder = true">
          <template #icon><NIconComp size="16"><CreateOutline /></NIconComp></template>
          {{ t('files.createFolder') }}
        </NButton>
        <NButton size="small" @click="showCreateFile = true">
          <template #icon><NIconComp size="16"><DocumentTextOutline /></NIconComp></template>
          {{ t('files.createFile') }}
        </NButton>
        <NButton size="small" type="primary" :loading="uploading" @click="triggerUpload">
          <template #icon><NIconComp size="16"><CloudUploadOutline /></NIconComp></template>
          {{ uploading ? `${uploadFileName}…` : t('files.uploadFile') }}
        </NButton>
        <input ref="uploadTarget" type="file" style="display:none" @change="handleFileSelected" />

        <NButton size="small" quaternary :loading="loading" @click="fetchDir">
          <template #icon><NIconComp size="16"><RefreshOutline /></NIconComp></template>
        </NButton>
      </div>
    </div>

    <!-- File listing: Grid view -->
    <div v-if="viewMode === 'grid' && entries.length" class="zs-file-grid">
      <div
        v-for="entry in entries" :key="entry.path"
        class="zs-file-card"
        @dblclick="entry.isDirectory ? navigateTo(entry.path) : openPreview(entry)"
        @click="handleRowClick(entry)"
      >
        <div class="zs-file-card-icon" :class="fileIconBg(entry.name, entry.isDirectory)">
          <span class="zs-file-card-emoji">{{ fileTypeIcon(entry.name, entry.isDirectory) }}</span>
        </div>
        <div class="zs-file-card-name" :title="entry.name">{{ entry.name }}</div>
        <div class="zs-file-card-meta">
          {{ entry.isDirectory ? t('files.folder') : formatBytes(entry.sizeBytes ?? 0) }}
        </div>
        <div class="zs-file-card-actions">
          <NButton size="tiny" quaternary @click.stop="openRename(entry)">✏️</NButton>
          <NButton size="tiny" quaternary @click.stop="confirmDelete(entry)">🗑️</NButton>
        </div>
      </div>
    </div>

    <!-- File listing: List view (table) -->
    <NCard v-else-if="viewMode === 'list'" :bordered="true" size="small" class="zs-files-card">
      <NDataTable
        v-if="entries.length"
        :columns="columns" :data="entries"
        :bordered="false" size="small" striped
        :loading="loading" :max-height="600"
      />
      <EmptyState v-else :message="t('files.noFiles')" :description="t('files.noFilesHint')" />
    </NCard>

    <!-- Empty state for grid -->
    <EmptyState v-if="viewMode === 'grid' && !entries.length && !loading" :message="t('files.noFiles')" :description="t('files.noFilesHint')" />

    <!-- Create Folder modal -->
    <NModal v-model:show="showCreateFolder" preset="card" :title="t('files.createFolder')" style="width: 420px">
      <NForm label-placement="left" label-width="80">
        <NFormItem :label="t('files.folderName')" required>
          <NInput v-model:value="newFolderName" :placeholder="t('files.folderNamePlaceholder')" @keyup.enter="handleCreateFolder" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showCreateFolder = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="creatingFolder" @click="handleCreateFolder" style="margin-left:12px">{{ t('common.create') }}</NButton>
      </template>
    </NModal>

    <!-- Create File modal -->
    <NModal v-model:show="showCreateFile" preset="card" :title="t('files.createFile')" style="width: 420px">
      <NForm label-placement="left" label-width="80">
        <NFormItem :label="t('files.fileName')" required>
          <NInput v-model:value="newFileName" placeholder="e.g. notes.txt" @keyup.enter="handleCreateFile" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showCreateFile = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="creatingFile" @click="handleCreateFile" style="margin-left:12px">{{ t('common.create') }}</NButton>
      </template>
    </NModal>

    <!-- Rename modal -->
    <NModal v-model:show="showRename" preset="card" :title="t('files.renameFile')" style="width: 420px">
      <NForm label-placement="left" label-width="80">
        <NFormItem :label="t('files.fileName')">
          <NInput :value="renameTarget?.name ?? ''" disabled />
        </NFormItem>
        <NFormItem :label="t('files.newName')" required>
          <NInput v-model:value="renameNewName" :placeholder="t('files.newNamePlaceholder')" @keyup.enter="handleRename" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showRename = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="renaming" @click="handleRename" style="margin-left:12px">{{ t('common.save') }}</NButton>
      </template>
    </NModal>

    <!-- Move modal -->
    <NModal v-model:show="showMove" preset="card" :title="t('files.moveFile')" style="width: 420px">
      <NForm label-placement="left" label-width="80">
        <NFormItem :label="t('files.fileName')">
          <NInput :value="moveTarget?.name ?? ''" disabled />
        </NFormItem>
        <NFormItem :label="t('files.destinationPath')" required>
          <NInput v-model:value="moveDestPath" :placeholder="t('files.destPlaceholder')" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NButton @click="showMove = false">{{ t('common.cancel') }}</NButton>
        <NButton type="primary" :loading="moving" @click="handleMove" style="margin-left:12px">{{ t('files.moveFile') }}</NButton>
      </template>
    </NModal>

    <!-- Preview modal -->
    <NModal
      v-model:show="showPreview"
      preset="card"
      style="width: 1100px; max-width: 96vw"
      :title="previewEntry?.name ?? ''"
      @update:show="!$event && closePreview()"
    >
      <!-- Image preview -->
      <div v-if="previewEntry && isImage(previewEntry.name)" class="preview-media">
        <NSpin v-if="previewLoading" />
        <img
          v-else-if="previewDataUrl"
          :src="previewDataUrl"
          :alt="previewEntry.name"
          style="max-width:100%;max-height:80vh;object-fit:contain"
          @error="($event.target as HTMLImageElement).style.display='none'"
        />
        <EmptyState v-else :message="t('common.loading')" />
      </div>

      <!-- Video preview -->
      <div v-else-if="previewEntry && isVideo(previewEntry.name)" class="preview-media">
        <NSpin v-if="previewLoading" />
        <video
          v-else-if="previewDataUrl"
          :src="previewDataUrl"
          controls
          autoplay
          style="width:100%;max-height:80vh"
        >
          {{ t('files.unsupportedPreview') }}
        </video>
        <EmptyState v-else :message="t('common.loading')" />
      </div>

      <!-- Text editor -->
      <div v-else-if="previewEntry && isText(previewEntry.name)">
        <NSpin v-if="previewLoading" />
        <div v-else>
          <NInput
            type="textarea"
            :value="previewContent"
            @update:value="(v: string) => previewContent = v"
            :rows="20"
            :placeholder="t('files.fileContent')"
          />
          <NSpace style="margin-top: 12px" justify="end">
            <NButton @click="closePreview">{{ t('files.closePreview') }}</NButton>
            <NButton type="primary" :loading="savingText" @click="handleSaveText">{{ t('common.save') }}</NButton>
          </NSpace>
        </div>
      </div>

      <!-- Unsupported preview -->
      <div v-else style="text-align:center;padding:40px;color:var(--zs-text-tertiary)">
        <p>{{ t('files.unsupportedPreview') }}</p>
        <NButton type="primary" :loading="downloading" style="margin-top: 12px" @click="openExternalDownload(previewEntry)">
          {{ t('files.download') }}
        </NButton>
      </div>
    </NModal>
  </div>
</template>

<script lang="ts">
import { NForm, NFormItem, NInput, NSpace, NModal } from 'naive-ui'
import {
  ArrowUpOutline, CreateOutline, DocumentTextOutline, CloudUploadOutline,
  RefreshOutline, ListOutline, GridOutline,
} from '@vicons/ionicons5'
</script>

<style scoped>
.zs-files-page {
  max-width: 1400px;
  margin: 0 auto;
}

/* ---- Toolbar ---- */
.zs-files-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
  padding: 12px 16px;
  background: var(--zs-bg-card);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius-lg);
  margin-bottom: 16px;
}
.zs-files-breadcrumb {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.zs-breadcrumb-segments {
  display: flex;
  align-items: center;
  gap: 2px;
  overflow-x: auto;
  white-space: nowrap;
}
.zs-breadcrumb-sep {
  color: var(--zs-text-tertiary);
  font-size: 14px;
  padding: 0 2px;
  user-select: none;
}
.zs-breadcrumb-btn {
  background: none;
  border: none;
  color: var(--zs-text-secondary);
  font-size: 13px;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 6px;
  transition: all var(--zs-transition);
  white-space: nowrap;
}
.zs-breadcrumb-btn:hover {
  background: var(--zs-bg-input);
  color: var(--zs-text-primary);
}
.zs-breadcrumb-btn--active {
  color: var(--zs-primary) !important;
  font-weight: 600;
}

.zs-files-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}
.zs-view-toggle {
  display: flex;
  gap: 2px;
  margin-right: 6px;
  padding-right: 8px;
  border-right: 1px solid var(--zs-border);
}

/* ---- Card wrapper ---- */
.zs-files-card {
  border-radius: var(--zs-radius-lg) !important;
  border-color: var(--zs-border) !important;
}

/* ---- Grid view ---- */
.zs-file-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 12px;
}
.zs-file-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 16px 10px 12px;
  border-radius: var(--zs-radius);
  border: 1px solid var(--zs-border);
  background: var(--zs-bg-card);
  cursor: pointer;
  transition: all var(--zs-transition);
  text-align: center;
  position: relative;
}
.zs-file-card:hover {
  background: var(--zs-bg-card-hover);
  border-color: var(--zs-primary);
  box-shadow: var(--zs-shadow);
}
.zs-file-card:hover .zs-file-card-actions {
  opacity: 1;
}
.zs-file-card-icon {
  width: 60px;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 14px;
  margin-bottom: 8px;
  font-size: 30px;
}
.zs-file-card-icon.bg-blue { background: rgba(74,144,217,0.12); }
.zs-file-card-icon.bg-pink { background: rgba(236,72,153,0.12); }
.zs-file-card-icon.bg-purple { background: rgba(139,92,246,0.12); }
.zs-file-card-icon.bg-teal { background: rgba(20,184,166,0.12); }
.zs-file-card-icon.bg-gray { background: rgba(100,116,139,0.12); }

.zs-file-card-emoji { line-height: 1; }
.zs-file-card-name {
  font-size: 12px;
  color: var(--zs-text-primary);
  line-height: 1.4;
  word-break: break-all;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  width: 100%;
}
.zs-file-card-meta {
  font-size: 11px;
  color: var(--zs-text-tertiary);
  margin-top: 4px;
}
.zs-file-card-actions {
  position: absolute;
  top: 6px;
  right: 6px;
  display: flex;
  gap: 2px;
  opacity: 0;
  transition: opacity var(--zs-transition);
}

/* ---- Preview media ---- */
.preview-media {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 200px;
  background: #000;
  border-radius: 6px;
  overflow: hidden;
}
</style>
