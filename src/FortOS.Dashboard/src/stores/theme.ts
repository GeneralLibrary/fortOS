// ============================================================================
// FortOS Dashboard — Theme & Locale Store
// Persists user preference for dark/light mode and UI language (zh-CN / en-US).
// ============================================================================

import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { darkTheme, enUS, zhCN, dateEnUS, dateZhCN } from 'naive-ui'
import type { NLocale, NDateLocale } from 'naive-ui'

export type ThemeMode = 'dark' | 'light'
export type UILocale = 'zh-CN' | 'en-US'

const THEME_KEY = 'fortos_theme'
const LOCALE_KEY = 'fortos_locale'

/**
 * Manages the current theme (dark/light) and UI language.
 * Both values are persisted to localStorage so the user's preference
 * survives browser restarts.
 */
export const useThemeStore = defineStore('theme', () => {
  // ---- State ----

  const mode = ref<ThemeMode>(loadTheme())
  const locale = ref<UILocale>(loadLocale())

  // ---- Getters ----

  /** Naive UI theme object: darkTheme or null (light). */
  const naiveTheme = computed(() => (mode.value === 'dark' ? darkTheme : null))

  /** Naive UI locale object for component text. */
  const naiveLocale = computed<NLocale>(() => (locale.value === 'zh-CN' ? zhCN : enUS))

  /** Naive UI date locale object for DatePicker etc. */
  const naiveDateLocale = computed<NDateLocale>(() => (locale.value === 'zh-CN' ? dateZhCN : dateEnUS))

  /** True when dark mode is active. */
  const isDark = computed(() => mode.value === 'dark')

  // ---- Actions ----

  /** Toggle between dark and light theme. */
  function toggleTheme() {
    mode.value = mode.value === 'dark' ? 'light' : 'dark'
  }

  /** Set a specific theme mode. */
  function setTheme(m: ThemeMode) {
    mode.value = m
  }

  /** Switch locale. */
  function setLocale(loc: UILocale) {
    locale.value = loc
  }

  // ---- Persistence ----

  watch(mode, (v) => localStorage.setItem(THEME_KEY, v))
  watch(locale, (v) => localStorage.setItem(LOCALE_KEY, v))

  return {
    mode,
    locale,
    naiveTheme,
    naiveLocale,
    naiveDateLocale,
    isDark,
    toggleTheme,
    setTheme,
    setLocale,
  }
})

function loadTheme(): ThemeMode {
  const stored = localStorage.getItem(THEME_KEY)
  if (stored === 'dark' || stored === 'light') return stored
  // Default to dark — FortOS is a NAS appliance UI.
  return 'dark'
}

function loadLocale(): UILocale {
  const stored = localStorage.getItem(LOCALE_KEY)
  if (stored === 'zh-CN' || stored === 'en-US') return stored
  // Detect browser preference, default to zh-CN.
  const nav = navigator.language
  if (nav.startsWith('zh')) return 'zh-CN'
  if (nav.startsWith('en')) return 'en-US'
  return 'zh-CN'
}
