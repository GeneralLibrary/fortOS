import { get, post, put, del } from './client'
import { useAuthStore } from '@/stores/auth'
import { apiUrl } from './client'
import type {
  ManagedFileEntry, ManagedFileStat, ManagedFileContent, ManagedDeleteResult,
  Page, UploadSession,
} from '@/types'

export function listFiles(path: string, recursive = false, signal?: AbortSignal): Promise<ManagedFileEntry[]> {
  return get<ManagedFileEntry[]>('/api/files', { path, recursive }, signal)
}

export function listFilesPage(path: string, offset = 0, limit = 100): Promise<Page<ManagedFileEntry>> {
  return get<Page<ManagedFileEntry>>('/api/files/page', { path, recursive: false, offset, limit })
}

export function statFile(path: string): Promise<ManagedFileStat> {
  return get<ManagedFileStat>('/api/files/stat', { path })
}

export function readFileContent(path: string, encoding = 'text'): Promise<ManagedFileContent> {
  return get<ManagedFileContent>('/api/files/content', { path, encoding })
}

export function createDirectory(path: string): Promise<ManagedFileStat> {
  return post<ManagedFileStat>('/api/files/mkdir', { path })
}

export function writeFile(path: string, content: string, overwrite = false, encoding = 'text'): Promise<ManagedFileStat> {
  return post<ManagedFileStat>('/api/files/write', { path, content, encoding, overwrite })
}

export function updateFile(path: string, content: string): Promise<ManagedFileStat> {
  return put<ManagedFileStat>('/api/files/content', { path, content, encoding: 'text' })
}

export function moveFile(sourcePath: string, destinationPath: string, overwrite = false): Promise<ManagedFileStat> {
  return post<ManagedFileStat>('/api/files/move', { sourcePath, destinationPath, overwrite })
}

export function copyFile(sourcePath: string, destinationPath: string, overwrite = false): Promise<ManagedFileStat> {
  return post<ManagedFileStat>('/api/files/copy', { sourcePath, destinationPath, overwrite })
}

export function deleteFile(path: string, hard = false): Promise<ManagedDeleteResult> {
  return del<ManagedDeleteResult>(`/api/files?path=${encodeURIComponent(path)}&hard=${hard}`)
}

export function restoreFile(recyclePath: string, targetPath: string): Promise<ManagedFileStat> {
  return post<ManagedFileStat>('/api/files/restore', { recyclePath, targetPath })
}

export function createUpload(path: string, sizeBytes?: number, sha256?: string): Promise<UploadSession> {
  return post<UploadSession>('/api/files/uploads', { path, sizeBytes, sha256 })
}

export function getUploadSession(sessionId: string): Promise<UploadSession> {
  return get<UploadSession>(`/api/files/uploads/${encodeURIComponent(sessionId)}`)
}

/**
 * Download a file as a Blob with an Authorization header.
 * Used for media preview (<video>/<img>) where the browser cannot attach JWT
 * to a raw src URL. The caller is responsible for URL.revokeObjectURL.
 */
export async function downloadBlob(path: string, signal?: AbortSignal): Promise<Blob> {
  const authStore = useAuthStore()
  const headers: Record<string, string> = {}
  if (authStore.token) {
    headers['Authorization'] = `Bearer ${authStore.token}`
  }

  const params = new URLSearchParams()
  params.set('path', path)

  const response = await fetch(apiUrl('/api/files/download', params), { headers, signal })

  if (!response.ok) {
    const text = await response.text().catch(() => '')
    throw new Error(text || `Download failed (${response.status})`)
  }

  return response.blob()
}

export function abortUpload(sessionId: string): Promise<void> {
  return del(`/api/files/uploads/${encodeURIComponent(sessionId)}`)
}

/**
 * Append a binary chunk to a resumable upload session.
 * Uses raw fetch (not the JSON client) because the body is a binary Blob.
 * Content-Range follows the standard HTTP format: "bytes {start}-{end}/{total}".
 */
export async function appendUpload(
  sessionId: string,
  chunk: Blob,
  start: number,
  total: number,
): Promise<UploadSession> {
  const authStore = useAuthStore()
  const end = start + chunk.size - 1
  const headers: Record<string, string> = {
    'Content-Range': `bytes ${start}-${end}/${total}`,
    // Explicitly set octet-stream so ASP.NET Core does not default to
    // form-urlencoded parsing (which fails on binary bodies with
    // "Form value count limit 1024 exceeded").
    'Content-Type': 'application/octet-stream',
  }
  if (authStore.token) {
    headers['Authorization'] = `Bearer ${authStore.token}`
  }

  const response = await fetch(
    apiUrl(`/api/files/uploads/${encodeURIComponent(sessionId)}`),
    { method: 'PUT', headers, body: chunk },
  )

  if (!response.ok) {
    const text = await response.text().catch(() => '')
    throw new Error(text || `Upload chunk failed (${response.status})`)
  }

  return response.json() as Promise<UploadSession>
}

/**
 * Finalize a resumable upload session — atomically moves the temp file to the target path.
 */
export async function finalizeUpload(sessionId: string): Promise<ManagedFileStat> {
  const authStore = useAuthStore()
  const headers: Record<string, string> = {}
  if (authStore.token) {
    headers['Authorization'] = `Bearer ${authStore.token}`
  }

  const response = await fetch(
    apiUrl(`/api/files/uploads/${encodeURIComponent(sessionId)}/finalize`),
    { method: 'POST', headers },
  )

  if (!response.ok) {
    const text = await response.text().catch(() => '')
    throw new Error(text || `Upload finalize failed (${response.status})`)
  }

  return response.json() as Promise<ManagedFileStat>
}
