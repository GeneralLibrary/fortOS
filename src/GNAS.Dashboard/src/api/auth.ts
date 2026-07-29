import { post } from './client'
import type { LoginRequest, LoginResponse, RegisterRequest, ActionSuccessResponse } from '@/types'

export function login(request: LoginRequest): Promise<LoginResponse> {
  return post<LoginResponse>('/api/auth/login', request)
}

export function register(request: RegisterRequest): Promise<ActionSuccessResponse> {
  return post<ActionSuccessResponse>('/api/auth/register', request)
}

export function refreshToken(): Promise<{ token: string }> {
  return post<{ token: string }>('/api/auth/refresh')
}
