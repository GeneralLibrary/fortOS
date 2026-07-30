// ============================================================================
// GORT Dashboard — Auth Store
// Manages authentication state: login, logout, token persistence,
// and automatic token refresh scheduling.
// ============================================================================

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { login as apiLogin, refreshToken as apiRefreshToken } from '@/api/auth'
import { getHealth } from '@/api/health'
import type { LoginRequest, NasTokenPayload } from '@/types'
import { ApiError } from '@/api/client'

const TOKEN_KEY = 'gort_token'
const PAYLOAD_KEY = 'gort_payload'

export const useAuthStore = defineStore('auth', () => {
  // ---- State ----

  /** Current Bearer token. Persisted in localStorage. */
  const token = ref<string | null>(localStorage.getItem(TOKEN_KEY))

  /** Decoded token payload. */
  const payload = ref<NasTokenPayload | null>(loadPayload())

  /** Whether a login request is in flight. */
  const loading = ref(false)

  /** Last login error message. */
  const error = ref<string | null>(null)

  /** Interval ID for automatic token refresh. */
  let refreshTimer: ReturnType<typeof setInterval> | null = null

  // ---- Getters ----

  /** Whether the user is currently authenticated. */
  const isAuthenticated = computed(() => token.value !== null && payload.value !== null)

  /** Whether the token is expired. */
  const isExpired = computed(() => {
    if (!payload.value?.exp) return true
    return new Date(payload.value.exp).getTime() <= Date.now()
  })

  /** Current username (subject). */
  const username = computed(() => payload.value?.sub ?? null)

  /** User capabilities list. */
  const capabilities = computed(() => payload.value?.capabilities?.abilities ?? [])

  // ---- Actions ----

  /**
   * Load token payload from localStorage.
   */
  function loadPayload(): NasTokenPayload | null {
    try {
      const raw = localStorage.getItem(PAYLOAD_KEY)
      return raw ? JSON.parse(raw) : null
    } catch {
      return null
    }
  }

  /**
   * Persist authentication to localStorage.
   */
  function persist(newToken: string, newPayload: NasTokenPayload): void {
    token.value = newToken
    payload.value = newPayload
    localStorage.setItem(TOKEN_KEY, newToken)
    localStorage.setItem(PAYLOAD_KEY, JSON.stringify(newPayload))
  }

  /**
   * Clear authentication state.
   */
  function clear(): void {
    token.value = null
    payload.value = null
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(PAYLOAD_KEY)
    stopRefreshTimer()
  }

  /**
   * Authenticate with username/password.
   * Sets token and payload on success, throws ApiError on failure.
   */
  async function authenticate(credentials: LoginRequest): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const response = await apiLogin(credentials)
      persist(response.token, response.payload)
      startRefreshTimer()
    } catch (e) {
      if (e instanceof ApiError) {
        error.value = e.message
      } else {
        error.value = '登录失败，请检查网络连接'
      }
      throw e
    } finally {
      loading.value = false
    }
  }

  /**
   * Log out and clear all auth state.
   */
  function logout(): void {
    clear()
  }

  /**
   * Refresh the current token. Called automatically on a timer.
   */
  async function refresh(): Promise<void> {
    if (!token.value) return
    try {
      const response = await apiRefreshToken()
      if (payload.value) {
        persist(response.token, payload.value)
      }
    } catch {
      // Token refresh failed — user will need to re-login.
      clear()
    }
  }

  /**
   * Start periodic token refresh (checks every 10 minutes).
   */
  function startRefreshTimer(): void {
    stopRefreshTimer()
    refreshTimer = setInterval(refresh, 10 * 60 * 1000)
  }

  /**
   * Stop the refresh timer.
   */
  function stopRefreshTimer(): void {
    if (refreshTimer) {
      clearInterval(refreshTimer)
      refreshTimer = null
    }
  }

  /**
   * Initialize the store. If a persisted token exists, try to refresh it.
   * If the backend is unreachable, keep the persisted token and hope it's still valid.
   */
  async function initialize(): Promise<boolean> {
    if (!token.value) return false
    try {
      await getHealth()
      return true
    } catch {
      // Backend not reachable; but the token might still be valid.
      // Keep the persisted token for now.
      return isAuthenticated.value && !isExpired.value
    }
  }

  return {
    // state
    token,
    payload,
    loading,
    error,
    // getters
    isAuthenticated,
    isExpired,
    username,
    capabilities,
    // actions
    authenticate,
    logout,
    refresh,
    initialize,
  }
})
