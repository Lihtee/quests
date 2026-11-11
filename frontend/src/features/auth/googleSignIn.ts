import { Capacitor } from '@capacitor/core'
import { FirebaseAuthentication } from '@capacitor-firebase/authentication'
import {
  GoogleAuthProvider,
  signInWithCredential,
  signInWithPopup,
  signOut as firebaseSignOut,
} from 'firebase/auth'
import { auth } from './firebase'

function debug(message: string, detail?: unknown): void {
  if (import.meta.env.DEV) console.debug(`[auth] ${message}`, detail ?? '')
}

/**
 * Google sign-in for the Firebase web SDK. Android: native account picker (plugin with skipNativeAuth) returns a
 * Google ID token, which signs in the web SDK via signInWithCredential. Web: signInWithPopup. Never logs tokens.
 */
export async function signInWithGoogle(): Promise<void> {
  const platform = Capacitor.getPlatform()
  debug('sign-in', { platform })

  if (!Capacitor.isNativePlatform()) {
    await signInWithPopup(auth, new GoogleAuthProvider())
    return
  }

  try {
    const result = await FirebaseAuthentication.signInWithGoogle()
    const idToken = result.credential?.idToken
    debug('native credential received', { hasIdToken: Boolean(idToken) })
    if (!idToken) throw new Error('Google sign-in returned no ID token')

    await signInWithCredential(auth, GoogleAuthProvider.credential(idToken))
  } catch (error) {
    console.error('[auth] native sign-in failed', error)
    throw error
  }
}

/** Signs out of Firebase; on Android also of the native Google session, so the account picker shows next time. */
export async function signOutEverywhere(): Promise<void> {
  try {
    if (Capacitor.isNativePlatform()) {
      debug('native sign-out')
      await FirebaseAuthentication.signOut()
    }
  } finally {
    // Always sign out of the web SDK, even if the native sign-out failed.
    await firebaseSignOut(auth)
  }
}
