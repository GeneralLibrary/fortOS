<!--
  FortOS Dashboard — Root Application Component
  Handles auth initialization, dynamic theme/locale switching,
  and rendering the login vs. main layout.
-->
<script setup lang="ts">
import { onMounted, watch, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useThemeStore } from '@/stores/theme'
import ErrorBoundary from '@/components/common/ErrorBoundary.vue'
import {
  NConfigProvider,
  NLoadingBarProvider,
  NMessageProvider,
  NNotificationProvider,
  NDialogProvider,
} from 'naive-ui'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const theme = useThemeStore()
const { locale: i18nLocale } = useI18n()

/** Sync i18n locale with the persisted theme-store locale on startup. */
i18nLocale.value = theme.locale

/** Keep zspace-light class and i18n in sync with the theme store. */
watch(() => theme.locale, (loc) => {
  i18nLocale.value = loc
})

/** Toggle zspace-light class on <html> for the zspace.css light-mode overrides. */
watch(() => theme.isDark, (dark) => {
  document.documentElement.classList.toggle('zspace-light', !dark)
}, { immediate: true })

/** Per-theme Naive UI overrides — tuned to match the JiSpace design palette. */
const themeOverrides = computed(() => ({
  common: {
    primaryColor: '#4a90d9',
    primaryColorHover: '#6ba8e8',
    primaryColorPressed: '#3a7bc8',
    primaryColorSuppl: '#4a90d9',
    ...(theme.isDark ? {
      bodyColor: '#0f1117',
      cardColor: '#1a1d24',
      modalColor: '#1a1d24',
      popoverColor: '#1e2130',
      inputColor: '#1e2130',
      tableColor: '#1a1d24',
      dividerColor: '#2a2d35',
      borderColor: '#2a2d35',
    } : {
      bodyColor: '#f0f2f5',
      cardColor: '#ffffff',
      modalColor: '#ffffff',
      popoverColor: '#ffffff',
      inputColor: '#f5f6f8',
      tableColor: '#ffffff',
      dividerColor: '#e8eaed',
      borderColor: '#e8eaed',
    }),
  },
} as const))

// On mount, try to initialize auth from persisted token.
onMounted(async () => {
  const ok = await auth.initialize()
  if (!ok && route.path !== '/login') {
    router.replace('/login')
  }
})

// Watch auth state — redirect to login when session expires.
watch(() => auth.isAuthenticated, (val) => {
  if (!val && route.path !== '/login') {
    router.replace('/login')
  }
})
</script>

<template>
  <NConfigProvider
    :theme="theme.naiveTheme"
    :theme-overrides="themeOverrides"
    :locale="theme.naiveLocale"
    :date-locale="theme.naiveDateLocale"
  >
    <NLoadingBarProvider>
      <NMessageProvider>
        <NNotificationProvider>
          <NDialogProvider>
            <ErrorBoundary>
              <router-view />
            </ErrorBoundary>
          </NDialogProvider>
        </NNotificationProvider>
      </NMessageProvider>
    </NLoadingBarProvider>
  </NConfigProvider>
</template>
