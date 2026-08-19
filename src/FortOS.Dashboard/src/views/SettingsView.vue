<!--
  FortOS Dashboard — System Settings View
  Categorized, semantically-labelled settings with graphical controls.
  The category + entry metadata comes from GET /api/config/meta; every
  whitelisted entry is rendered — keys absent from the live config fall
  back to their metadata default value and are written on save.
-->
<script setup lang="ts">
import { computed, onMounted, reactive, ref, type Component } from 'vue'
import {
  NIcon,
  NTag,
  NInput,
  NInputNumber,
  NSelect,
  NSwitch,
} from 'naive-ui'
import {
  ShieldCheckmarkOutline,
  SpeedometerOutline,
  PulseOutline,
  OptionsOutline,
  SearchOutline,
  CheckmarkOutline,
  ArrowUndoOutline,
  RefreshOutline,
} from '@vicons/ionicons5'
import { useSettingsStore } from '@/stores/settings'
import { useI18n } from 'vue-i18n'
import { useMessage } from 'naive-ui'
import PageHeader from '@/components/common/PageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import type { ConfigCategoryMeta, ConfigEntryMeta } from '@/types'

const store = useSettingsStore()
const message = useMessage()
const { t } = useI18n()

const activeCategory = ref('')
const search = ref('')
const saving = ref(false)

/** Pending edits keyed by config key, in the string form the API stores. */
const drafts = reactive<Record<string, string>>({})

/** Icon lookup for backend category `icon` identifiers. */
const categoryIcons: Record<string, Component> = {
  security: ShieldCheckmarkOutline,
  access: SpeedometerOutline,
  observability: PulseOutline,
  advanced: OptionsOutline,
}

onMounted(async () => {
  await store.load()
  // Use the filtered list (storage category lives on the Storage page), so the
  // initial category is always one that is actually rendered — even if the
  // backend were to reorder storage to the front.
  if (!activeCategory.value && categories.value.length) {
    activeCategory.value = categories.value[0].id
  }
})

const categories = computed(() =>
  // The storage category is a pure operations panel (RAID create/init), not a
  // config-key category — it moved to the Storage page (StorageView) per issue #16.
  [...store.categories].filter(c => c.id !== 'storage').sort((a, b) => a.order - b.order),
)

const activeCat = computed<ConfigCategoryMeta | undefined>(() =>
  categories.value.find(c => c.id === activeCategory.value),
)

/** Number of config keys rendered for a category (all whitelisted entries). */
function countFor(categoryId: string): number {
  return store.entries.filter(e => e.category === categoryId).length
}

/** All whitelisted entries of the active category (drives save/reset/dirty state). */
const categoryEntries = computed<ConfigEntryMeta[]>(() =>
  store.entries
    .filter(e => e.category === activeCategory.value)
    .sort((a, b) => a.order - b.order),
)

/** Entries of the active category, search-filtered. Keys absent from the live
    config are still rendered (draft falls back to the metadata default). */
const activeEntries = computed<ConfigEntryMeta[]>(() => {
  const query = search.value.trim().toLowerCase()
  return categoryEntries.value
    .filter(e => !query || matches(e, query))
})

/** Match by label, description, raw key or the current (draft) value. */
function matches(entry: ConfigEntryMeta, query: string): boolean {
  return labelFor(entry).toLowerCase().includes(query)
    || descriptionFor(entry).toLowerCase().includes(query)
    || entry.key.toLowerCase().includes(query)
    || draftValue(entry).toLowerCase().includes(query)
}

// ---- i18n with backend-metadata fallback ----

function labelFor(entry: ConfigEntryMeta): string {
  const key = `settings.meta.${entry.key}.label`
  const localized = t(key)
  return localized === key ? (entry.label ?? entry.key) : localized
}

function descriptionFor(entry: ConfigEntryMeta): string {
  const key = `settings.meta.${entry.key}.description`
  const localized = t(key)
  return localized === key ? (entry.description ?? '') : localized
}

function categoryName(cat: ConfigCategoryMeta): string {
  const key = `settings.categories.${cat.id}.name`
  const localized = t(key)
  return localized === key ? cat.name : localized
}

function categoryDescription(cat: ConfigCategoryMeta): string {
  const key = `settings.categories.${cat.id}.description`
  const localized = t(key)
  return localized === key ? (cat.description ?? '') : localized
}

// ---- Draft / dirty tracking ----

/** Effective current value: live config wins, else the metadata default ('' if none). */
function originalValue(entry: ConfigEntryMeta): string {
  return store.config[entry.key] ?? entry.defaultValue ?? ''
}

function draftValue(entry: ConfigEntryMeta): string {
  return entry.key in drafts ? drafts[entry.key] : originalValue(entry)
}

function isDirty(entry: ConfigEntryMeta): boolean {
  return entry.key in drafts && drafts[entry.key] !== originalValue(entry)
}

function categoryDirty(entries: ConfigEntryMeta[]): boolean {
  return entries.some(isDirty)
}

function dirtyCount(categoryId: string): number {
  return store.entries.filter(e => e.category === categoryId && isDirty(e)).length
}

