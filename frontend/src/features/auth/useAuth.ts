import { useCallback, useEffect, useState } from 'react'
import {
  GoogleAuthProvider,
  onAuthStateChanged,
  signInWithPopup,
  signOut as firebaseSignOut,
  type User,
} from 'firebase/auth'
import { auth } from './firebase'

export interface AuthState {
  user: User | null
  loading: boolean
  signIn: () => Promise<void>
  signOut: () => Promise<void>
}

/** Firebase auth state + Google sign-in/sign-out. */
export function useAuth(): AuthState {
  const [user, setUser] = useState<User | null>(auth.currentUser)
  const [loading, setLoading] = useState(true)

  useEffect(
    () =>
      onAuthStateChanged(auth, (next) => {
        if (import.meta.env.DEV) {
          console.debug('[useAuth] auth state changed', { signedIn: next !== null })
        }
        setUser(next)
        setLoading(false)
      }),
    [],
  )

  const signIn = useCallback(async () => {
    try {
      await signInWithPopup(auth, new GoogleAuthProvider())
    } catch (error) {
      console.error('[useAuth] sign-in failed', error)
      throw error
    }
  }, [])

  const signOut = useCallback(() => firebaseSignOut(auth), [])

  return { user, loading, signIn, signOut }
}
