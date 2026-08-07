// ============================================================================
// FortOS Dashboard — Application Entry Point
// Sets up Vue, Pinia, Vue Router, vue-i18n, and Naive UI, then mounts the app.
// ============================================================================

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import i18n from './i18n'

// JiSpace (ZSpace) Design System — global CSS custom properties & primitives.
import './styles/zspace.css'

const app = createApp(App)

// Pinia — global state management.
app.use(createPinia())

// Vue Router — SPA navigation.
app.use(router)

// vue-i18n — internationalization (zh-CN / en-US).
app.use(i18n)

// Naive UI components are auto-imported by unplugin-vue-components; no manual registration needed.

app.mount('#app')