function setDraft(entry: ConfigEntryMeta, value: string): void {
  if (value === originalValue(entry)) delete drafts[entry.key]
  else drafts[entry.key] = value
}

function resetEntry(entry: ConfigEntryMeta): void {
  delete drafts[entry.key]
}

function resetCategory(): void {
  categoryEntries.value.forEach(e => delete drafts[e.key])
}

// ---- Control helpers ----

function boolValue(entry: ConfigEntryMeta): boolean {
  return ['true', '1', 'yes', 'on'].includes(draftValue(entry).toLowerCase())
}

function numValue(entry: ConfigEntryMeta): number | null {
  const raw = draftValue(entry)
  const n = Number.parseFloat(raw)
  return Number.isFinite(n) ? n : null
}

function selectOptions(entry: ConfigEntryMeta) {
  return (entry.options ?? []).map(o => ({ label: o, value: o }))
}

// ---- Save ----

async function saveCategory(): Promise<void> {
  const dirty = categoryEntries.value.filter(isDirty)
  if (!dirty.length) return
  saving.value = true
  try {
    await store.setConfigs(dirty.map(e => ({ key: e.key, value: drafts[e.key] })))
    dirty.forEach(e => delete drafts[e.key])
    message.success(t('settings.updateSuccess'))
  } catch {
    message.error(store.error ?? t('settings.updateFailed'))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="settings-page">
    <PageHeader :title="t('settings.title')" :subtitle="t('settings.subtitle')">
      <template #actions>
        <NButton size="small" :loading="store.loading" @click="store.load()">
          <template #icon><NIcon><RefreshOutline /></NIcon></template>
          {{ t('common.refresh') }}
        </NButton>
      </template>
    </PageHeader>

    <div v-if="store.categories.length" class="settings-layout">
      <!-- Semantic category navigation -->
      <aside class="settings-nav">
        <button
          v-for="cat in categories"
          :key="cat.id"
          type="button"
          class="settings-nav-item"
          :class="{ active: cat.id === activeCategory }"
          @click="activeCategory = cat.id"
        >
          <NIcon class="settings-nav-icon">
            <component :is="categoryIcons[cat.icon] ?? OptionsOutline" />
          </NIcon>
          <span class="settings-nav-name">{{ categoryName(cat) }}</span>
          <span v-if="dirtyCount(cat.id)" class="settings-nav-badge">{{ dirtyCount(cat.id) }}</span>
          <span v-else class="settings-nav-count">{{ countFor(cat.id) }}</span>
        </button>
      </aside>

      <!-- Category content -->
      <section v-if="activeCat" class="settings-content">
        <div class="settings-content-head">
          <div>
            <h3 class="settings-content-title">{{ categoryName(activeCat) }}</h3>
            <p class="settings-content-desc">{{ categoryDescription(activeCat) }}</p>
          </div>
          <div class="settings-content-actions">
            <NTag v-if="dirtyCount(activeCategory)" size="small" type="warning" round>
              {{ t('settings.unsavedCount', { count: dirtyCount(activeCategory) }) }}
            </NTag>
            <NButton size="small" :disabled="!categoryDirty(categoryEntries)" @click="resetCategory">
              <template #icon><NIcon><ArrowUndoOutline /></NIcon></template>
              {{ t('settings.resetAll') }}
            </NButton>
            <NButton
              size="small" type="primary" :loading="saving"
              :disabled="!categoryDirty(categoryEntries)"
              @click="saveCategory"
            >
              <template #icon><NIcon><CheckmarkOutline /></NIcon></template>
              {{ t('settings.saveChanges') }}
            </NButton>
          </div>
        </div>

        <!-- Config entries with graphical controls -->
        <template>
          <div class="settings-search">
            <NInput v-model:value="search" :placeholder="t('settings.searchPlaceholder')" clearable>
              <template #prefix><NIcon><SearchOutline /></NIcon></template>
            </NInput>
          </div>

          <div v-if="activeEntries.length" class="settings-list">
            <div
            v-for="entry in activeEntries"
            :key="entry.key"
            class="settings-item"
            :class="{ dirty: isDirty(entry) }"
          >
            <div class="settings-item-info">
              <div class="settings-item-label-row">
                <span class="settings-item-label">{{ labelFor(entry) }}</span>
                <NTag v-if="isDirty(entry)" size="tiny" type="warning" round>
                  {{ t('settings.unsaved') }}
                </NTag>
              </div>
              <p v-if="descriptionFor(entry)" class="settings-item-desc">{{ descriptionFor(entry) }}</p>
              <div class="settings-item-meta">
                <code>{{ entry.key }}</code>
                <span v-if="entry.defaultValue" class="settings-item-default">
                  {{ t('settings.defaultLabel') }} {{ entry.defaultValue }}
                </span>
              </div>
            </div>

            <div class="settings-item-control">
              <NSwitch
                v-if="entry.type === 'boolean'"
                :value="boolValue(entry)"
                @update:value="(v: boolean) => setDraft(entry, String(v))"
              />
              <NInputNumber
                v-else-if="entry.type === 'number'"
                :value="numValue(entry)"
                :min="entry.min ?? undefined"
                :max="entry.max ?? undefined"
                :step="entry.step ?? undefined"
                :show-button="false"
                style="width: 180px"
                @update:value="(v: number | null) => setDraft(entry, v === null ? '' : String(v))"
              />
              <NSelect
                v-else-if="entry.type === 'select'"
                :value="draftValue(entry)"
                :options="selectOptions(entry)"
                style="width: 180px"
                @update:value="(v: string | number | null) => setDraft(entry, String(v ?? ''))"
              />
              <NInput
                v-else-if="entry.type === 'string'"
                :value="draftValue(entry)"
                :placeholder="entry.defaultValue ?? ''"
                style="width: 280px"
                @update:value="(v: string) => setDraft(entry, v)"
              />
              <NInput
                v-else
                :value="draftValue(entry)"
                type="textarea"
                :autosize="{ minRows: 2, maxRows: 5 }"
                style="width: 320px"
                @update:value="(v: string) => setDraft(entry, v)"
              />
              <NButton
                v-if="isDirty(entry)"
                size="tiny" quaternary
                :title="t('settings.reset')"
                @click="resetEntry(entry)"
              >
                <template #icon><NIcon><ArrowUndoOutline /></NIcon></template>
              </NButton>
            </div>
          </div>
        </div>

        <EmptyState
          v-else-if="!store.loading"
          :message="search ? t('settings.noSearchResults') : t('settings.noMeta')"
        />
        </template>
      </section>
    </div>

    <EmptyState v-else-if="!store.loading" :message="t('settings.noConfig')" />
  </div>
</template>

<style scoped>
.settings-page {
  max-width: 1080px;
  margin: 0 auto;
}

/* ---- Layout ---- */
.settings-layout {
  display: grid;
  grid-template-columns: 220px 1fr;
  gap: 16px;
  align-items: start;
}

/* ---- Category navigation ---- */
.settings-nav {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px;
  background: var(--zs-bg-card);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius-lg);
  position: sticky;
  top: calc(var(--zs-header-height) + 16px);
}

.settings-nav-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 10px 12px;
  border: none;
  border-radius: var(--zs-radius);
  background: transparent;
  color: var(--zs-text-secondary);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  text-align: left;
  transition: all var(--zs-transition);
}

