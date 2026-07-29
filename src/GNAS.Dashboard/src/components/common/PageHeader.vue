<!--
  GNAS Dashboard — 极空间-style Page Header
  Consistent page title bar with optional action buttons.
  Uses zspace CSS custom properties for theme-aware colors.
-->
<script setup lang="ts">
defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <div class="zs-page-header">
    <div>
      <h2 class="zs-page-title">{{ title }}</h2>
      <p v-if="subtitle" class="zs-page-subtitle">{{ subtitle }}</p>
    </div>
    <div class="zs-page-actions">
      <slot name="actions" />
    </div>
  </div>
</template>

<style scoped>
.zs-page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 20px;
}
.zs-page-title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--zs-text-primary);
}
.zs-page-subtitle {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--zs-text-tertiary);
}
.zs-page-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}
</style>
