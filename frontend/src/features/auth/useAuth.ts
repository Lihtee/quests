import { useCallback, useEffect, useState } from 'react'
import { onAuthStateChanged, type User } from 'firebase/auth'
import { auth } from './firebase'
import { signInWithGoogle, signOutEverywhere } from './googleSignIn'

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
      await signInWithGoogle()
    } catch (error) {
      console.error('[useAuth] sign-in failed', error)
      throw error
    }
  }, [])

  const signOut = useCallback(() => signOutEverywhere(), [])

  return { user, loading, signIn, signOut }
}