.settings-nav-item:hover {
  background: var(--zs-bg-card-hover);
  color: var(--zs-text-primary);
}

.settings-nav-item.active {
  background: var(--zs-primary-bg);
  color: var(--zs-primary);
}

.settings-nav-icon {
  font-size: 18px;
  flex-shrink: 0;
}

.settings-nav-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.settings-nav-count {
  font-size: 11px;
  color: var(--zs-text-tertiary);
  background: var(--zs-bg-input);
  border-radius: 10px;
  padding: 1px 8px;
}

.settings-nav-badge {
  font-size: 11px;
  font-weight: 600;
  color: var(--zs-orange);
  background: rgba(245, 158, 11, 0.12);
  border-radius: 10px;
  padding: 1px 8px;
}

/* ---- Content ---- */
.settings-content-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}

.settings-content-title {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
  color: var(--zs-text-primary);
}

.settings-content-desc {
  margin: 4px 0 0;
  font-size: 12px;
  color: var(--zs-text-tertiary);
}

.settings-content-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}

.settings-search {
  margin-bottom: 14px;
  max-width: 320px;
}

/* ---- Config entry rows (group card, divider-separated, ZSpace style) ---- */
.settings-list {
  display: flex;
  flex-direction: column;
  background: var(--zs-bg-card);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius-lg);
  overflow: hidden;
}

.settings-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 18px;
  border-bottom: 1px solid var(--zs-border);
  transition: background var(--zs-transition), box-shadow var(--zs-transition);
}

.settings-item:last-child {
  border-bottom: none;
}

.settings-item:hover {
  background: var(--zs-bg-card-hover);
}

.settings-item.dirty {
  background: rgba(245, 158, 11, 0.06);
  box-shadow: inset 3px 0 0 var(--zs-orange);
}

.settings-item-info {
  min-width: 0;
  flex: 1;
}

.settings-item-label-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.settings-item-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--zs-text-primary);
}

.settings-item-desc {
  margin: 4px 0 0;
  font-size: 12px;
  color: var(--zs-text-tertiary);
  line-height: 1.5;
}

.settings-item-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 6px;
  font-size: 11px;
}

.settings-item-meta code {
  color: var(--zs-text-tertiary);
  background: var(--zs-bg-input);
  padding: 1px 6px;
  border-radius: 4px;
}

.settings-item-default {
  color: var(--zs-text-tertiary);
}

.settings-item-control {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

@media (max-width: 860px) {
  .settings-layout {
    grid-template-columns: 1fr;
  }
  .settings-nav {
    position: static;
    flex-direction: row;
    overflow-x: auto;
  }
  .settings-item {
    flex-direction: column;
    align-items: stretch;
  }
  .settings-item-control {
    justify-content: flex-end;
  }
}
</style>
