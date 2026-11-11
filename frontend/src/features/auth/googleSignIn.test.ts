import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  isNative: false,
  pluginSignIn: vi.fn(),
  pluginSignOut: vi.fn(),
  signInWithCredential: vi.fn(),
  signInWithPopup: vi.fn(),
  firebaseSignOut: vi.fn(),
  credential: vi.fn((idToken: string) => ({ idToken })),
}))

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: () => mocks.isNative,
    getPlatform: () => (mocks.isNative ? 'android' : 'web'),
  },
}))

vi.mock('@capacitor-firebase/authentication', () => ({
  FirebaseAuthentication: { signInWithGoogle: mocks.pluginSignIn, signOut: mocks.pluginSignOut },
}))

vi.mock('firebase/auth', () => {
  class GoogleAuthProvider {
    static credential = mocks.credential
  }
  return {
    GoogleAuthProvider,
    signInWithCredential: mocks.signInWithCredential,
    signInWithPopup: mocks.signInWithPopup,
    signOut: mocks.firebaseSignOut,
  }
})

vi.mock('./firebase', () => ({ auth: { name: 'test-auth' } }))

const { signInWithGoogle, signOutEverywhere } = await import('./googleSignIn')

describe('googleSignIn', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.isNative = false
  })

  it('native: signs in the web SDK with the plugin ID token', async () => {
    mocks.isNative = true
    mocks.pluginSignIn.mockResolvedValue({ credential: { idToken: 'google-id-token' } })

    await signInWithGoogle()

    expect(mocks.pluginSignIn).toHaveBeenCalledOnce()
    expect(mocks.credential).toHaveBeenCalledWith('google-id-token')
    expect(mocks.signInWithCredential).toHaveBeenCalledWith({ name: 'test-auth' }, { idToken: 'google-id-token' })
    expect(mocks.signInWithPopup).not.toHaveBeenCalled()
  })

  it('native: rejects without an ID token and does not sign in', async () => {
    mocks.isNative = true
    mocks.pluginSignIn.mockResolvedValue({ credential: null })
    vi.spyOn(console, 'error').mockImplementation(() => {})

    await expect(signInWithGoogle()).rejects.toThrow('no ID token')

    expect(mocks.signInWithCredential).not.toHaveBeenCalled()
  })

  it('web: uses the popup only', async () => {
    await signInWithGoogle()

    expect(mocks.signInWithPopup).toHaveBeenCalledOnce()
    expect(mocks.pluginSignIn).not.toHaveBeenCalled()
    expect(mocks.signInWithCredential).not.toHaveBeenCalled()
  })

  it('native sign-out: signs out of the plugin and Firebase', async () => {
    mocks.isNative = true

    await signOutEverywhere()

    expect(mocks.pluginSignOut).toHaveBeenCalledOnce()
    expect(mocks.firebaseSignOut).toHaveBeenCalledWith({ name: 'test-auth' })
  })
})
