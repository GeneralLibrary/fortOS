<!--
  GORT Dashboard — 极空间-style Empty State
  Displayed when a list/table has no data. Uses NEmpty with theme-aware colors.
-->
<script setup lang="ts">
defineProps<{
  message?: string
  description?: string
}>()
</script>

<template>
  <div class="zs-empty">
    <NEmpty :description="description ?? '暂无数据'">
      <template #extra>
        <span class="zs-empty-msg">{{ message }}</span>
      </template>
    </NEmpty>
  </div>
</template>

<style scoped>
.zs-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 200px;
  padding: 40px 0;
}
.zs-empty-msg {
  font-size: 13px;
  color: var(--zs-text-tertiary);
}
</style>
