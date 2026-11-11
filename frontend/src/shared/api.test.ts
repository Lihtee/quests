import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiUrl } from './api'

vi.mock('../features/auth/firebase', () => ({ auth: { currentUser: null } }))

describe('apiUrl', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('returns the path unchanged when no base URL is set', () => {
    vi.stubEnv('VITE_API_BASE_URL', '')
    expect(apiUrl('/api/sync')).toBe('/api/sync')
  })

  it('prefixes the base URL', () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://x.example')
    expect(apiUrl('/api/sync')).toBe('https://x.example/api/sync')
  })

  it('does not double the slash when the base URL ends with one', () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://x.example/')
    expect(apiUrl('/api/sync')).toBe('https://x.example/api/sync')
  })
})
