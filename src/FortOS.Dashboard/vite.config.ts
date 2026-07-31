import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'node:path'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { NaiveUiResolver } from 'unplugin-vue-components/resolvers'

// FortOS Dashboard — Vite build configuration.
// Output goes into the API project's wwwroot so ASP.NET serves the SPA directly.
export default defineConfig({
  plugins: [
    vue(),
    // Auto-import Vue APIs (ref, computed, watch, etc.) — eliminates repetitive imports.
    AutoImport({
      imports: [
        'vue',
        'vue-router',
        'pinia',
        {
          'naive-ui': ['useDialog', 'useMessage', 'useNotification', 'useLoadingBar'],
          'vue-i18n': ['useI18n'],
        },
      ],
      dts: 'src/auto-imports.d.ts',
    }),
    // Auto-register Naive UI components — no manual registration needed.
    Components({
      resolvers: [NaiveUiResolver()],
      dts: 'src/components.d.ts',
    }),
  ],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },
  // When building, output to the API's wwwroot/dashboard for built-in static-file hosting.
  build: {
    outDir: resolve(__dirname, '../FortOS.Api/wwwroot/dashboard'),
    emptyOutDir: true,
    sourcemap: false,
  },
  server: {
    port: 5173,
    // Proxy API calls to the FortOS backend during development.
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})
