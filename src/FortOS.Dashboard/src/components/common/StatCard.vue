<!--
  FortOS Dashboard — JiSpace-style Stat Card
  A compact metric card with colored accent border, icon, value, and label.
-->
<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{
  label: string
  value: string | number
  unit?: string
  subtitle?: string
  icon?: Component
  color?: string
}>()
</script>

<template>
  <div class="zs-stat-card">
    <div class="zs-stat-card-inner">
      <div class="zs-stat-header">
        <span class="zs-stat-label">{{ label }}</span>
        <NIcon v-if="icon" size="20" :color="color ?? 'var(--zs-text-tertiary)'">
          <component :is="icon" />
        </NIcon>
      </div>
      <div class="zs-stat-body">
        <span class="zs-stat-value" :style="color ? { color } : {}">
          {{ value }}
        </span>
        <span v-if="unit" class="zs-stat-unit">{{ unit }}</span>
      </div>
      <div v-if="subtitle" class="zs-stat-subtitle">{{ subtitle }}</div>
    </div>
  </div>
</template>

<style scoped>
.zs-stat-card {
  background: var(--zs-bg-card);
  border: 1px solid var(--zs-border);
  border-radius: var(--zs-radius-lg);
  transition: all var(--zs-transition);
  overflow: hidden;
  position: relative;
}
.zs-stat-card::before {
  content: '';
  position: absolute;
  left: 0;
  top: 12px;
  bottom: 12px;
  width: 3px;
  border-radius: 0 3px 3px 0;
  background: v-bind(color ?? 'var(--zs-primary)');
}
.zs-stat-card:hover {
  border-color: var(--zs-border-light);
  background: var(--zs-bg-card-hover);
}
.zs-stat-card-inner {
  padding: 14px 16px 14px 20px;
}
.zs-stat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
}
.zs-stat-label {
  font-size: 11px;
  font-weight: 600;
  color: var(--zs-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.zs-stat-body {
  display: flex;
  align-items: baseline;
  gap: 4px;
}
.zs-stat-value {
  font-size: 24px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--zs-text-primary);
  line-height: 1.2;
}
.zs-stat-unit {
  font-size: 12px;
  color: var(--zs-text-tertiary);
  font-weight: 500;
}
.zs-stat-subtitle {
  font-size: 11px;
  color: var(--zs-text-tertiary);
  margin-top: 4px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
