import { useEffect, useState, type ReactNode } from 'react'
import { ApiError, apiGet } from '../../shared/api'
import { openUserDb, readMeta, writeMeta, type QuestsDb } from '../../shared/db'
import { useSync } from '../sync/useSync'
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

  // key: remount (new local database, refetch /api/me) when a different user signs in
  return <SignedIn key={user.uid} uid={user.uid} onSignOut={() => void signOut()} />
}

/** GET /api/me and cache it; on a network error (not an API error) use the cached value when there is one. */
async function loadMe(db: QuestsDb): Promise<MeState> {
  try {
    const me = await apiGet<Me>('/api/me')
    await writeMeta(db, 'me', me)
    return { kind: 'ok', me }
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) return { kind: 'not-invited' }

    if (import.meta.env.DEV) console.error('[AuthGate] GET /api/me failed', error)
    const cached = error instanceof ApiError ? undefined : await readMeta<Me>(db, 'me').catch(() => undefined)
    if (cached) return { kind: 'ok', me: cached }
    return { kind: 'error', message: error instanceof Error ? error.message : 'Unexpected error' }
  }
}

function SignedIn({ uid, onSignOut }: { uid: string; onSignOut: () => void }) {
  const [db] = useState(() => openUserDb(uid))
  const [state, setState] = useState<MeState>({ kind: 'loading' })

  useEffect(() => {
    let active = true
    void loadMe(db).then((next) => {
      if (active) setState(next)
    })
    return () => {
      active = false
    }
  }, [db])

  const retry = () => {
    setState({ kind: 'loading' })
    void loadMe(db).then(setState)
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
        <Synced db={db}>
          <header className="app-header">
            <span>{state.me.email}</span>
            {signOutButton}
          </header>
          <TasksPage db={db} />
        </Synced>
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

/** Keeps the user's local database in sync while mounted. */
function Synced({ db, children }: { db: QuestsDb; children: ReactNode }) {
  useSync(db)
  return <>{children}</>
}
