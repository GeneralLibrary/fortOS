// ============================================================================
// GORT Dashboard — i18n Setup
// Creates and exports the vue-i18n instance configured with zh-CN and en-US.
// ============================================================================

import { createI18n } from 'vue-i18n'
import zhCN from './locales/zh-CN'
import enUS from './locales/en-US'

/** Locale message type derived from the Chinese locale (primary source). */
export type MessageSchema = typeof zhCN

/**
 * The i18n instance.
 * Defaults to zh-CN; the theme store synchronizes the active locale
 * via the `locale` ref on the i18n instance.
 */
const i18n = createI18n<[MessageSchema], 'zh-CN' | 'en-US'>({
  legacy: false,           // use Composition API mode
  globalInjection: true,   // inject $t() into all component templates
  locale: 'zh-CN',
  fallbackLocale: 'zh-CN',
  messages: {
    'zh-CN': zhCN,
    'en-US': enUS,
  },
})

export default i18n
