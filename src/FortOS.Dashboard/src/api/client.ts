// ============================================================================
// FortOS Dashboard — HTTP Client
// Centralized HTTP client with automatic auth token injection,
// error normalization, and request/response interceptors.
// ============================================================================

import { useAuthStore } from '@/stores/auth'

/** Base URL for API requests. Empty string means same-origin. */
const BASE_URL = ''

/** Custom error class carrying the server's problem-detail fields. */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code?: string,
    public readonly traceId?: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

/** Standard RFC 7807 problem detail shape returned by FortOS. */
interface ProblemDetails {
  title?: string
  status?: number
  detail?: string
  code?: string
  traceId?: string
}

/** Builds a fully qualified URL for the given API path. */
function apiUrl(path: string, params?: URLSearchParams): string {
  const url = new URL(`${BASE_URL}${path}`, window.location.origin)
  if (params) {
    params.forEach((value, key) => {
      if (value !== undefined && value !== null && value !== '') {
        url.searchParams.set(key, value)
      }
    })
  }
  return url.toString()
}

/** Core request function. Automatically attaches the Bearer token. */
async function request<T>(
  method: string,
  path: string,
  options?: {
    body?: unknown
    params?: URLSearchParams
    headers?: Record<string, string>
    signal?: AbortSignal
  },
): Promise<T> {
  const authStore = useAuthStore()
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...options?.headers,
  }

  if (authStore.token) {
    headers['Authorization'] = `Bearer ${authStore.token}`
  }

  const response = await fetch(apiUrl(path, options?.params), {
    method,
    headers,
    body: options?.body !== undefined ? JSON.stringify(options.body) : undefined,
    signal: options?.signal,
  })

  if (response.status === 204) return undefined as T

  let data: unknown
  const contentType = response.headers.get('content-type') ?? ''
  if (contentType.includes('application/json')) {
    data = await response.json()
  } else {
    data = await response.text()
  }

  if (!response.ok) {
    const problem = (data as ProblemDetails) ?? {}
    throw new ApiError(
      problem.detail ?? problem.title ?? `HTTP ${response.status}`,
      response.status,
      problem.code,
      problem.traceId,
    )
  }

  return data as T
}

export function get<T>(
  path: string,
  params?: Record<string, string | number | boolean | undefined | null>,
  signal?: AbortSignal,
): Promise<T> {
  const searchParams = new URLSearchParams()
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        searchParams.set(key, String(value))
      }
    })
  }
  return request<T>('GET', path, { params: searchParams, signal })
}

export function post<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  return request<T>('POST', path, { body, signal })
}

export function put<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  return request<T>('PUT', path, { body, signal })
}

export function del<T>(path: string, signal?: AbortSignal): Promise<T> {
  return request<T>('DELETE', path, { signal })
}

export { apiUrl }
