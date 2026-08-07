<!--
  FortOS Dashboard — JiSpace-style Error Boundary
  Catches unhandled errors in child component subtrees via
  Vue's onErrorCaptured hook and shows a fallback UI.
-->
<script setup lang="ts">
import { ref, onErrorCaptured } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const showDetails = ref(false)
const error = ref<Error | null>(null)
const errorSource = ref<string>('')

onErrorCaptured((err: Error, _instance, info) => {
  console.error('[ErrorBoundary] Captured error:', err, info)
  error.value = err
  errorSource.value = info ?? ''
  return false
})

function dismiss() {
  error.value = null
  errorSource.value = ''
  window.location.reload()
}
</script>

<template>
  <div v-if="error" class="zs-error">
    <div class="zs-error-inner">
      <div class="zs-error-icon">
        <NIcon size="48" color="#f87171">
          <AlertCircleOutline />
        </NIcon>
      </div>
      <h3 class="zs-error-title">{{ t('errorBoundary.title') }}</h3>
      <p class="zs-error-msg">{{ error.message }}</p>

      <div v-if="showDetails" class="zs-error-detail">
        <pre>{{ error.stack }}</pre>
        <p v-if="errorSource" class="zs-error-source">Source: {{ errorSource }}</p>
      </div>

      <div class="zs-error-actions">
        <NButton type="primary" size="small" @click="dismiss">
          {{ t('errorBoundary.reloadPage') }}
        </NButton>
        <NButton size="small" @click="showDetails = !showDetails">
          {{ showDetails ? t('errorBoundary.hideDetails') : t('errorBoundary.showDetails') }}
        </NButton>
      </div>
    </div>
  </div>
  <slot v-else />
</template>

<script lang="ts">
import { AlertCircleOutline } from '@vicons/ionicons5'
</script>

<style scoped>
.zs-error {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 360px;
  padding: 40px;
}
.zs-error-inner {
  text-align: center;
  max-width: 520px;
}
.zs-error-icon {
  margin-bottom: 16px;
}
.zs-error-title {
  font-size: 20px;
  font-weight: 600;
  margin: 0 0 8px 0;
  color: var(--zs-text-primary);
}
.zs-error-msg {
  font-size: 14px;
  color: #f87171;
  margin: 0 0 16px 0;
  word-break: break-word;
}
.zs-error-detail {
  text-align: left;
  background: var(--zs-bg-input);
  border: 1px solid var(--zs-border);
  border-radius: 6px;
  padding: 12px;
  margin-bottom: 16px;
  max-height: 240px;
  overflow-y: auto;
}
.zs-error-detail pre {
  margin: 0;
  font-size: 11px;
  font-family: 'Cascadia Code', 'Fira Code', monospace;
  color: var(--zs-text-secondary);
  white-space: pre-wrap;
  word-break: break-word;
}
.zs-error-source {
  font-size: 12px;
  color: var(--zs-text-tertiary);
  margin: 8px 0 0 0;
}
.zs-error-actions {
  display: flex;
  gap: 8px;
  justify-content: center;
}
</style>
