// ============================================================================
// FortOS Dashboard — Form Validators
// Validation rules used across create/edit forms.
// ============================================================================

/**
 * Validates that a string is non-empty after trimming.
 */
export function required(value: string): boolean {
  return value.trim().length > 0
}

/**
 * Returns an error message if the value is empty, otherwise null.
 */
export function requiredMessage(value: string, label: string): string | null {
  return required(value) ? null : `${label} 不能为空`
}

/**
 * Validates a Linux filesystem path.
 */
export function isValidPath(value: string): boolean {
  return /^\/([a-zA-Z0-9._\-\s()]+[\\/])*[a-zA-Z0-9._\-\s()]*$/.test(value)
}

/**
 * Validates a hostname or IP address string.
 */
export function isValidHost(value: string): boolean {
  return value.trim().length > 0 && value.length <= 253
}

/**
 * Validates that a port number is in the valid range.
 */
export function isValidPort(port: number): boolean {
  return Number.isInteger(port) && port >= 1 && port <= 65535
}

/**
 * Validates a cron expression or interval pattern.
 */
export function isValidSchedule(value: string): boolean {
  if (!value) return false
  if (value.startsWith('interval:')) {
    const minutes = parseInt(value.split(':')[1], 10)
    return !isNaN(minutes) && minutes >= 1 && minutes <= 1440
  }
  // Allow HH:mm format
  const timeMatch = value.match(/^(\d{1,2}):(\d{2})$/)
  if (timeMatch) {
    const h = parseInt(timeMatch[1], 10)
    const m = parseInt(timeMatch[2], 10)
    return h >= 0 && h <= 23 && m >= 0 && m <= 59
  }
  return false
}

/**
 * Validates an email address format.
 */
export function isValidEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

/**
 * Validates password minimum strength requirements.
 */
export function isStrongPassword(value: string): boolean {
  return value.length >= 8
}
