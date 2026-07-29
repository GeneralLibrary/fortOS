<!--
  GNAS Dashboard — Login View
  Full-screen login page with username/password authentication.
  Redirects to the dashboard on successful login.
-->
<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useI18n } from 'vue-i18n'
import { ServerOutline, KeyOutline } from '@vicons/ionicons5'
import type { FormInst, FormRules } from 'naive-ui'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const { t } = useI18n()

const formRef = ref<FormInst | null>(null)
const submitting = ref(false)

/** Login form model. */
const model = reactive({
  username: '',
  password: '',
  totp: '',
})

/** Form validation rules — messages come from i18n. */
const rules: FormRules = {
  username: [{ required: true, message: () => t('auth.usernameRequired'), trigger: 'blur' }],
  password: [{ required: true, message: () => t('auth.passwordRequired'), trigger: 'blur' }],
}

/** Submit login form. */
async function handleSubmit() {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }

  submitting.value = true
  try {
    await auth.authenticate({
      username: model.username,
      password: model.password,
      totp: model.totp || undefined,
    })
    const redirect = (route.query.redirect as string) ?? '/'
    router.replace(redirect)
  } catch {
    // Error is stored in authStore.error — displayed via alert.
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <div class="login-card">
      <!-- Header -->
      <div class="login-header">
        <div class="login-logo">
          <NIcon size="40" color="#2080f0">
            <ServerOutline />
          </NIcon>
        </div>
        <h1 class="login-title">GNAS</h1>
        <p class="login-subtitle">{{ t('auth.subtitle') }}</p>
      </div>

      <!-- Error alert -->
      <NAlert
        v-if="auth.error"
        type="error"
        :title="auth.error"
        style="margin-bottom: 16px"
        closable
        @close="auth.error = null"
      />

      <!-- Login form -->
      <NForm ref="formRef" :model="model" :rules="rules" @submit.prevent="handleSubmit">
        <NFormItem path="username" :label="t('auth.username')">
          <NInput
            v-model:value="model.username"
            :placeholder="t('auth.usernamePlaceholder')"
            :disabled="submitting"
            size="large"
            clearable
          />
        </NFormItem>

        <NFormItem path="password" :label="t('auth.password')">
          <NInput
            v-model:value="model.password"
            type="password"
            :placeholder="t('auth.passwordPlaceholder')"
            :disabled="submitting"
            size="large"
            show-password-on="click"
          />
        </NFormItem>

        <NFormItem path="totp" :label="t('auth.totp')">
          <NInput
            v-model:value="model.totp"
            :placeholder="t('auth.totpPlaceholder')"
            :disabled="submitting"
            size="large"
            maxlength="6"
          />
        </NFormItem>

        <NButton
          type="primary"
          size="large"
          :loading="submitting"
          block
          attr-type="submit"
        >
          <template #icon>
            <NIcon><KeyOutline /></NIcon>
          </template>
          {{ submitting ? t('auth.loggingIn') : t('auth.loginButton') }}
        </NButton>
      </NForm>

      <div class="login-footer">
        <NText depth="3">GNAS · .NET 10 · Container-Native</NText>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
}
.login-card {
  width: 400px;
  max-width: 90vw;
  padding: 40px;
  background: var(--zs-bg-card);
  border: 1px solid var(--zs-border);
  border-radius: 12px;
}
.login-header {
  text-align: center;
  margin-bottom: 32px;
}
.login-logo {
  margin-bottom: 12px;
}
.login-title {
  margin: 0;
  font-size: 28px;
  font-weight: 800;
  color: var(--zs-text-primary);
  letter-spacing: 0.05em;
}
.login-subtitle {
  margin: 6px 0 0;
  font-size: 14px;
  color: var(--zs-text-tertiary);
}
.login-footer {
  text-align: center;
  margin-top: 24px;
}
</style>
