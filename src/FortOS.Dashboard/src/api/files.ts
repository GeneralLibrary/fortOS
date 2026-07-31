import { get, post, put, del } from './client'
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

export function abortUpload(sessionId: string): Promise<void> {
  return del(`/api/files/uploads/${encodeURIComponent(sessionId)}`)
}
