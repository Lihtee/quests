import { useEffect, useState } from 'react'
import { ApiError, apiGet } from '../../shared/api'
import { TasksPage } from '../tasks/TasksPage'
import { useAuth } from './useAuth'

interface Me {
  id: string
  email: string
}

type MeState =
  | { kind: 'loading' }
  | { kind: 'ok'; me: Me }
  | { kind: 'not-invited' }
  | { kind: 'error'; message: string }

/** Sign-in screen when signed out; otherwise checks access via GET /api/me. */
export function AuthGate() {
  const { user, loading, signIn, signOut } = useAuth()

  if (loading) return <p>Loading…</p>

  if (!user) {
    return (
      <button type="button" onClick={() => void signIn().catch(() => undefined)}>
        Sign in with Google
      </button>
    )
  }

  // key: remount (and refetch /api/me) when a different user signs in
  return <SignedIn key={user.uid} onSignOut={() => void signOut()} />
}

async function loadMe(): Promise<MeState> {
  try {
    return { kind: 'ok', me: await apiGet<Me>('/api/me') }
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) return { kind: 'not-invited' }

    if (import.meta.env.DEV) console.error('[AuthGate] GET /api/me failed', error)
    return { kind: 'error', message: error instanceof Error ? error.message : 'Unexpected error' }
  }
}

function SignedIn({ onSignOut }: { onSignOut: () => void }) {
  const [state, setState] = useState<MeState>({ kind: 'loading' })

  useEffect(() => {
    let active = true
    void loadMe().then((next) => {
      if (active) setState(next)
    })
    return () => {
      active = false
    }
  }, [])

  const retry = () => {
    setState({ kind: 'loading' })
    void loadMe().then(setState)
  }

  const signOutButton = (
    <button type="button" onClick={onSignOut}>
      Sign out
    </button>
  )

  switch (state.kind) {
    case 'loading':
      return <p>Loading…</p>
    case 'ok':
      return (
        <>
          <header className="app-header">
            <span>{state.me.email}</span>
            {signOutButton}
          </header>
          <TasksPage />
        </>
      )
    case 'not-invited':
      return (
        <>
          <p>You are not invited</p>
          {signOutButton}
        </>
      )
    case 'error':
      return (
        <>
          <p>Could not load your account: {state.message}</p>
          <button type="button" onClick={retry}>
            Retry
          </button>
          {signOutButton}
        </>
      )
  }
}
