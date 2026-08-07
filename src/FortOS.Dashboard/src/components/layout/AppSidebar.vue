<!--
  FortOS Dashboard — JiSpace-style Sidebar
  Slim icon bar with gradient accent colors and active state indicators.
-->
<script setup lang="ts">
import { h, computed, type Component } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useThemeStore } from '@/stores/theme'
import { NIcon, NTooltip } from 'naive-ui'
import {
  HomeOutline, ServerOutline, ShareSocialOutline,
  CloudUploadOutline, CameraOutline, CubeOutline,
  WifiOutline, CogOutline, DocumentTextOutline,
  AlertCircleOutline, SettingsOutline, FolderOpenOutline,
} from '@vicons/ionicons5'

defineProps<{ collapsed: boolean }>()

const { t } = useI18n()
const theme = useThemeStore()

interface MenuItem {
  key: string
  i18nKey: string
  icon: Component
  color: string
  gradient: string
}

const menuItems: MenuItem[] = [
  { key: 'Dashboard',  i18nKey: 'nav.dashboard',  icon: HomeOutline,        color: '#4a90d9', gradient: 'linear-gradient(135deg, #4a90d9, #6ba8e8)' },
  { key: 'Files',      i18nKey: 'nav.files',      icon: FolderOpenOutline,  color: '#f59e0b', gradient: 'linear-gradient(135deg, #f59e0b, #fbbf24)' },
  { key: 'Storage',    i18nKey: 'nav.storage',    icon: ServerOutline,      color: '#34c759', gradient: 'linear-gradient(135deg, #34c759, #4ade80)' },
  { key: 'Sharing',    i18nKey: 'nav.sharing',    icon: ShareSocialOutline, color: '#8b5cf6', gradient: 'linear-gradient(135deg, #8b5cf6, #a78bfa)' },
  { key: 'Backup',     i18nKey: 'nav.backup',     icon: CloudUploadOutline, color: '#14b8a6', gradient: 'linear-gradient(135deg, #14b8a6, #2dd4bf)' },
  { key: 'Snapshots',  i18nKey: 'nav.snapshots',  icon: CameraOutline,      color: '#ec4899', gradient: 'linear-gradient(135deg, #ec4899, #f472b6)' },
  { key: 'Agents',     i18nKey: 'nav.agents',     icon: CubeOutline,        color: '#6366f1', gradient: 'linear-gradient(135deg, #6366f1, #818cf8)' },
  { key: 'Network',    i18nKey: 'nav.network',    icon: WifiOutline,        color: '#0ea5e9', gradient: 'linear-gradient(135deg, #0ea5e9, #38bdf8)' },
  { key: 'Services',   i18nKey: 'nav.services',   icon: CogOutline,         color: '#64748b', gradient: 'linear-gradient(135deg, #64748b, #94a3b8)' },
  { key: 'Logs',       i18nKey: 'nav.logs',       icon: DocumentTextOutline,color: '#78716c', gradient: 'linear-gradient(135deg, #78716c, #a8a29e)' },
  { key: 'Alerts',     i18nKey: 'nav.alerts',     icon: AlertCircleOutline, color: '#ef4444', gradient: 'linear-gradient(135deg, #ef4444, #f87171)' },
  { key: 'Settings',   i18nKey: 'nav.settings',   icon: SettingsOutline,    color: '#64748b', gradient: 'linear-gradient(135deg, #64748b, #94a3b8)' },
]

const router = useRouter()
const route = useRoute()

const activeKey = computed(() => String(route.name ?? 'Dashboard'))

function navigate(key: string) {
  router.push({ name: key })
}
</script>

<template>
  <nav class="zs-sidebar" :class="{ 'zs-sidebar--light': !theme.isDark }">
    <!-- Logo -->
    <div class="zs-sidebar-logo" @click="navigate('Dashboard')">
      <div class="zs-logo-icon">
        <NIcon size="26" color="#fff"><ServerOutline /></NIcon>
      </div>
    </div>

    <!-- Menu items fill the remaining sidebar height -->
    <div class="zs-sidebar-menu">
      <NTooltip
        v-for="item in menuItems"
        :key="item.key"
        trigger="hover"
        placement="right"
      >
        <template #trigger>
          <button
            class="zs-nav-item"
            :class="{ 'zs-nav-item--active': activeKey === item.key }"
            @click="navigate(item.key)"
          >
            <div
              class="zs-nav-icon"
              :style="{
                background: activeKey === item.key ? item.gradient : 'transparent',
                color: activeKey === item.key ? '#fff' : item.color,
              }"
            >
              <NIcon size="20"><component :is="item.icon" /></NIcon>
            </div>
            <span class="zs-nav-label">{{ t(item.i18nKey) }}</span>
          </button>
        </template>
        {{ t(item.i18nKey) }}
      </NTooltip>
    </div>
  </nav>
</template>

<style scoped>
.zs-sidebar {
  display: flex;
  flex-direction: column;
  align-items: center;
  width: var(--zs-sidebar-width);
  height: 100%;
  background: var(--zs-bg-sidebar);
  border-right: 1px solid var(--zs-border);
  padding: 0;
  overflow: hidden;
  flex-shrink: 0;
}
.zs-sidebar--light {
  /* light handled by CSS vars */
}

.zs-sidebar-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 56px;
  flex-shrink: 0;
  cursor: pointer;
  border-bottom: 1px solid var(--zs-border);
}
.zs-logo-icon {
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: var(--zs-primary-gradient);
  display: flex;
  align-items: center;
  justify-content: center;
  transition: transform var(--zs-transition);
}
.zs-logo-icon:hover {
  transform: scale(1.08);
}

.zs-sidebar-menu {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 8px 0;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  width: 100%;
}

.zs-nav-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 3px;
  width: 100%;
  /* Stretch nav items to fill the full sidebar height; they share the
     available space evenly and shrink when the window gets shorter. */
  flex: 1 1 0;
  min-height: 52px;
  padding: 6px 4px 4px;
  border: none;
  background: none;
  cursor: pointer;
  transition: all var(--zs-transition);
  position: relative;
  color: var(--zs-text-tertiary);
}
.zs-nav-item:hover {
  color: var(--zs-text-primary);
}
.zs-nav-item--active::before {
  content: '';
  position: absolute;
  left: 0;
  top: 50%;
  transform: translateY(-50%);
  width: 3px;
  height: 24px;
  border-radius: 0 3px 3px 0;
  background: var(--zs-primary-gradient);
}

.zs-nav-icon {
  width: 38px;
  height: 38px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all var(--zs-transition);
}
.zs-nav-item:not(.zs-nav-item--active):hover .zs-nav-icon {
  background: var(--zs-bg-input);
}

.zs-nav-label {
  font-size: 10px;
  line-height: 1;
  white-space: nowrap;
  font-weight: 500;
}
</style>
