<!--
  GORT Dashboard — 极空间-style Application Shell
  Slim icon sidebar + top header with stat pills + scrollable content area.
-->
<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useThemeStore } from '@/stores/theme'
import { useI18n } from 'vue-i18n'
import AppSidebar from './AppSidebar.vue'
import AppHeader from './AppHeader.vue'

const route = useRoute()
const theme = useThemeStore()
const { t } = useI18n()
const collapsed = ref(false)

/** Page title — prefer explicit route meta, fall back to nav label, then 'GORT'. */
const pageTitle = computed(() => {
  const metaTitle = route.meta.title as string | undefined
  if (metaTitle) return metaTitle
  const routeName = String(route.name ?? '')
  const navMap: Record<string, string> = {
    Dashboard: 'nav.dashboard',
    Files: 'nav.files',
    Storage: 'nav.storage',
    Sharing: 'nav.sharing',
    Backup: 'nav.backup',
    Snapshots: 'nav.snapshots',
    Agents: 'nav.agents',
    Network: 'nav.network',
    Services: 'nav.services',
    Logs: 'nav.logs',
    Alerts: 'nav.alerts',
    Settings: 'nav.settings',
  }
  return navMap[routeName] ? t(navMap[routeName]) : 'GORT'
})

function toggleCollapsed() {
  collapsed.value = !collapsed.value
}

/** Keep the browser tab title in sync with the current page. */
watch(pageTitle, (title) => {
  document.title = `${title} — GORT`
}, { immediate: true })
</script>

<template>
  <div class="zs-shell">
    <!-- Slim icon sidebar -->
    <AppSidebar :collapsed="collapsed" />

    <!-- Main area: header + content -->
    <div class="zs-main">
      <!-- Top header bar -->
      <AppHeader
        :title="pageTitle"
        :collapsed="collapsed"
        @toggle="toggleCollapsed"
      />

      <!-- Page content — scrollable, with zspace background -->
      <main class="zs-content">
        <router-view v-slot="{ Component }">
          <transition name="zs-fade" mode="out-in">
            <component :is="Component" />
          </transition>
        </router-view>
      </main>
    </div>
  </div>
</template>

<style scoped>
.zs-shell {
  display: flex;
  height: 100vh;
  width: 100vw;
  overflow: hidden;
}

.zs-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  height: 100vh;
}

.zs-content {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 20px;
  background: var(--zs-bg-body);
}

/* Transition */
.zs-fade-enter-active,
.zs-fade-leave-active {
  transition: opacity 0.15s ease;
}
.zs-fade-enter-from,
.zs-fade-leave-to {
  opacity: 0;
}
</style>
