// ============================================================================
// FortOS Dashboard — Vue Router Configuration
// Defines all application routes with lazy-loaded view components.
// ============================================================================

import { createRouter, createWebHashHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

/** Route definitions. All views are lazy-loaded for code splitting. */
const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/LoginView.vue'),
    meta: { requiresAuth: false, title: '登录' },
  },
  {
    path: '/',
    component: () => import('@/components/layout/AppLayout.vue'),
    meta: { requiresAuth: true },
    children: [
      {
        path: '',
        name: 'Dashboard',
        component: () => import('@/views/DashboardView.vue'),
        meta: { title: '总览' },
      },
      {
        path: 'files',
        name: 'Files',
        component: () => import('@/views/FilesView.vue'),
        meta: { title: '文件管理' },
      },
      {
        path: 'storage',
        name: 'Storage',
        component: () => import('@/views/StorageView.vue'),
        meta: { title: '存储管理' },
      },
      {
        path: 'sharing',
        name: 'Sharing',
        component: () => import('@/views/SharingView.vue'),
        meta: { title: '文件共享' },
      },
      {
        path: 'backup',
        name: 'Backup',
        component: () => import('@/views/BackupView.vue'),
        meta: { title: '备份管理' },
      },
      {
        path: 'snapshots',
        name: 'Snapshots',
        component: () => import('@/views/SnapshotsView.vue'),
        meta: { title: '快照管理' },
      },
      {
        path: 'agents',
        name: 'Agents',
        component: () => import('@/views/AgentsView.vue'),
        meta: { title: 'Agent 管理' },
      },
      {
        path: 'network',
        name: 'Network',
        component: () => import('@/views/NetworkView.vue'),
        meta: { title: '网络管理' },
      },
      {
        path: 'services',
        name: 'Services',
        component: () => import('@/views/ServicesView.vue'),
        meta: { title: '服务管理' },
      },
      {
        path: 'logs',
        name: 'Logs',
        component: () => import('@/views/LogsView.vue'),
        meta: { title: '日志审计' },
      },
      {
        path: 'alerts',
        name: 'Alerts',
        component: () => import('@/views/AlertsView.vue'),
        meta: { title: '告警中心' },
      },
      {
        path: 'settings',
        name: 'Settings',
        component: () => import('@/views/SettingsView.vue'),
        meta: { title: '系统设置' },
      },
    ],
  },
  {
    // Catch-all redirect.
    path: '/:pathMatch(.*)*',
    redirect: '/',
  },
]

const router = createRouter({
  // Use hash history so the SPA works correctly when served as static files
  // behind ASP.NET Core at /dashboard.
  history: createWebHashHistory(),
  routes,
})

/**
 * Navigation guard: redirect unauthenticated users to login.
 */
router.beforeEach((to, _from, next) => {
  // DEV: skip auth when backend has security.require_auth disabled.
  if (import.meta.env.DEV) {
    next()
    return
  }

  const auth = useAuthStore()

  if (to.meta.requiresAuth !== false && !auth.isAuthenticated) {
    next({ name: 'Login', query: { redirect: to.fullPath } })
  } else if (to.name === 'Login' && auth.isAuthenticated) {
    next({ name: 'Dashboard' })
  } else {
    next()
  }
})

/**
 * After navigation, update the document title.
 */
router.afterEach((to) => {
  const title = (to.meta.title as string) ?? 'FortOS Dashboard'
  document.title = `${title} — FortOS`
})

export { router }
