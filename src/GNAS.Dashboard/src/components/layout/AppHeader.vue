<!--
  GNAS Dashboard — 极空间-style Top Header Bar
  Page title on the left, system-health stat pills in the center,
  theme/locale/user controls on the right.
-->
<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useThemeStore } from '@/stores/theme'
import { useI18n } from 'vue-i18n'
import { getSystemMetrics } from '@/api/metrics'
import { listAlerts } from '@/api/alerts'
import { formatBytes, formatUptime } from '@/utils/format'
import type { SystemMetricsSnapshot } from '@/types'
import {
  LogOutOutline, PersonOutline,
  SunnyOutline, MoonOutline, LanguageOutline,
  PulseOutline, ServerOutline, SaveOutline, WifiOutline,
} from '@vicons/ionicons5'

defineProps<{
  title: string
  collapsed: boolean
}>()

defineEmits<{
  toggle: []
}>()

const router = useRouter()
const auth = useAuthStore()
const theme = useThemeStore()
const { t, locale } = useI18n()

// ---- Poll system metrics for header stat pills ----

const metrics = ref<SystemMetricsSnapshot | null>(null)
const alertCount = ref(0)
let pollTimer: ReturnType<typeof setInterval> | null = null

async function fetchHeaderMetrics() {
  try {
    const [m, alerts] = await Promise.all([
      getSystemMetrics(),
      listAlerts(),
    ])
    metrics.value = m
    alertCount.value = alerts.length
  } catch { /* header metrics are best-effort; silence failures */ }
}

onMounted(() => {
  fetchHeaderMetrics()
  pollTimer = setInterval(fetchHeaderMetrics, 10_000)
})
onUnmounted(() => {
  if (pollTimer) clearInterval(pollTimer)
})

const cpuPct = computed(() => metrics.value ? Math.round(metrics.value.cpu.usagePercent) : null)
const memUsed = computed(() => metrics.value ? formatBytes(metrics.value.memory.usedBytes) : null)
const memPct = computed(() => metrics.value ? Math.round(metrics.value.memory.usedBytes / metrics.value.memory.totalBytes * 100) : null)
const uptime = computed(() => metrics.value ? formatUptime(metrics.value.host.uptime) : null)

/** Stat-pill color for CPU: green < 50%, orange 50-80%, red > 80% */
const cpuColor = computed(() => {
  if (cpuPct.value == null) return '#64748b'
  if (cpuPct.value >= 80) return '#ef4444'
  if (cpuPct.value >= 50) return '#f59e0b'
  return '#34c759'
})

/** Stat-pill color for memory */
const memColor = computed(() => {
  if (memPct.value == null) return '#64748b'
  if (memPct.value >= 90) return '#ef4444'
  if (memPct.value >= 70) return '#f59e0b'
  return '#34c759'
})

// ---- User & controls ----

const userOptions = [
  {
    label: t('user.logout'),
    key: 'logout',
    icon: () => h(NIcon, null, { default: () => h(LogOutOutline) }),
  },
]

const langOptions = [
  { label: '中文', key: 'zh-CN' },
  { label: 'English', key: 'en-US' },
]

function handleUserSelect(key: string) {
  if (key === 'logout') {
    auth.logout()
    router.replace('/login')
  }
}

function handleToggleTheme() {
  theme.toggleTheme()
}

function handleLangSelect(key: string) {
  locale.value = key
  theme.setLocale(key as 'zh-CN' | 'en-US')
}
</script>

<template>
  <header class="zs-header" :class="{ 'zs-header--light': !theme.isDark }">
    <!-- Left: Page title -->
    <div class="zs-header-left">
      <h1 class="zs-header-title">{{ title }}</h1>
    </div>

    <!-- Center: System-health stat pills -->
    <div class="zs-header-stats">
      <!-- CPU -->
      <div class="zs-stat-pill" v-if="cpuPct !== null">
        <span class="zs-stat-dot" :style="{ background: cpuColor }"></span>
        <NIcon size="14" color="currentColor"><PulseOutline /></NIcon>
        <span>CPU {{ cpuPct }}%</span>
      </div>

      <!-- Memory -->
      <div class="zs-stat-pill" v-if="memUsed !== null">
        <span class="zs-stat-dot" :style="{ background: memColor }"></span>
        <NIcon size="14" color="currentColor"><ServerOutline /></NIcon>
        <span>{{ memUsed }}</span>
      </div>

      <!-- Storage (from metrics if available) -->
      <div class="zs-stat-pill" v-if="metrics?.fileSystems?.length">
        <span class="zs-stat-dot" style="background: #8b5cf6"></span>
        <NIcon size="14" color="currentColor"><SaveOutline /></NIcon>
        <span>{{ metrics.fileSystems.length }} {{ t('dashboard.disks') }}</span>
      </div>

      <!-- Network status -->
      <div class="zs-stat-pill" v-if="metrics?.networks?.length">
        <span class="zs-stat-dot" :style="{ background: metrics.networks.some(n => n.isUp) ? '#34c759' : '#ef4444' }"></span>
        <NIcon size="14" color="currentColor"><WifiOutline /></NIcon>
        <span>{{ metrics.networks.filter(n => n.isUp).length }}/{{ metrics.networks.length }}</span>
      </div>

      <!-- Uptime -->
      <div class="zs-stat-pill" v-if="uptime !== null">
        <span class="zs-stat-dot" style="background: #0ea5e9"></span>
        <span>{{ uptime }}</span>
      </div>
    </div>

    <!-- Right: Controls -->
    <div class="zs-header-right">
      <button class="zs-icon-btn" :class="{ 'zs-icon-btn--active': !theme.isDark }" @click="handleToggleTheme" :title="theme.isDark ? t('theme.light') : t('theme.dark')">
        <NIcon size="18">
          <SunnyOutline v-if="theme.isDark" />
          <MoonOutline v-else />
        </NIcon>
      </button>

      <NDropdown trigger="click" :options="langOptions" @select="handleLangSelect">
        <button class="zs-icon-btn">
          <NIcon size="18"><LanguageOutline /></NIcon>
          <span style="font-size:11px;margin-left:4px;font-weight:500">{{ locale === 'zh-CN' ? '中' : 'EN' }}</span>
        </button>
      </NDropdown>

      <NDropdown trigger="click" :options="userOptions" @select="handleUserSelect">
        <button class="zs-icon-btn">
          <NIcon size="18"><PersonOutline /></NIcon>
          <span style="font-size:11px;margin-left:4px;font-weight:500">{{ auth.username ?? 'Admin' }}</span>
        </button>
      </NDropdown>
    </div>
  </header>
</template>

<script lang="ts">
import { h } from 'vue'
import { NIcon, NDropdown } from 'naive-ui'
</script>

<style scoped>
.zs-header {
  display: flex;
  align-items: center;
  height: var(--zs-header-height);
  padding: 0 16px;
  flex-shrink: 0;
  background: var(--zs-bg-card);
  border-bottom: 1px solid var(--zs-border);
  gap: 12px;
}

.zs-header-left {
  display: flex;
  align-items: center;
  flex-shrink: 0;
}

.zs-header-title {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
  color: var(--zs-text-primary);
  white-space: nowrap;
}

.zs-header-stats {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  overflow: hidden;
}

.zs-header-right {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

@media (max-width: 900px) {
  .zs-header-stats {
    display: none;
  }
}
</style>
